using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace StudyGate.App;

/// <summary>
/// 双进程互心跳：主进程（--shell）与守护进程（--watchdog）互相盯着，任一方消失由另一方拉起。
/// 真正的兜底仍是 TaskRegistrar 的计划任务（30 秒复查），本类只是在会话内缩短空窗期。
///
/// 为什么用命名 EventWaitHandle 而不是命名管道：
///   - 只需要"对方还活着"这一个比特，不需要传数据；
///   - 管道的建立/断开是异步的，进程被杀时服务端要等 PipeBroken 才能感知，延迟不可控；
///   - EventWaitHandle 的 WaitOne 超时语义精确到毫秒，且跨会话（Global\）可用。
///
/// fork 炸弹防护：
///   互拉是天然的放大器（A 拉 B，B 拉 A，各自再拉一次就翻倍）。因此：
///   1. 每次 spawn 前先在命名 Mutex 保护下把计数写进 spawn.count；
///   2. 计数 > 5 一律不再 spawn，只在内存里标记 —— 让计数器文件留着供人工排查；
///   3. 计数在成功放行（ShellExit）后不清零；它的含义是"本次开机被拉起过几次"，
///      由计划任务在机器重启后自然归零（文件在 %ProgramData%，重启不删）。
///      这是刻意的：重启后若还要靠互拉才能活，说明安装本身坏了，应当让它停在门外而不是
///      无限重启。
/// </summary>
internal static class SelfDefense
{
    private const string GateBeatName = @"Global\StudyGate.Heartbeat.Gate";
    private const string WatchdogBeatName = @"Global\StudyGate.Heartbeat.Watchdog";
    private const string SpawnCountMutexName = @"Global\StudyGate.SpawnCounter";
    private const string SpawnCountFileName = "spawn.count";

    private const int BeatIntervalMs = 1000;
    private const int WaitSliceMs = 1500;
    private const int MissingToleranceMs = 15000;
    private const int MaxSpawn = 5;

    private static Thread? s_guardThread;
    private static volatile bool s_guardStop;

    // ─────────────────────────────────────────────────────────────
    // 主进程侧：起后台线程盯 watchdog
    // ─────────────────────────────────────────────────────────────

    /// <summary>由 Program 在确认要放门之后调用一次。重复调用无副作用。</summary>
    internal static void StartGuardian()
    {
        if (s_guardThread is not null)
        {
            return;
        }

        if (!TryOpenPair(out EventWaitHandle own, out EventWaitHandle peer))
        {
            // 心跳对象建不出来（权限/命名冲突）：不做守护，计划任务仍然兜底。
            return;
        }

        s_guardThread = new Thread(() => GuardLoop(own, peer))
        {
            IsBackground = true,
            Name = "StudyGate.Guard",
        };
        s_guardThread.Start();
    }

    internal static void StopGuardian()
    {
        s_guardStop = true;
        s_guardThread?.Join(TimeSpan.FromSeconds(1));
    }

    private static void GuardLoop(EventWaitHandle own, EventWaitHandle peer)
    {
        int missingMs = 0;

        try
        {
            while (!s_guardStop)
            {
                own.Set();
                bool alive = peer.WaitOne(WaitSliceMs);

                if (alive)
                {
                    missingMs = 0;
                    continue;
                }

                missingMs += WaitSliceMs + BeatIntervalMs;
                if (missingMs < MissingToleranceMs)
                {
                    continue;
                }

                missingMs = 0;
                SpawnPeer("--watchdog");
            }
        }
        finally
        {
            own.Dispose();
            peer.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 守护进程侧（--watchdog）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 守护进程主循环。返回进程退出码。不创建任何窗口、不加载 Ui、不碰题库。
    /// mutexName 由 Program 传入，用于保证只有一个 watchdog。
    /// </summary>
    internal static int RunWatchdog(string mutexName)
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                // 已有守护进程在跑：静默退出（这也是 --watchdog 可以被反复拉起的原因）。
                return 0;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        try
        {
            if (!TryOpenPair(out EventWaitHandle own, out EventWaitHandle peer))
            {
                return 1;
            }

            int missingMs = 0;

            try
            {
                while (true)
                {
                    // watchdog 自己的拍子：写 Watchdog 心跳，读 Gate 心跳。
                    own.Set();

                    bool alive = peer.WaitOne(WaitSliceMs);
                    if (alive)
                    {
                        missingMs = 0;
                        continue;
                    }

                    missingMs += WaitSliceMs + BeatIntervalMs;
                    if (missingMs < MissingToleranceMs)
                    {
                        continue;
                    }

                    missingMs = 0;
                    SpawnPeer("--shell");
                }
            }
            finally
            {
                own.Dispose();
                peer.Dispose();
            }
        }
        catch (Exception)
        {
            return 1;
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 已释放或未持有：退出路径上的重复释放，忽略。
            }

            mutex.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 心跳对象
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// own = 本角色要写的那个；peer = 本角色要等的那个。
    /// 名字按角色固定，因此主进程与 watchdog 交换后正好对上。
    /// </summary>
    private static bool TryOpenPair(out EventWaitHandle own, out EventWaitHandle peer)
    {
        own = null!;
        peer = null!;

        string selfName = IsWatchdogRole() ? WatchdogBeatName : GateBeatName;
        string peerName = IsWatchdogRole() ? GateBeatName : WatchdogBeatName;

        try
        {
            own = new EventWaitHandle(false, EventResetMode.AutoReset, selfName);
            peer = new EventWaitHandle(false, EventResetMode.AutoReset, peerName);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            try
            {
                own?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            own = null!;
            peer = null!;
            return false;
        }
    }

    /// <summary>同一个 exe 带不同参数启动，只能靠命令行判断角色。</summary>
    private static bool IsWatchdogRole()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--watchdog", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────
    // 拉起对方（带 spawn 上限）
    // ─────────────────────────────────────────────────────────────

    private static void SpawnPeer(string argument)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return;
        }

        if (!TryBumpSpawnCount())
        {
            // 超过上限：不再重启。留一个空文件供 verify.ps1 检查"互相拉起已熔断"。
            return;
        }

        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            psi.ArgumentList.Add(argument);
            Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // 拉不起来就等计划任务；这里绝不能抛到消息循环外。
        }
    }

    /// <summary>返回 false 表示已达上限，不要再 spawn。</summary>
    private static bool TryBumpSpawnCount()
    {
        Mutex? counter = null;
        try
        {
            counter = new Mutex(initiallyOwned: false, SpawnCountMutexName);
            if (!counter.WaitOne(TimeSpan.FromSeconds(2)))
            {
                // 抢不到锁：宁可不 spawn，也不要无锁写计数导致计数被覆盖后无限重启。
                return false;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        try
        {
            GatePaths.EnsureDataDir();
            string path = Path.Combine(GatePaths.DataDir, SpawnCountFileName);

            int count = 0;
            try
            {
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path).Trim();
                    int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
                }
            }
            catch (IOException)
            {
                count = 0;
            }

            if (count >= MaxSpawn)
            {
                return false;
            }

            count++;
            try
            {
                File.WriteAllText(path, count.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 写不进计数就当作已到上限：无法记账的自动重启等于 fork 炸弹。
                return false;
            }

            return true;
        }
        finally
        {
            try
            {
                counter.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            counter.Dispose();
        }
    }
}
