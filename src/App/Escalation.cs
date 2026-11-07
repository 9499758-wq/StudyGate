using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StudyGate.App;

internal enum EscalationOutcome
{
    /// <summary>已是管理员：可直接改 HKLM / 注册计划任务。</summary>
    Elevated = 0,

    /// <summary>UAC 提权成功（新进程已启动或本进程已提升）。</summary>
    Promoted = 1,

    /// <summary>无法提权：降级为"仅锁当前会话"，锁屏门本身照常工作。</summary>
    Degraded = 2,
}

internal sealed class EscalationResult
{
    internal EscalationOutcome Outcome { get; }
    internal string? Reason { get; }

    internal EscalationResult(EscalationOutcome outcome, string? reason = null)
    {
        Outcome = outcome;
        Reason = reason;
    }

    internal bool CanWriteMachineScope => Outcome is EscalationOutcome.Elevated or EscalationOutcome.Promoted;
}

/// <summary>
/// 提权统一入口。全工程只有这里判断"能不能改机器级设置"。
///
/// 设计原则（SPEC 第二节第 7 条）：提权失败**不阻断锁屏门**。
/// 机器级动作（改 Winlogon Shell、注册计划任务、写 IFEO）拿不到权限时，
/// 降级为"仅锁当前会话"：钩子、Job 对象、进程内守护都只在当前会话内生效，
/// 用户仍要走答题门才能看到桌面，只是无法跨重启持久化。
///
/// 不自建 runas 对话框：WinExe 无控制台，弹 UAC 需要 ShellExecute verb=runas，
/// 而本程序作为 shell 运行时没有可信桌面可依附。因此只用令牌检测 + 由脚本侧提权。
/// </summary>
internal static class Escalation
{
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        ref TokenElevation tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        internal int TokenIsElevated;
    }

    /// <summary>当前令牌是否已提升到管理员。</summary>
    internal static bool IsElevated()
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
            {
                return false;
            }

            TokenElevation info = default;
            int size = Marshal.SizeOf<TokenElevation>();
            if (!GetTokenInformation(token, TokenElevationClass, ref info, size, out _))
            {
                return false;
            }

            return info.TokenIsElevated != 0;
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }

    /// <summary>
    /// 请求机器级权限。已提升直接返回 Elevated；
    /// 未提升且 allowPrompt 为真时尝试 runas 重启自身（仅安装期可用）；
    /// 其余情况返回 Degraded，调用方据此把动作缩到当前会话。
    /// </summary>
    internal static EscalationResult Request(bool allowPrompt)
    {
        if (IsElevated())
        {
            return new EscalationResult(EscalationOutcome.Elevated);
        }

        if (!allowPrompt)
        {
            return new EscalationResult(EscalationOutcome.Degraded, "not elevated");
        }

        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return new EscalationResult(EscalationOutcome.Degraded, "process path unavailable");
            }

            ProcessStartInfo psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            psi.ArgumentList.Add("--install");

            Process.Start(psi);
            return new EscalationResult(EscalationOutcome.Promoted);
        }
        catch (Win32Exception)
        {
            // 用户在 UAC 点了"否"，或没有可用的提权通道。
            return new EscalationResult(EscalationOutcome.Degraded, "uac declined");
        }
    }
}
