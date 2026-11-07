using System;
using System.Diagnostics;
using System.IO;
using StudyGate.Core;

namespace StudyGate.App;

internal enum UnlockKind
{
    /// <summary>答满 90 分放行。解锁窗口默认到当日 23:59。</summary>
    Passed = 0,

    /// <summary>隐蔽口令放行。窗口固定 now + 24h（SPEC 第五节硬要求）。</summary>
    Panic = 1,
}

/// <summary>
/// 放行动作。全工程只有这里可以直接把桌面还回去。
///
/// 两条铁律：
/// 1. 落盘失败也要放行。state.json 写不进去（磁盘满、权限丢、杀软锁文件）时若拒绝启动
///    explorer.exe，机器就永久停在答题门 —— 变砖的代价远大于少记一天门禁。
/// 2. 先 Process.Start 再 Environment.Exit。不能走 Application.Shutdown()：Ui 的 Closing
///    在 Phase=Unlocked 之外一律 e.Cancel=true，Shutdown 会被拦下，进程留在内存里
///    继续持有全局钩子，explorer 与答题门会互相抢桌面。
/// </summary>
internal static class ShellExit
{
    /// <summary>配置环境变量：解锁截止时刻（本地 HH:mm）。缺省 23:59。</summary>
    private const string CutoffEnv = "STUDYGATE_UNLOCK_HHMM";

    private static readonly TimeSpan DefaultCutoff = new TimeSpan(23, 59, 0);

    /// <summary>
    /// 判定两只 bootUtc 是否属于同一次开机。
    ///
    /// 为什么不用 DllImport GetTickCount64：.NET 6 起 Environment.TickCount64 就是同一个
    /// 系统计数器（自开机以来的毫秒数，重启归零），原生实现，无需 Interop。
    ///
    /// 容忍 2 分钟：进程启动到读 tick、以及 tick 自身的 10~16ms 精度，
    /// 都会让「同一时刻」算出的 bootUtc 有几毫秒漂移；而两次不同开机之间至少差几分钟
    /// （重启至少要走完固件+引导）。2 分钟是这两者的安全分隔。
    /// </summary>
    private static readonly TimeSpan BootTolerance = TimeSpan.FromMinutes(2);

    /// <summary>本次开机的启动时刻（UTC）。由 tick 反推，与系统时钟是否被人改过无关。</summary>
    internal static DateTimeOffset BootUtc() =>
        DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// 当前 state 是否仍处在放行窗口内（进程被计划任务拉起时的快速通道）。
    ///
    /// 两个条件同时成立才算：<b>时间没到</b> 且 <b>还是同一次开机</b>。
    /// 后者是用户「只要电脑重启就出现一次做题」的硬需求 —— 只看时间的话，
    /// 上午答完晚上重启就直接进桌面，一次题都不出。
    /// </summary>
    internal static bool IsWithinUnlockWindow(GateState state)
    {
        if (state.Phase != GatePhase.Unlocked && state.Phase != GatePhase.PanicBypassed)
        {
            return false;
        }

        DateTimeOffset? until = state.UnlockUntilUtc;
        if (!until.HasValue || until.Value <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        // 老存档 / 从没成功放行过：没有 bootUtc 就无法证明是同一次开机，一律重开门。
        DateTimeOffset? recorded = state.UnlockBootUtc;
        if (!recorded.HasValue)
        {
            return false;
        }

        TimeSpan drift = BootUtc() - recorded.Value;
        if (drift < TimeSpan.Zero)
        {
            drift = drift.Negate();
        }
        return drift <= BootTolerance;
    }

    /// <summary>
    /// 已在放行窗口内：只启动桌面，不再改状态。
    /// 用于「计划任务每 30s 拉起一次，但今天已经答过了」这条最常见的路径。
    /// </summary>
    internal static void StartExplorer()
    {
        StartExplorerCore();
        Environment.Exit(0);
    }

    /// <summary>落盘解锁状态 → 启动 explorer.exe → 退出本进程。</summary>
    internal static void UnlockAndStartExplorer(IGateStore store, GateState state, UnlockKind kind)
    {
        state.Phase = kind == UnlockKind.Panic ? GatePhase.PanicBypassed : GatePhase.Unlocked;
        state.UnlockUntilUtc = kind == UnlockKind.Panic
            ? DateTimeOffset.UtcNow.AddHours(24)
            : NextCutoffUtc();

        // 把「这次放行属于哪一次开机」钉下来。重启后 bootUtc 归零重算，
        // 与这里记录的差出数小时，IsWithinUnlockWindow 立刻失效 —— 门重新打开。
        // 两个放行渠道都写：口令通道 24 小时窗口同样不该跨重启。
        state.UnlockBootUtc = BootUtc();

        if (kind == UnlockKind.Panic)
        {
            // 口令通道成功即清零失败计数（CONTRACTS F）。
            state.PasswordFailCount = 0;
            state.PasswordLockedUntilUtc = null;
        }

        try
        {
            store.Save(state);
        }
        catch (IOException)
        {
            // 见文件头铁律 1：吞掉，继续放行。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }

        StartExplorerCore();
        Environment.Exit(0);
    }

    /// <summary>
    /// 下一次「当日 HH:mm」的 UTC 时刻。
    /// 若当前已过该时刻（例如 23:59:30 交卷），顺延到次日同一时刻：
    /// 返回一个已经过去的时间等于没解锁，计划任务下一次拉起会立刻重开门。
    /// </summary>
    private static DateTimeOffset NextCutoffUtc()
    {
        TimeSpan at = DefaultCutoff;

        string? configured = Environment.GetEnvironmentVariable(CutoffEnv);
        if (!string.IsNullOrEmpty(configured)
            && TimeSpan.TryParse(configured, out TimeSpan parsed)
            && parsed >= TimeSpan.Zero
            && parsed < TimeSpan.FromDays(1))
        {
            at = parsed;
        }

        DateTime nowLocal = DateTime.Now;
        DateTime cutoffLocal = nowLocal.Date + at;
        if (cutoffLocal <= nowLocal)
        {
            cutoffLocal = cutoffLocal.AddDays(1);
        }

        return new DateTimeOffset(cutoffLocal, TimeZoneInfo.Local.GetUtcOffset(cutoffLocal));
    }

    private static void StartExplorerCore()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
            };
            Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // explorer 起不来（被 IFEO 软屏蔽、或路径异常）：本进程仍要退出，
            // 让计划任务 30 秒后重新拉起一次，不要留在内存里跟锁屏门死锁。
        }
    }
}
