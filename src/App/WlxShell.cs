using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StudyGate.App;

/// <summary>Shell 接管失败。Reason 是一行可直接写进 stderr 的原因，不含堆栈。</summary>
internal sealed class WlxShellException : Exception
{
    internal string Reason { get; }

    internal WlxShellException(string reason)
        : base(reason)
    {
        Reason = reason;
    }
}

/// <summary>
/// Winlogon 的 Shell 值接管。
///
/// 只碰 HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell 这一个值。
/// 不动 Userinit（动了会让登录链断在别处）、不动 LogonUI、不动 Screensaver。
///
/// 为什么手写 advapi32 而不是 Microsoft.Win32.Registry：
/// 该类型在部分 net8.0-windows 组合下需要额外 PackageReference，本工程零第三方包、且
/// TreatWarningsAsErrors=true，任何"可能缺包"的写法都会变成编译风险。P/Invoke 是确定集。
///
/// 权限现实（必须在 Windows 上验）：Winlogon 键的 ACL 默认只授予 SYSTEM 与
/// TrustedInstaller 写权限，Administrators 组通常**也写不进**。因此 Install 直接写
/// 会得到 ERROR_ACCESS_DENIED（5），本类会把它翻译成明确原因并提示改用 SYSTEM 侧执行
/// （TaskRegistrar 注册的计划任务正是为此准备的 SYSTEM 通道）。这不是提权失败静默。
/// </summary>
internal static class WlxShell
{
    private const string WinlogonSubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string ShellValueName = "Shell";
    private const string FallbackShell = "explorer.exe";

    private const int ErrorSuccess = 0;
    private const int ErrorFileNotFound = 2;
    private const int ErrorMoreData = 234;
    private const int ErrorAccessDenied = 5;

    private const uint RegSzType = 1;
    private const int KeyAllAccess = 0x000F003F;

    private static readonly IntPtr HkeyLocalMachine = unchecked((IntPtr)0x80000002);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegOpenKeyExW")]
    private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "RegCloseKey")]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegQueryValueExW")]
    private static extern int RegQueryValueEx(
        IntPtr hKey,
        string lpValueName,
        IntPtr lpReserved,
        out uint lpType,
        byte[]? lpData,
        ref int lpcbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegSetValueExW")]
    private static extern int RegSetValueEx(
        IntPtr hKey,
        string lpValueName,
        int reserved,
        uint dwType,
        byte[] lpData,
        int cbData);

    // ─────────────────────────────────────────────────────────────
    // 读
    // ─────────────────────────────────────────────────────────────

    /// <summary>读当前 Shell 值；值不存在返回 null（代表系统默认 explorer.exe）。</summary>
    internal static string? Read()
    {
        int rc = RegOpenKeyEx(HkeyLocalMachine, WinlogonSubKey, 0, KeyAllAccess, out IntPtr hKey);
        if (rc != ErrorSuccess)
        {
            throw new WlxShellException("open Winlogon key failed: " + Describe(rc));
        }

        try
        {
            int cb = 0;
            rc = RegQueryValueEx(hKey, ShellValueName, IntPtr.Zero, out uint type, null, ref cb);
            if (rc == ErrorFileNotFound)
            {
                return null;
            }

            if (rc != ErrorSuccess && rc != ErrorMoreData)
            {
                throw new WlxShellException("read Shell failed: " + Describe(rc));
            }

            if (cb <= 0)
            {
                return null;
            }

            byte[] buffer = new byte[cb];
            int cbActual = cb;
            rc = RegQueryValueEx(hKey, ShellValueName, IntPtr.Zero, out type, buffer, ref cbActual);
            if (rc != ErrorSuccess)
            {
                throw new WlxShellException("read Shell failed: " + Describe(rc));
            }

            if (type != RegSzType)
            {
                throw new WlxShellException("Shell value is not REG_SZ");
            }

            string text = Encoding.Unicode.GetString(buffer, 0, Math.Min(cbActual, buffer.Length));
            int nul = text.IndexOf('\0');
            if (nul >= 0)
            {
                text = text.Substring(0, nul);
            }

            return text;
        }
        finally
        {
            RegCloseKey(hKey);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 安装 / 还原
    // ─────────────────────────────────────────────────────────────

    /// <summary>备份原 Shell 值，然后把 Shell 指到本 exe 的 --shell 入口。</summary>
    internal static void Install()
    {
        RequireElevation();

        string? current = Read();

        // 幂等：重复安装不覆盖备份，避免把 "StudyGate.exe --shell" 备份成"原值"。
        if (!IsBackupSafe(current))
        {
            GatePaths.EnsureDataDir();
            if (!File.Exists(GatePaths.ShellBackupPath))
            {
                // current 为 null 时写空文件：Restore 侧会把它当成"无可用备份"而回落 explorer。
                File.WriteAllText(GatePaths.ShellBackupPath, current ?? string.Empty, Encoding.UTF8);
            }
        }

        WriteShell(BuildShellCommand());
    }

    /// <summary>
    /// 还原 Shell 并删除备份。
    /// 安全阀（lead 明确要求）：备份为空、不存在、或本身指向 StudyGate 时一律回落
    /// explorer.exe —— 卸载绝不能把用户锁回答题门。
    /// </summary>
    internal static void Restore()
    {
        RequireElevation();

        string target = FallbackShell;

        try
        {
            if (File.Exists(GatePaths.ShellBackupPath))
            {
                string backup = File.ReadAllText(GatePaths.ShellBackupPath, Encoding.UTF8).Trim();
                if (IsBackupSafe(backup))
                {
                    target = backup;
                }
            }
        }
        catch (IOException)
        {
            // 备份读不出来（损坏/被锁）：直接回落默认，不阻断卸载。
            target = FallbackShell;
        }

        WriteShell(target);

        try
        {
            if (File.Exists(GatePaths.ShellBackupPath))
            {
                File.Delete(GatePaths.ShellBackupPath);
            }
        }
        catch (IOException)
        {
            // 删不掉只留下一个无害文件，卸载本身已完成。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>备份值是否可以安全还原：非空、且不指向本程序。</summary>
    private static bool IsBackupSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.IndexOf("StudyGate", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static string BuildShellCommand()
    {
        string exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "StudyGate.exe");

        // SPEC 第二节要求写成 "<exe路径>" --shell；路径必须带引号，否则含空格的目录会被截断。
        return "\"" + exePath + "\" --shell";
    }

    private static void WriteShell(string value)
    {
        int rc = RegOpenKeyEx(HkeyLocalMachine, WinlogonSubKey, 0, KeyAllAccess, out IntPtr hKey);
        if (rc != ErrorSuccess)
        {
            throw new WlxShellException("open Winlogon key failed: " + Describe(rc));
        }

        try
        {
            byte[] data = Encoding.Unicode.GetBytes(value + "\0");
            rc = RegSetValueEx(hKey, ShellValueName, 0, RegSzType, data, data.Length);
            if (rc != ErrorSuccess)
            {
                string reason = rc == ErrorAccessDenied
                    ? "Winlogon key is writable only by SYSTEM/TrustedInstaller; run this step as SYSTEM (see TaskRegistrar) or grant the key ACL"
                    : "write Shell failed: " + Describe(rc);
                throw new WlxShellException(reason);
            }
        }
        finally
        {
            RegCloseKey(hKey);
        }
    }

    private static void RequireElevation()
    {
        if (!Escalation.IsElevated())
        {
            throw new WlxShellException("administrator required to modify Winlogon Shell");
        }
    }

    private static string Describe(int rc)
    {
        return "win32=" + rc.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
