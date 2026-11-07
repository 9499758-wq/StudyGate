using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace StudyGate.App;

/// <summary>
/// 进程守护。门内（Phase 非 Unlocked/PanicBypassed）时阻止桌面与提权工具出现。
///
/// 三层手段，从硬到软：
///   1. Job Object（JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE）：把本进程树钉在一个 job 里，
///      本进程一死，job 内所有子进程一起被内核干掉。这是唯一不依赖轮询的一层。
///   2. 轮询（Timer 3000ms）：explorer 与本类列出的管理工具，在门内一律 kill。
///      轮询是补丁而非常态 —— explorer 由 ShellExit 显式启动、或用户手动点时才会出现。
///   3. IFEO Debugger 软屏蔽：给 taskmgr/cmd/... 的图像执行选项写一个指向不存在程序的
///      Debugger 值，系统去启动它们时会失败。失败降级继续，绝不影响锁屏门本身。
///
/// 本类不做互杀，也不自保（自保归 SelfDefense + 计划任务 30s 复查）。
/// 所有 kill / 注册表写入都吞异常：杀不掉只是"这一轮没成"，不是系统故障。
/// </summary>
internal static class ProcessGuard
{
    private const int TickIntervalMs = 3000;

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private const string IfeoSubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string DebuggerValueName = "Debugger";

    private const int KeyWow64_64Key = 0x0100;

    /// <summary>GetProcessesByName 用不带扩展名的名字。</summary>
    private static readonly string[] BlockedNames =
    {
        "taskmgr",
        "cmd",
        "powershell",
        "pwsh",
        "regedit",
        "mmc",
        "msconfig",
        "control",
    };

    private static Timer? s_timer;
    private static Func<bool>? s_gateClosed;
    private static IntPtr s_job;
    private static int s_tickBusy;
    private static bool s_started;

    // ── kernel32 ──────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateJobObjectW")]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int jobObjectInfoClass,
        ref JobObjectExtendedLimitInformation lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    // ── advapi32（IFEO）──────────────────────────────────────

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegCreateKeyExW")]
    private static extern int RegCreateKeyEx(
        IntPtr hKey,
        string lpSubKey,
        int reserved,
        string? lpClass,
        uint dwOptions,
        int samDesired,
        IntPtr lpSecurityAttributes,
        out IntPtr phkResult,
        out int lpdwDisposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegSetValueExW")]
    private static extern int RegSetValueEx(
        IntPtr hKey,
        string lpValueName,
        int reserved,
        uint dwType,
        byte[] lpData,
        int cbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegDeleteValueW")]
    private static extern int RegDeleteValue(IntPtr hKey, string lpValueName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegOpenKeyExW")]
    private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegQueryValueExW")]
    private static extern int RegQueryValueEx(
        IntPtr hKey,
        string lpValueName,
        IntPtr lpReserved,
        out uint lpType,
        byte[]? lpData,
        ref int lpcbData);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "RegCloseKey")]
    private static extern int RegCloseKey(IntPtr hKey);

    private static readonly IntPtr HkeyLocalMachine = unchecked((IntPtr)0x80000002);
    private const uint RegSzType = 1;
    private const int KeyAllAccess = 0x000F003F;
    private const int KeyReadOnly = 0x00020019;
    private const int ErrorFileNotFound = 2;
    private const int ErrorMoreData = 234;
    private const int ErrorSuccess = 0;

    // ── 结构体（win-x64 自然对齐，CLR Sequential 与 C 布局一致）──

    /// <summary>
    /// JOBOBJECT_BASIC_LIMIT_INFORMATION（64 字节）：
    ///   LARGE_INTEGER@0, LARGE_INTEGER@8, DWORD LimitFlags@16, pad@20,
    ///   SIZE_T@24, SIZE_T@32, DWORD ActiveProcessLimit@40, pad@44,
    ///   ULONG_PTR Affinity@48, DWORD PriorityClass@56, DWORD SchedulingClass@60
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize;
        internal UIntPtr MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    /// <summary>IO_COUNTERS：6 个 ULONGLONG = 48 字节。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    /// <summary>JOBOBJECT_EXTENDED_LIMIT_INFORMATION = 64 + 48 + 8*4 = 144 字节。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit;
        internal UIntPtr JobMemoryLimit;
        internal UIntPtr PeakProcessMemoryUsed;
        internal UIntPtr PeakJobMemoryUsed;
    }

    // ─────────────────────────────────────────────────────────────
    // 启动
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// gateClosed 返回 true 表示仍在门内（可以杀 explorer 与工具）；false 表示已放行。
    /// 幂等：重复调用只替换判定委托。
    /// </summary>
    internal static void Start(Func<bool> gateClosed)
    {
        if (gateClosed is null)
        {
            throw new ArgumentNullException(nameof(gateClosed));
        }

        s_gateClosed = gateClosed;

        if (s_started)
        {
            return;
        }

        s_started = true;

        AttachJob();

        // 退出钩子：定时器回调打在已释放的对象上会抛 ObjectDisposedException，
        // 而 ProcessExit 期间没人接得住这个异常，必须在退出前主动停表。
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        s_timer = new Timer(Tick, null, TickIntervalMs, TickIntervalMs);
    }

    /// <summary>幂等停止：停表、解绑退出钩子、释放 job 句柄。</summary>
    internal static void Stop()
    {
        Timer? timer = Interlocked.Exchange(ref s_timer, null);
        if (timer is not null)
        {
            try
            {
                timer.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        IntPtr job = Interlocked.Exchange(ref s_job, IntPtr.Zero);
        if (job != IntPtr.Zero)
        {
            // 关掉 job 句柄即触发 KILL_ON_JOB_CLOSE：job 内残留子进程随本进程一起走。
            CloseHandle(job);
        }

        if (s_started)
        {
            s_started = false;
            try
            {
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
            }
        }
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        Stop();
    }

    // ─────────────────────────────────────────────────────────────
    // Job Object
    // ─────────────────────────────────────────────────────────────

    private static void AttachJob()
    {
        try
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                // 拿不到 job（极少数被容器/沙箱限制的环境）：降级为纯轮询。
                return;
            }

            JobObjectExtendedLimitInformation info = default;
            info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

            int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            bool set = SetInformationJobObject(
                job,
                JobObjectExtendedLimitInformation,
                ref info,
                unchecked((uint)size));

            if (!set)
            {
                CloseHandle(job);
                return;
            }

            if (!AssignProcessToJobObject(job, GetCurrentProcess()))
            {
                // 已经在别的 job 里（例如被父进程放进 job 的调试环境）会失败。
                // Windows 8+ 允许嵌套 job，但失败不做补救：轮询仍在。
                CloseHandle(job);
                return;
            }

            s_job = job;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException)
        {
            // 非 Windows 或符号缺失：不阻断，交给轮询层。
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 轮询
    // ─────────────────────────────────────────────────────────────

    private static void Tick(object? state)
    {
        // 防重入：一次 tick 若被系统挂住，下一拍不能叠加。
        if (Interlocked.Exchange(ref s_tickBusy, 1) == 1)
        {
            return;
        }

        try
        {
            Func<bool>? probe = Volatile.Read(ref s_gateClosed);
            if (probe is null || !probe())
            {
                return;
            }

            KillByName("explorer");

            foreach (string name in BlockedNames)
            {
                KillByName(name);
            }
        }
        catch (Exception)
        {
            // 回调里绝不能抛：Timer 回调未捕获异常会终结进程。
        }
        finally
        {
            Volatile.Write(ref s_tickBusy, 0);
        }
    }

    private static void KillByName(string name)
    {
        Process[] list;
        try
        {
            list = Process.GetProcessesByName(name);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return;
        }

        foreach (Process p in list)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
            {
                // 权限不足或进程刚好自己退了：这一轮放弃，下一轮再看。
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // IFEO 软屏蔽
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 给 name（带或不带 .exe）写 IFEO Debugger，指向一个不存在的路径。
    /// 系统尝试启动该映像时会转而启动 Debugger 并失败，效果等于"打不开"。
    /// 返回 false = 没写进去（无管理员权限或被安全软件拦），调用方继续，不阻断。
    /// </summary>
    internal static bool AttemptBlock(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string image = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name
            : name + ".exe";

        try
        {
            string subKey = IfeoSubKey + "\\" + image;

            int rc = RegCreateKeyEx(
                HkeyLocalMachine,
                subKey,
                0,
                null,
                0,
                KeyAllAccess | KeyWow64_64Key,
                IntPtr.Zero,
                out IntPtr hKey,
                out _);

            if (rc != ErrorSuccess)
            {
                return false;
            }

            try
            {
                byte[] data = Encoding.Unicode.GetBytes(BlockedImagePath() + "\0");

                rc = RegSetValueEx(hKey, DebuggerValueName, 0, RegSzType, data, data.Length);
                return rc == ErrorSuccess;
            }
            finally
            {
                RegCloseKey(hKey);
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException)
        {
            return false;
        }
    }

    /// <summary>
    /// 清除软屏蔽。卸载路径调用，必须幂等。
    ///
    /// 只删「值恰好是本程序写入的那条假路径」的 Debugger：
    /// IFEO 是全局共享的，别的软件（兼容性修复、调试器、杀软）也会在这里写 Debugger。
    /// 无条件删值等于替别人改配置。同时只做**只读打开**——不存在的键不能被顺手创建出来。
    /// </summary>
    internal static void ClearBlocks()
    {
        string expected = BlockedImagePath();

        foreach (string name in BlockedNames)
        {
            string image = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name
                : name + ".exe";

            try
            {
                if (!TryOpenImageKey(image, out IntPtr hKey))
                {
                    continue;
                }

                try
                {
                    if (!DebuggerValueMatches(hKey, expected))
                    {
                        continue;
                    }

                    int rc = RegDeleteValue(hKey, DebuggerValueName);
                    _ = rc == ErrorSuccess || rc == ErrorFileNotFound;
                }
                finally
                {
                    RegCloseKey(hKey);
                }
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException)
            {
                return;
            }
        }
    }

    /// <summary>值不存在、类型不对、或内容不是本程序写的 → false。</summary>
    private static bool DebuggerValueMatches(IntPtr hKey, string expected)
    {
        int cb = 0;
        int rc = RegQueryValueEx(hKey, DebuggerValueName, IntPtr.Zero, out uint type, null, ref cb);
        if (rc == ErrorFileNotFound || cb <= 0)
        {
            return false;
        }

        if (rc != ErrorSuccess && rc != ErrorMoreData)
        {
            return false;
        }

        if (type != RegSzType)
        {
            return false;
        }

        byte[] buffer = new byte[cb];
        int cbActual = cb;
        rc = RegQueryValueEx(hKey, DebuggerValueName, IntPtr.Zero, out type, buffer, ref cbActual);
        if (rc != ErrorSuccess)
        {
            return false;
        }

        string text = Encoding.Unicode.GetString(buffer, 0, Math.Min(cbActual, buffer.Length));
        int nul = text.IndexOf('\0');
        if (nul >= 0)
        {
            text = text.Substring(0, nul);
        }

        return string.Equals(text, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>只读打开已存在的 IFEO 映像键；键不存在返回 false，绝不创建。</summary>
    private static bool TryOpenImageKey(string image, out IntPtr hKey)
    {
        hKey = IntPtr.Zero;

        try
        {
            int rc = RegOpenKeyEx(
                HkeyLocalMachine,
                IfeoSubKey + "\\" + image,
                0,
                KeyReadOnly | KeyWow64_64Key,
                out IntPtr opened);

            if (rc != ErrorSuccess)
            {
                return false;
            }

            hKey = opened;
            return true;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException)
        {
            return false;
        }
    }

    /// <summary>软屏蔽指向的假路径。这个文件永远不会被创建，所以启动必然失败。</summary>
    private static string BlockedImagePath()
    {
        return GatePaths.DataDir + "\\blocked-guard.exe";
    }

    /// <summary>本类被 Program 调用的批量入口。</summary>
    internal static void ApplyBlocks()
    {
        foreach (string name in BlockedNames)
        {
            AttemptBlock(name);
        }
    }
}
