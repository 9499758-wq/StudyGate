# 跨层契约（lead 拍板，不可协商）

本文件补充 SPEC 第一节/第三节。**所有签名逐字照抄，不要自己发明名字。**
任何一方想改，先问 lead，改完同步更新本文件。

---

## A. `GateState` 精确成员（core-kernel 用，lock-shell 也依赖）

`src/Core/GateState.cs`：

```csharp
namespace StudyGate.Core;

public enum GatePhase { Locked = 0, QuizReady = 1, QuizInProgress = 2, Unlocked = 3, PanicBypassed = 4 }

public sealed class GateState
{
    public GatePhase Phase { get; set; } = GatePhase.Locked;

    /// <summary>解锁截止时间（UTC）。null = 未解锁。</summary>
    public DateTimeOffset? UnlockUntilUtc { get; set; }

    /// <summary>
    /// 放行那一刻的系统启动时刻（UTC）。语义：只有"同一次开机"内才认这份放行。
    /// 计算式：DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64)。
    /// 重启后 tick 归零，算出的 bootUtc 与上次记录的必然相差数小时。
    /// null = 从来没成功放行过（或老存档），判定侧一律视为"不在窗口内"。
    /// </summary>
    public DateTimeOffset? UnlockBootUtc { get; set; }

    /// <summary>当前年级。6 = 六年级。7/8/9 = 初一/初二/初三。</summary>
    public int Grade { get; set; } = 6;

    /// <summary>"上" 或 "下"。</summary>
    public string Term { get; set; } = "下";

    /// <summary>当日已尝试次数（本地日期）。</summary>
    public int AttemptsToday { get; set; }

    /// <summary>最后一次尝试的 UTC 时间。从未尝试为 DateTimeOffset.MinValue。</summary>
    public DateTimeOffset LastAttemptUtc { get; set; } = DateTimeOffset.MinValue;

    /// <summary>隐蔽通道连续错误次数。成功后归零。</summary>
    public int PasswordFailCount { get; set; }

    /// <summary>隐蔽通道锁定截止（UTC）。null = 未锁。</summary>
    public DateTimeOffset? PasswordLockedUntilUtc { get; set; }

    /// <summary>当前卷种子。重考时换新种子。0 = 未初始化。</summary>
    public int PaperSeed { get; set; }

    /// <summary>本卷题目 id 顺序。空 = 无进行中的卷。</summary>
    public List<string> CurrentPaperIds { get; set; } = new();

    /// <summary>本卷已选答案，按 CurrentPaperIds 下标对齐。-1 = 未答。</summary>
    public List<int> CurrentAnswers { get; set; } = new();

    /// <summary>当前题游标，0-based。</summary>
    public int CurrentIndex { get; set; }

    /// <summary>
    /// 最近若干次作答记录，最新在前。
    /// 第 14 字段，序列化位置固定排 canonical 末尾 —— 老 state.json 缺这一段时按空表宽容。
    /// 条数上限由 App 层（Coordinator.HistoryMax = 30）维护，Core 不硬编码。
    /// </summary>
    public List<AttemptRecord> History { get; set; } = new();
}

public sealed record AttemptRecord(
    DateTimeOffset AtUtc, int Grade, string Term, int Score, int PassScore,
    bool Passed, int CorrectCount, int TotalCount, long ElapsedMs,
    IReadOnlyList<int> SubjectCorrect)
{
    /// <summary>SubjectCorrect 的长度恒为 3，下标即 Subject。</summary>
    public const int SubjectCount = 3;
}
```

序列化：**手写 canonical JSON，不用 `System.Text.Json`**（`src/Core/GateStateCodec.cs`）。
字段序固定为 `phase, unlockUntilUtc, unlockBootUtc, grade, term, attemptsToday, lastAttemptUtc,
passwordFailCount, passwordLockedUntilUtc, paperSeed, currentPaperIds, currentAnswers, currentIndex, history`。
时间格式统一 `yyyy-MM-dd'T'HH:mm:ss.fff'Z'`，null 写裸 `null`，无空格无缩进。
之所以不用 `JsonSerializer`：字段顺序、时间精度、转义规则任一字节偏差都会让 HMAC 验签失败，
而 `tools/core-reference.mjs` 的 Node 侧 `canonicalJson()` 是按这个顺序逐字节对齐的。

签名：`state.json` 磁盘原始字节的 UTF-8 做 HMAC-SHA256，存 `state.json.sig`（Base64，无换行）。
注意验的是**磁盘上的原始字节**，不是重新序列化的结果 —— 所以追加字段不会让存量存档失效：
老文件的签名是当时按 12 字段算的，正文没被动过就照旧通过，只有重新 `Save` 才升级成 14 字段格式。

`GateState` 没有口令相关字段 —— 口令哈希在 `vault.dat`，不在状态里。

---

## B. `IGateViewHost` 精确契约（ui-screen 实现，lock-shell 消费）

`src/Ui/IGateViewHost.cs`：

```csharp
namespace StudyGate.Ui;

public interface IGateViewHost
{
    /// <summary>装载一份新卷。gradeLabel 形如 "六年级下"。调用后视图应显示第一题。</summary>
    void LoadPaper(PaperSpec spec, IReadOnlyList<Question> paper, string gradeLabel);

    /// <summary>切到指定题。answeredCount 是已答题数（含当前已选），用于底部 "已答 N"。</summary>
    void ShowQuestion(int index, int answeredCount, int selectedIndex);

    /// <summary>显示结果页。视图内部决定达标/未达标的呈现，3 秒倒计时达标时触发 PassedCountdownElapsed。</summary>
    void ShowResult(QuizResult result);

    /// <summary>宿主强制切走（解锁后）。视图不再拦截 Closing。</summary>
    void ReleaseAndClose();

    /// <summary>用户选中某个选项。参数为 0..3 的选项索引。</summary>
    event Action<int>? OptionSelected;

    /// <summary>用户答完最后一题或主动交卷。宿主收到后判分。</summary>
    event Action? SubmitRequested;

    /// <summary>结果页点"重考"。</summary>
    event Action? RetakeRequested;

    /// <summary>达标后的 3 秒静默倒计时结束。宿主据此调 ShellExit。</summary>
    event Action? PassedCountdownElapsed;
}
```

## C. `GateWindow` 精确构造面（lock-shell 这样用）

`GateWindow : Window, IGateViewHost`，**无参构造**，`InitializeComponent()` 之后即可注入。

lock-shell 的 Coordinator 写法必须是这个形状：

```csharp
var win = new GateWindow();
win.OptionSelected        += idx => coordinator.OnOptionSelected(idx);
win.SubmitRequested       += ()  => coordinator.OnSubmitRequested();
win.RetakeRequested       += ()  => coordinator.OnRetakeRequested();
win.PassedCountdownElapsed+= ()  => shellExit.UnlockAndStartExplorer();
App.Current.MainWindow = win;
win.Show();
win.LoadPaper(spec, paper, gradeLabel);
```

约束：
- `GateWindow` 不自己读盘、不自己看 Core 的 store、不订阅键盘钩子。
- `LoadPaper` 时 `paper` 的 `Count` 必须等于 `spec.TotalQuestions`，否则抛 `ArgumentException`（视图侧防御）。
- `ShowQuestion` 的 `selectedIndex` 为 -1 表示该题尚未作答。
- `ReleaseAndClose()` 之后 `Closing` 不再取消，视图调 `this.Close()`。
- 事件在无人订阅时为 `null`，视图内部必须 null 检查（`OptionSelected?.Invoke(i)`）。

## D. 视觉与文案（ui-screen 必须照做）

- 顶部小签文案：`$"StudyGate · {gradeLabel}"`，13px，`#7C8088`。
- 底部左：`$"{index + 1:D2} / {total:D2}"`；右：`$"已答 {answeredCount:D2}"`。
- **不显示任何分数、正确率、剩余时间。**
- 结果页未达标主标题：`未达标 · 仍需 90 分`；达标：`已达标 · {score} 分`。
- 结果页三科分布格式：`语文 {c}/{t} · 数学 {c}/{t} · 英语 {c}/{t}`。
- 错题号区域标题：`错题`（不足时留空，不写"全对"）。
- 唯一按钮未达标态文案：`重考`。达标态无按钮。
- 右下角版本行：`v1.0.7`，`#7C8088`，点击无任何反应。

---

## E. `Program.cs` 命令行分流（lead 已定，lock-shell 按此实现）

| 参数 | 行为 |
|---|---|
| `--shell` | 锁屏主路径。加载 Core + Ui，进答题门。 |
| `--watchdog` | 守护进程。只做心跳互检与拉起，不创建窗口、不加载 Ui。 |
| `--install --set-password` | 从环境变量 `STUDYGATE_SET_PWD` 读口令，写 `vault.dat`，输出退出码。**不创建窗口**，控制台静默。 |
| `--install` | 完整安装：`--set-password` + `WlxShell.Install` + `TaskRegistrar.Register` + 复制题库到 `%ProgramData%\StudyGate\bank`。 |
| `--uninstall` | 还原 Shell + 删任务 + 清数据。不要求口令。 |
| 无参数 | 控制台交互式安装向导（管理员自检 → 输口令 → 写 Shell → 注册任务）。**不要 WPF 向导**，理由：安装期桌面还没接管，控制台最稳且便于脚本化。 |

`--install --set-password` 是 install.ps1 唯一依赖的入口，必须能用：读环境变量、PBKDF2、写盘、返回 0。失败返回非 0 并在 stderr 写一行原因。

---

## F. 隐蔽口令数据流（唯一允许的路径）

```
InputLock（WH_KEYBOARD_LL，未吞掉的字符）
   → char
   → HiddenChannel.Feed(char)          // Core，纯状态机
   → 命中：返回候选口令字符串
   → Coordinator 校验 Vault.Verify(候选)
   → 成功：GateState.Phase = PanicBypassed，UnlockUntilUtc = now + 24h，落盘
          → ShellExit.UnlockAndStartExplorer()
   → 失败：PasswordFailCount++，>=5 则 PasswordLockedUntilUtc = now + 1h，落盘
```

- **UI 层完全不参与**。不允许 UI 监听键盘做口令匹配。
- 口令命中与失败都**无任何视觉/听觉反馈**，只在成功时静默放行。
- 口令字符串用完立刻置 null，不进日志、不进 state.json。
- 通道锁定时 `Feed` 直接返回 null 不累积缓冲区。
