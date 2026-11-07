# StudyGate

开机第一件事是答题，答不到 90 分不给桌面。

Windows 专用。三科（语文 / 数学 / 英语），云南教材（部编版语文、人教版数学、PEP 与 Go for it 英语），六年级下起，往上盖初一、初二、初三。

## 机制

`HKLM\...\Winlogon\Shell` 从 `explorer.exe` 换成 `StudyGate.exe --shell`。登录后系统拉起的不是桌面，是答题门。

- 不可跳过：没有「退出」「稍后」「跳过」按钮；30 题答完才判定
- 不可取消：底层键盘钩子吞掉 `Esc`、`Win`、`Alt+F4`、`Alt+Tab`、`Ctrl+Shift+Esc`、`Ctrl+Esc`、`F12`；窗口 `Closing` 一律拦下
- 不可杀：主进程与守护进程互心跳，被杀 15 秒内重启；计划任务每 30 秒复查一次
- 只判不说：界面不显示实时分数，交卷才知道，堵掉「试出答案」的路
- 隐蔽通道：存在一条管理员口令通道，安装时由你设置。界面无任何提示、无按钮、无热键说明

达到 90 分 → 静默启动 `explorer.exe`，本进程退出，当日内不再拦。

## 为什么是这个组合

GitHub 上相关项目调研结论：

| 仓库 | 拿走的 | 没拿的 |
|---|---|---|
| `Quantazeroplus/Exam-Lockdown` | `Winlogon\Shell` 劫持 + 教师口令恢复 | 只有锁、没有判分 |
| `vedantgangrade21/StudyLock` | 计划任务每 30 秒复查 + 隐藏 + 单实例 | 靠自杀式互杀，易破 |
| `Kimdaewon/ISMS-Quiz-Lock` | `keyboard.add_hotkey(..., suppress=True)` 底层吞键 | 要装 Python 与 keyboard 包，纯净机不可用 |

合并成一套 .NET 单体，运行期零外部依赖。判分内核与 Windows 层彻底分开，判分逻辑能在任何平台跑测试。

## 构建

需要 Windows + .NET 8 SDK。

```powershell
cd StudyGate
dotnet build -c Release
dotnet run --project src\Core\Tests       # 判分内核断言，不依赖 Windows
node data\bank\validate.mjs               # 题库 schema 与配额校验
```

产物在 `src\App\bin\Release\net8.0-windows\`。

## 安装

```powershell
# 管理员 PowerShell
.\scripts\install.ps1
```

过程：校验题库 → 输入并确认管理员口令（PBKDF2-HMAC-SHA256，200k 迭代）→ 备份当前 Shell 值 → 写 `Winlogon\Shell` → 注册计划任务 `StudyGateAutostart` → 提示重启。

重启后直接进答题门。

## 卸载与回滚

安全阀优先于一切：

```powershell
# 管理员 PowerShell，不需要口令
.\scripts\uninstall.ps1
```

做三件事：还原 `Winlogon\Shell`（优先读 `shell.bak`，缺失则写回 `explorer.exe`）、删计划任务、删 `%ProgramData%\StudyGate`。立刻恢复正常桌面，不用重启。

已被锁在答题门里、又进不去桌面时：`Ctrl+Alt+Del` → 右下角电源键 → 按住 `Shift` 点「重启」→ 进 WinRE → 命令行 → 跑 `uninstall.ps1`。或者用隐蔽口令通道。

## 每次开机流程

1. 登录 → 答题门全屏
2. 30 题：语文 10、数学 10、英语 10（三科固定各 10，约 15 分钟）
3. 答完自动判定，得分 ≥90 放行
4. 未达 90：显示三科分布 + 错题号，只有「重考」
5. 重考换卷（新种子），题目不重复

## 已知限制

- `Ctrl+Alt+Del` 是系统级安全序列，任何用户态程序都拦不住。摘下它之后会回到登录/锁屏界面，重新登录时 `Shell` 仍是 StudyGate，答题门会再起来。要彻底堵，只能改 `LogonUI` 或上 `Ctrl+Alt+Del` 策略关掉任务管理器入口。
- 安全模式会跳过计划任务与部分策略。懂的人能绕。这是对自律工具，不是对攻击者。
- 提权失败时降级为「只锁当前会话」，不会阻断答题门本身。
- 题库内容按主流教材版本编排，不同地市进度差异自行增删。

## 目录

```
src/Core/     判分、状态机、口令保险库、隐蔽通道状态机（无 Windows 依赖）
src/App/      Winlogon Shell、输入钩子、进程守护、计划任务、协调器
src/Ui/       锁屏答题界面（WPF）
data/bank/    题库 JSON + validate.mjs
scripts/      安装、卸载、手工验收
docs/         SPEC（全队契约）、风险说明
tools/        Node 参考实现，用于跨平台对拍判分逻辑
```
