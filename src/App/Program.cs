using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using StudyGate.Core;

namespace StudyGate.App;

/// <summary>
/// 进程入口。命令分流按 docs/CONTRACTS.md E 节。
///
/// 编译闭合依赖（同批/后续批次落盘，本文件只负责接线，不含实现）：
///   GatePaths / ConsoleBridge   —— 本文件
///   Coordinator                 —— 同批
///   ShellExit                   —— 同批
///   SelfDefense                 —— 第二批
///   InputLock / WlxShell / TaskRegistrar —— 第二、三批
///   Escalation / ProcessGuard   —— 第三批
///
/// 单实例策略（重要）：
///   --shell     持有 Global\StudyGate.SingleInstance，避免两个锁屏门同时抢桌面。
///   --watchdog  持有 Global\StudyGate.SingleInstance.Watchdog。
///   两者**必须**能并存（守护进程与被守护进程是同一 exe 的两个角色），
///   所以不能共用同一个 mutex 名，否则守护进程会被自己的主进程挡在门外。
///   install / uninstall / set-password 不持锁，允许脚本并行调用。
/// </summary>
internal static class Program
{
    private const string ScriptPasswordEnv = "STUDYGATE_SET_PWD";
    private const string GateMutexName = @"Global\StudyGate.SingleInstance";
    private const string WatchdogMutexName = @"Global\StudyGate.SingleInstance.Watchdog";

    /// <summary>静态持有，防止被 GC 回收导致 mutex 句柄提前关闭。</summary>
    private static Mutex? s_gateMutex;

    /// <summary>
    /// 全局输入钩子。必须由静态字段持有：钩子过程是实例字段，一旦整个 InputLock
    /// 变成不可达对象，系统手里的函数指针就悬空，桌面输入会卡死。
    /// </summary>
    private static InputLock? s_inputLock;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return RunWizard();
        }

        switch (args[0])
        {
            case "--shell":
                return RunShell();

            case "--watchdog":
                return SelfDefense.RunWatchdog(WatchdogMutexName);

            case "--set-password":
                // 便捷别名，等价于 --install --set-password。
                return SetPasswordOnly();

            case "--install":
                return args.Length > 1 && args[1] == "--set-password"
                    ? SetPasswordOnly()
                    : FullInstall();

            case "--uninstall":
                return Uninstall();

            default:
                return Fail("unknown argument: " + args[0]);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // --shell
    // ─────────────────────────────────────────────────────────────

    private static int RunShell()
    {
        if (!AcquireGate(GateMutexName))
        {
            // 已有锁屏门在跑。第二个实例静默退出，不弹任何东西。
            return 0;
        }

        try
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Coordinator coordinator = Coordinator.CreateDefault();

            // 门必须先于窗口消息循环就位：Run() 内部可能直接放行并 Environment.Exit。
            coordinator.Run();

            // 只在确认需要放门之后才起守护，避免 fail-open 路径残留一个无主进程。
            SelfDefense.StartGuardian();

            // explorer 与提权工具在门内一律杀；判定委托读实时 Phase，放行后自动停手。
            ProcessGuard.Start(() => coordinator.IsGateClosed);

            // 输入压制 + 隐蔽口令唯一入口：未被吞掉的字符逐个喂给 HiddenChannel。
            s_inputLock = new InputLock(coordinator.FeedHiddenChar);
            s_inputLock.Start();

            app.Exit += OnAppExit;

            return app.Run();
        }
        catch (Exception ex)
        {
            // 只写异常类型名，绝不写 message —— 避免把题面或路径带进外部日志。
            return Fail("shell startup failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// WPF 退出收尾。顺序有讲究：
    ///   1. 先摘钩子 —— 钩子线程若还在跑而进程开始拆解，回调会打进已半销毁的对象，
    ///      表现为退出时偶发卡住或进程残留；
    ///   2. 再停 ProcessGuard 与守护 —— ProcessGuard.Stop 会关掉 job 句柄，
    ///      触发 KILL_ON_JOB_CLOSE，把本进程树的残留子进程一起带走。
    /// 这里不用 Environment.Exit：WPF 已经决定退出，强杀会跳过后续 finally 与刷盘。
    /// </summary>
    private static void OnAppExit(object? sender, ExitEventArgs e)
    {
        InputLock? input = Interlocked.Exchange(ref s_inputLock, null);
        if (input is not null)
        {
            input.Dispose();
        }

        ProcessGuard.Stop();
        SelfDefense.StopGuardian();
    }

    // ─────────────────────────────────────────────────────────────
    // --install --set-password
    // ─────────────────────────────────────────────────────────────

    private static int SetPasswordOnly()
    {
        ConsoleBridge.EnsureInherited();

        string? pwd = Environment.GetEnvironmentVariable(ScriptPasswordEnv);
        if (string.IsNullOrEmpty(pwd))
        {
            return Fail("STUDYGATE_SET_PWD is empty");
        }

        try
        {
            GatePaths.EnsureDataDir();
            Vault vault = Vault.Create(pwd, GatePaths.NewSalt());
            File.WriteAllBytes(GatePaths.VaultPath, vault.Export());
            GatePaths.EnsureHmacKey();
            return 0;
        }
        catch (IOException)
        {
            return Fail("cannot write vault.dat");
        }
        catch (UnauthorizedAccessException)
        {
            return Fail("cannot write vault.dat: administrator required");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // --install
    // ─────────────────────────────────────────────────────────────

    private static int FullInstall()
    {
        ConsoleBridge.EnsureInherited();

        if (!Escalation.IsElevated())
        {
            return Fail("install requires administrator");
        }

        GatePaths.EnsureDataDir();
        GatePaths.EnsureHmacKey();

        if (!File.Exists(GatePaths.VaultPath))
        {
            string? pwd = Environment.GetEnvironmentVariable(ScriptPasswordEnv);
            if (string.IsNullOrEmpty(pwd))
            {
                return Fail("vault.dat missing and STUDYGATE_SET_PWD is empty");
            }

            try
            {
                Vault vault = Vault.Create(pwd, GatePaths.NewSalt());
                File.WriteAllBytes(GatePaths.VaultPath, vault.Export());
            }
            catch (IOException)
            {
                return Fail("cannot write vault.dat");
            }
        }

        try
        {
            WlxShell.Install();
        }
        catch (WlxShellException ex)
        {
            return Fail("shell install failed: " + ex.Reason);
        }

        if (!TaskRegistrar.Register(SelfExePath()))
        {
            // Shell 已改但任务没注册：用户登录后仍会被锁，只是少了 30s 保活。
            // 这里按失败返回，让 install.ps1 能看见并重试，而不是假装成功。
            return Fail("task register failed");
        }

        CopyBank();
        return 0;
    }

    /// <summary>本进程可执行文件全路径。ProcessPath 为空时回落到 BaseDirectory。</summary>
    private static string SelfExePath()
    {
        return Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "StudyGate.exe");
    }

    private static void CopyBank()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "bank"),
            Path.Combine(AppContext.BaseDirectory, "data", "bank"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "data", "bank"),
        };

        foreach (string candidate in candidates)
        {
            string source;
            try
            {
                source = Path.GetFullPath(candidate);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (!Directory.Exists(source))
            {
                continue;
            }

            Directory.CreateDirectory(GatePaths.BankDir);
            foreach (string file in Directory.GetFiles(source, "*.json"))
            {
                string target = Path.Combine(GatePaths.BankDir, Path.GetFileName(file));
                File.Copy(file, target, overwrite: true);
            }

            return;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // --uninstall（安全阀：不需要口令）
    // ─────────────────────────────────────────────────────────────

    private static int Uninstall()
    {
        ConsoleBridge.EnsureInherited();

        if (!Escalation.IsElevated())
        {
            return Fail("uninstall requires administrator");
        }

        int exit = 0;

        try
        {
            WlxShell.Restore();
        }
        catch (WlxShellException ex)
        {
            exit = Fail("shell restore failed: " + ex.Reason);
        }

        // Delete() 幂等：任务不存在也返回 true，只有真起不来 schtasks 才为 false。
        if (!TaskRegistrar.Delete() && exit == 0)
        {
            exit = Fail("task unregister failed");
        }

        try
        {
            ProcessGuard.ClearBlocks();
        }
        catch (Exception ex)
        {
            if (exit == 0)
            {
                exit = Fail("ifeo clear failed: " + ex.GetType().Name);
            }
        }

        try
        {
            if (Directory.Exists(GatePaths.DataDir))
            {
                Directory.Delete(GatePaths.DataDir, recursive: true);
            }
        }
        catch (IOException)
        {
            if (exit == 0)
            {
                exit = Fail("cannot delete data directory");
            }
        }
        catch (UnauthorizedAccessException)
        {
            if (exit == 0)
            {
                exit = Fail("cannot delete data directory: administrator required");
            }
        }

        return exit;
    }

    // ─────────────────────────────────────────────────────────────
    // 无参数：控制台交互式向导（不要 WPF 向导，厂牌机装系统时桌面尚未接管）
    // ─────────────────────────────────────────────────────────────

    private static int RunWizard()
    {
        ConsoleBridge.EnsureOwnConsole();

        try
        {
            Console.Out.WriteLine("StudyGate 安装向导");
            Console.Out.WriteLine("==================");

            if (!Escalation.IsElevated())
            {
                Console.Out.WriteLine();
                Console.Out.WriteLine("管理员权限缺失。");
                Console.Out.WriteLine("请以管理员身份打开终端后重新运行本程序，或使用 scripts/install.ps1。");
                return 3;
            }

            string? password = ReadPassword("设置管理员口令（输入不显示，回车结束）: ");
            if (password is null)
            {
                return 5;
            }

            if (password.Length < 6)
            {
                Console.Out.WriteLine("口令至少 6 个字符。安装未做任何变更，可重新运行。");
                return 4;
            }

            GatePaths.EnsureDataDir();
            Vault vault = Vault.Create(password, GatePaths.NewSalt());
            File.WriteAllBytes(GatePaths.VaultPath, vault.Export());
            password = null;

            GatePaths.EnsureHmacKey();
            WlxShell.Install();

            if (!TaskRegistrar.Register(SelfExePath()))
            {
                // Shell 已改、任务未注册：登录仍会被锁，但少了 30s 保活。
                // 向导里直接说清楚，让用户知道要重跑一次，而不是假装成功。
                Console.Out.WriteLine();
                Console.Out.WriteLine("计划任务注册失败。下次登录仍会进入答题门，但没有 30 秒保活。");
                Console.Out.WriteLine("请重新以管理员身份运行本程序以重试注册。");
                return 1;
            }

            CopyBank();

            Console.Out.WriteLine();
            Console.Out.WriteLine("安装完成。下次登录将先进入 StudyGate 答题门。");
            Console.Out.WriteLine("卸载：StudyGate.exe --uninstall（管理员，不需要口令）。");
            return 0;
        }
        catch (WlxShellException ex)
        {
            return Fail("shell install failed: " + ex.Reason);
        }
        catch (IOException)
        {
            return Fail("write failed");
        }
        catch (UnauthorizedAccessException)
        {
            return Fail("administrator required");
        }
    }

    /// <summary>交互式读口令；返回 null 表示当前终端不支持逐字符读取。</summary>
    private static string? ReadPassword(string prompt)
    {
        Console.Out.Write(prompt);
        string pwd = string.Empty;

        try
        {
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (pwd.Length > 0)
                    {
                        pwd = pwd.Substring(0, pwd.Length - 1);
                    }

                    continue;
                }

                char c = key.KeyChar;
                if (c == '\u0000' || char.IsControl(c))
                {
                    continue;
                }

                pwd += c;
            }
        }
        catch (InvalidOperationException)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine("当前终端不支持交互式读字符（stdin 被重定向）。请改用环境变量方式：");
            Console.Out.WriteLine("  STUDYGATE_SET_PWD=口令  StudyGate.exe --install --set-password");
            return null;
        }

        Console.Out.WriteLine();
        return pwd;
    }

    // ─────────────────────────────────────────────────────────────
    // 工具
    // ─────────────────────────────────────────────────────────────

    private static bool AcquireGate(string name)
    {
        try
        {
            s_gateMutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
            if (createdNew)
            {
                return true;
            }

            s_gateMutex.Dispose();
            s_gateMutex = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Global 命名对象被其他用户/其他会话持有，或本进程权限不足以创建。
            s_gateMutex = null;
            return false;
        }
    }

    private static int Fail(string reason)
    {
        ConsoleBridge.EnsureInherited();
        try
        {
            Console.Error.WriteLine(reason);
        }
        catch (IOException)
        {
            // 无 stderr 可用时无处可写；退出码仍然有效。
        }

        return 1;
    }
}

/// <summary>%ProgramData%\StudyGate 下的固定路径。所有落盘位置只从这里来。</summary>
internal static class GatePaths
{
    private const int HmacKeyBytes = 32;
    private const int SaltBytes = 16;

    internal static string DataDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "StudyGate");

    internal static string StatePath => Path.Combine(DataDir, "state.json");
    internal static string SignPath => Path.Combine(DataDir, "state.json.sig");
    internal static string VaultPath => Path.Combine(DataDir, "vault.dat");
    internal static string ShellBackupPath => Path.Combine(DataDir, "shell.bak");
    internal static string HmacKeyPath => Path.Combine(DataDir, "hmac.key");
    internal static string BankDir => Path.Combine(DataDir, "bank");

    internal static void EnsureDataDir()
    {
        Directory.CreateDirectory(DataDir);
    }

    internal static byte[] NewSalt()
    {
        byte[] salt = new byte[SaltBytes];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    /// <summary>HMAC key 首次生成后固定；用于 state.json 的签名与篡改检测。</summary>
    internal static byte[] EnsureHmacKey()
    {
        try
        {
            if (File.Exists(HmacKeyPath))
            {
                byte[] existing = File.ReadAllBytes(HmacKeyPath);
                if (existing.Length == HmacKeyBytes)
                {
                    return existing;
                }
            }
        }
        catch (IOException)
        {
            // 读不到就当缺失，下面重写。key 损坏的后果是旧 state 被判篡改并回到 Locked，是安全侧。
        }

        EnsureDataDir();
        byte[] key = new byte[HmacKeyBytes];
        RandomNumberGenerator.Fill(key);
        File.WriteAllBytes(HmacKeyPath, key);
        return key;
    }
}

/// <summary>
/// WinExe 不分配控制台；管理命令靠父进程（powershell/脚本）stdout/stderr 转发，
/// 无参数向导则复用父控制台或自建一个。
/// 必须重绑标准流：.NET 的 Console 会缓存首次拿到的 std handle，AllocConsole 之后不刷新。
/// </summary>
internal static class ConsoleBridge
{
    private const int AttachParentProcess = -1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    /// <summary>挂到父进程控制台（脚本调用场景：stderr 直接进脚本的 stderr）。</summary>
    internal static void EnsureInherited()
    {
        if (AttachConsole(AttachParentProcess))
        {
            Rebind();
        }
    }

    /// <summary>向导场景：优先复用父控制台，否则自建。</summary>
    internal static void EnsureOwnConsole()
    {
        if (AttachConsole(AttachParentProcess))
        {
            Rebind();
            return;
        }

        if (AllocConsole())
        {
            Rebind();
        }
    }

    private static void Rebind()
    {
        try
        {
            // OpenStandardOutput/Error 每次都重新取 std handle，绕开 Console 的缓存。
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true });
        }
        catch (IOException)
        {
            // 句柄不可用：输出丢弃，功能不受影响。
        }
    }
}
