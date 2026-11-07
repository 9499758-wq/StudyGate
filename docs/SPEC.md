# StudyGate 规格书 v1

开机前置答题门。答不到 90 分不给桌面。有隐蔽口令通道。不可跳过、不可键盘取消。

## 参考基线（GitHub 调研结论）

| 仓库 | 可复用什么 | 不可直接抄什么 |
|---|---|---|
| `Quantazeroplus/Exam-Lockdown` | `Winlogon\Shell` 劫持 + `explorer.exe` 终止的批处理思路、教师口令恢复 | 只锁不判分，无题库 |
| `vedantgangrade21/StudyLock` | `harden.ps1` 的计划任务每分钟复查 + 隐藏窗口 + `MultipleInstances IgnoreNew` | 靠自杀式 watchdog 互保，单点易破 |
| `Kimdaewon/ISMS-Quiz-Lock` | `quizlock.py` 的底层键盘钩子 `keyboard.add_hotkey(..., suppress=True)` + `block_key('win')` | 必须装 Python 与 `keyboard` 包，纯净机不可用 |

结论：**判分门 + Shell 前置 + 内核级按键吞掉 + 计划任务复查**四件套合并成一套 .NET 单体，避免运行期依赖。

## 目录归属（写入域，互不重叠）

```
src/Core/      ← core-quiz      纯类库，零 Windows 依赖，可跨平台单测
src/App/       ← lock-shell     仅 Windows，P/Invoke、钩子、Shell、计划任务
src/Ui/        ← ui-screen      锁屏视觉与答题交互（WPF XAML + 代码后置）
data/bank/     ← question-bank  题库 JSON，按年级/科目分文件
scripts/       ← lead           安装、卸载、回滚
docs/          ← lead           本规格、README、风险
tools/         ← lead           行为校验脚本（Node 侧参考实现）
```

任何人的改动不跨出自己那一行。Lead 独占 `StudyGate.sln` 与 `docs/`。

## 一、核心契约（lead 定死，所有人按此对齐）

`src/Core/StudyGate.Core.csproj` — `net8.0`，`Nullable=enable`，无任何第三方包。

```csharp
namespace StudyGate.Core;

public enum Subject { Chinese = 0, Math = 1, English = 2 }

public sealed record Question(
    string Id,
    Subject Subject,
    int Grade,                 // 6 下 = 6；初一 = 7；初二 = 8；初三 = 9
    string Term,               // "下" / "上"
    string Stem,
    IReadOnlyList<string> Options,
    int AnswerIndex,
    string Explanation);

public sealed record PaperSpec(
    int Grade, string Term,
    int TotalQuestions,        // 每次 30
    int PassScore,             // 90
    int PerSubject);           // 每科题数下限，30 题时 = 10（三科各 10，无轮换）

public sealed record AnswerRecord(string QuestionId, int SelectedIndex, bool Correct, long ElapsedMs);

public sealed record SubjectScore(Subject Subject, int Correct, int Total);

public sealed record QuizResult(
    int Score, int PassScore, bool Passed,
    IReadOnlyList<SubjectScore> BySubject,
    IReadOnlyList<string> WrongQuestionIds,
    long TotalElapsedMs);

public sealed class QuestionBank {
    public QuestionBank(IEnumerable<Question> questions);
    public static QuestionBank LoadFromDirectory(string dir);      // 读 data/bank/*.json
    public int Count { get; }
    public IReadOnlyList<Question> Pick(PaperSpec spec, int seed);  // 确定性抽题，同 seed 同卷
}

public sealed class Grader {
    public static QuizResult Score(IReadOnlyList<Question> paper, IReadOnlyList<AnswerRecord> answers, int passScore);
}

public enum GatePhase { Locked, QuizReady, QuizInProgress, Unlocked, PanicBypassed }

public sealed class GateState { /* Phase + UnlockUntilUtc + Grade/Term + Attempts */ }

public interface IGateStore {
    GateState Load();
    void Save(GateState state);        // 落盘前做 HMAC 签名，读时校验
}

public sealed class FileGateStore : IGateStore { public FileGateStore(string dir, byte[] hmacKey); }

public sealed class Vault {
    public static Vault Create(string password, byte[] salt);          // PBKDF2-SHA256, 200k iter
    public bool Verify(string password);                              // 常量时间比较
}

public sealed class HiddenChannel {   // 隐蔽口令解析器，与 UI 无关，可单测
    // 输入流中 \x01 前缀 + 口令 + \r 触发；返回 null 表示不是口令序列
    public string? Feed(char c);
}
```

硬性逻辑要求：
1. `Grader` 必须支持**部分题未作答**（`SelectedIndex < 0` 或缺失记录 = 错），不得抛异常。
2. `Pick` 必须每科命中 `PerSubject`，且同 `seed` 输出完全一致（可重放测试）。
3. `GateState.Save` 写盘用「临时文件 + `File.Replace`」，断电不产生半截状态。
4. `Vault.Verify` 用 `CryptographicOperations.FixedTimeEquals`，禁止 `==`。
5. 全程无日志、无 Console 输出、无 `Debug.WriteLine`（商品机不当间谍软件）。

## 二、锁屏接管（`src/App`，仅 Windows）

目标：开机 → `winlogon` 拉起本程序作为 shell → 全屏不可切 → 答满 90 分 → 才启动 `explorer.exe`。

必须实现：
1. `WlxShell` — 读 `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell`，安装时写 `StudyGate.exe --shell`，卸载时还原原值（默认 `explorer.exe`）。备份到 `%ProgramData%\StudyGate\shell.bak`。
2. `InputLock` — 全局低级键盘钩子（`WH_KEYBOARD_LL`）+ 鼠标钩子（`WH_MOUSE_LL`），白名单放行：字母数字、方向键、回车、退格、空格、F5 刷新题。吞掉：`Esc`、`Alt+F4`、`Alt+Tab`、`Win`（`VK_LWIN/VK_RWIN`）、`Ctrl+Alt+Del`（拦截 `Winlogon` 热键不可行，改为进程 `SetThreadExecutionState` + `Ctrl+Alt+Del` 后锁屏回来自动续锁）、`Ctrl+Shift+Esc`、`Ctrl+Esc`。
3. `ProcessGuard` — 禁用 `taskmgr.exe` / `cmd.exe` / `powershell.exe` / `regedit.exe` / `mmc.exe` / `msconfig.exe` 的本进程树创建（`WH_CBT` 或 Job 对象），并把名字加入 AppLocker 不可用时的软屏蔽（`IFEO Debugger` 置空 `dword:0`）。
4. `SelfDefense` — 双进程：`StudyGate.exe` 与 `StudyGate.Watchdog.exe`（同一 exe 带 `--watchdog`），互发心跳，任一被杀由计划任务 30s 内拉起；`AllowMultipleInstances` 关闭。
5. `TaskRegistrar` — 注册 `StudyGateAutostart`（`AtLogOn` + 每 30s 重复，`RunLevel=Highest`，`Hidden`，`ExecutionTimeLimit=0`），与 StudyLock 的 `harden.ps1` 同构但收敛为一次调用。
6. `ShellExit` — 通过验证后 `Process.Start("explorer.exe")`，本进程 `Exit`；并写入 `state.Phase=Unlocked` + 解锁窗口（默认到当日 23:59，可配 `unlockUntil`）。
7. `Escalation` — 所有需要提权的动作走 `runas` 提示一次，失败则降级为「仅锁当前会话」（不阻断锁屏门本身）。

禁止：不要写 `Winlogon\Shell` 到 `userinit`，不要把 `ScreenSaver` 改成锁屏，不要动 `LogonUI`。

## 三、锁屏界面（`src/Ui`）

- 视觉：深灰近黑（`#101114`）底 + 极窄字距中文字体 + 单色强调（`#C8A24A` 沙金）。无渐变、无发光、无 emoji、无胶囊。目标像一张实体答题卡，不像 AI 生成页。
- 结构：顶部一行「StudyGate · 六年级下」灰色小字（不写"AI""智能"）；中部题干大字；下方 4 个选项卡片；底部进度 `07 / 30` 与累计分（**只显示"已答题数"，不显示实时分数**，防试探）。
- 交卷：第 30 题答完后自动判定。`<90`：显示「未达标 · 仍需 90 分」+ 三科分布 + 错题号，仅一个按钮「重考」。无「退出」「跳过」「稍后」。
- 交卷后禁止任何键盘取消：`PreviewKeyDown` 吞掉 `Esc`，窗口 `Closing` 事件 `e.Cancel = true` 直到 `Phase=Unlocked`。
- 隐蔽通道 UI：右下角一行 `v1.0.7` 版本文本，点击无反应；实际口令从 `InputLock` 的原始键盘流截获（不经 UI），UI 不做任何视觉反馈，直到解锁才静默切走。**界面上不出现任何提示、问号、锁图标。**
- 兼容：`1920x1080` 与 `1366x768` 都不得溢出；不支持 DPI 缩放错位。

## 四、题库（`data/bank/`）

文件：`g{grade}{term}-{subject}.json`，例 `g6下-chinese.json`。

```json
{ "grade": 6, "term": "下", "subject": "chinese",
  "items": [ { "id": "g6x-cn-001", "stem": "...", "options": ["A","B","C","D"],
               "answer": 0, "explanation": "..." } ] }
```

内容基线（云南，人教/部编版）：
- 六年级下：语文（《北京的春节》《腊八粥》《藏戏》《鲁滨逊漂流记》《骑鹅旅行记》《古诗三首》《十六年前的回忆》《真理诞生于一百个问号之后》）；数学（负数、圆柱与圆锥、比例、统计与概率、整理复习）；英语（PEP 六下 Unit1 How tall are you / Unit2 Last weekend / Unit3 Where did you go / Unit4 Then and now）。
- 初一（七上/七下）：语文（《春》《济南的冬天》《雨的四季》《古代诗歌四首》《秋天的怀念》《从百草园到三味书屋》）；数学（有理数、整式、一元一次方程、几何图形初步、相交线与平行线、实数、二元一次方程组、不等式）；英语（Go for it 七上/七下）。
- 初二（八上/八下）：语文（《消息二则》《首届诺贝尔奖颁发》《三峡》《答谢中书书》《背影》《中国石拱桥》）；数学（全等三角形、轴对称、分式、二次根式、勾股定理、一次函数）；英语（八上/八下）。
- 初三（九上/九下）：语文（《岳阳楼记》《醉翁亭记》《诗词三首》《故乡》《我的叔叔于勒》《送东阳马生序》）；数学（一元二次方程、二次函数、旋转、圆、相似、锐角三角函数）；英语（九全）。

数量要求：每 `(grade, term, subject)` ≥ 40 题，含解析；选项 4 个，答案索引 0..3；`id` 全局唯一；不得出现教材版本冲突题（云南用人教版数学、部编版语文、PEP 英语）。

## 五、隐蔽口令（用户硬需求）

- 口令设置：安装时由用户输入一次，仅存 PBKDF2 哈希（`salt` 16B，`iter` 200k，`SHA256`）。
- 触发：任意锁屏页，键盘输入序列 `\x01` + 口令 + `\r`。`\x01` 在 GUI 层不作任何显示（吞掉，光标不动）。
- 效果：`Phase=PanicBypassed`，`UnlockUntilUtc = now + 24h`，静默放行 explorer，无任何对话框、无音效。
- 防猜：连续 5 次错误 → 锁定该通道 1 小时（记在 `state.json`，UI 不显示）。
- 不可从界面发现：无按钮、无热键提示、无关于页说明；README 也不写具体组合（只写"存在管理员口令通道，见安装时设置"）。

## 六、验收

- `dotnet build -c Release` 零警告（`TreatWarningsAsErrors`）。
- `src/Core` 单测覆盖：判分边界（89/90/91）、未作答、同 seed 同卷、篡改状态被 HMAC 拒绝、口令错误 5 次锁 1h、隐蔽序列不误触。
- 手工验收脚本 `scripts/verify.ps1`：装 → 重启 → 看不到桌面 → 连错 → 90 分放行 → 卸载还原 shell。
- 回滚：`scripts/uninstall.ps1` 必须能还原 `Winlogon\Shell`、删计划任务、删 `%ProgramData%\StudyGate`，且**不需要口令**（安全阀，写进 README）。
