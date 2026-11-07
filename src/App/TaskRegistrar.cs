using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace StudyGate.App;

/// <summary>
/// 计划任务注册/删除。
///
/// **为什么选 `schtasks /Create /XML` 而不是 TaskScheduler COM：**
/// COM 路线要引 TaskScheduler 类型库，等价于引入 `Interop.TaskScheduler` 包 —— 本工程
/// 零第三方依赖是硬约束，手写 `ITaskService` 的 vtable 又要在 mac 上盲写 12 个接口，
/// 编译风险远高于收益。XML 路线只有一个外部依赖（系统自带 schtasks.exe），
/// 任务定义是纯文本、可版本化、可 `schtasks /Query /XML` 回读比对，
/// 且 `--uninstall` 侧同样只用一条命令，与 scripts/install.ps1 保持同构。
///
/// **为什么不用拼命令行（`schtasks /Create ... /TR "path"`）：**
/// `/TR` 的字符串里带空格、中文、`&` 时，cmd 的分词与引号规则会和 XML 属性混在一起，
/// "Program Files"、含中文的用户目录、带 `&` 的路径都会炸。改用 `ArgumentList`
/// （进程参数由 runtime 负责加引号）+ XML 里做实体转义，两层都避开分词问题。
///
/// 任务形态：登录时触发 + 每 30 秒重复 + IgnoreNew + 无执行时限 + Hidden +
/// HighestAvailable，动作是 `<exe> --watchdog`。守护进程由它保活，主进程再由
/// SelfDefense 互拉 —— 这是 SPEC 第二节第 4 条的"计划任务 30 秒兜底"。
/// </summary>
internal static class TaskRegistrar
{
    private const string TaskName = "StudyGateAutostart";
    private const string XmlFileName = "studygate-task.xml";
    private const int SchtasksTimeoutMs = 15000;

    /// <summary>
    /// 注册 / 覆盖计划任务。exePath 是 StudyGate.exe 全路径。
    /// 失败不抛异常：写一行 stderr 并返回 false，调用方（--install）据此报错退出。
    /// </summary>
    internal static bool Register(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            WriteError("task register failed: exe path is empty");
            return false;
        }

        string fullExe;
        try
        {
            fullExe = Path.GetFullPath(exePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            WriteError("task register failed: invalid exe path");
            return false;
        }

        if (!File.Exists(fullExe))
        {
            WriteError("task register failed: exe not found");
            return false;
        }

        string xmlPath = Path.Combine(Path.GetTempPath(), XmlFileName);

        try
        {
            // UTF-16 + BOM：schtasks /XML 对声明与实际编码的一致性很敏感，
            // 用 Encoding.Unicode 写并声明 encoding="UTF-16" 是 Task Scheduler 原生形态。
            File.WriteAllText(xmlPath, BuildXml(fullExe), Encoding.Unicode);

            int exit = RunSchtasks(
                new[]
                {
                    "/Create",
                    "/TN", TaskName,
                    "/XML", xmlPath,
                    "/F",
                },
                out string err);

            if (exit == 0)
            {
                return true;
            }

            WriteError("task register failed: schtasks exit " + exit.ToString(CultureInfo.InvariantCulture) + err);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteError("task register failed: cannot write task xml");
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            WriteError("task register failed: cannot start schtasks");
            return false;
        }
        finally
        {
            TryDelete(xmlPath);
        }
    }

    /// <summary>
    /// 删除计划任务。幂等：任务本来就不存在也返回 true（lead 明确要求）。
    /// 真正的权限问题会写一行 stderr 供 install.ps1 观察，但不影响返回值语义。
    /// </summary>
    internal static bool Delete()
    {
        try
        {
            int exit = RunSchtasks(
                new[]
                {
                    "/Delete",
                    "/TN", TaskName,
                    "/F",
                },
                out string err);

            if (exit != 0)
            {
                // 退出码非 0 多数是"任务不存在"；不区分权限与不存在，按幂等处理。
                WriteError("task delete returned " + exit.ToString(CultureInfo.InvariantCulture) + err);
            }

            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            WriteError("task delete failed: cannot start schtasks");
            return true;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // XML
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 元素顺序严格按 Task Scheduler schema（xs:sequence，乱序会导入失败）：
    ///   triggerBaseType 的 Repetition/Enabled 在前，logonTriggerType 追加的 Delay 在后；
    ///   settingsType 里 Hidden 位于 Enabled 之后、ExecutionTimeLimit 之前。
    /// </summary>
    private static string BuildXml(string exePath)
    {
        string command = EscapeXml(exePath);
        string workingDir = EscapeXml(Path.GetDirectoryName(exePath) ?? string.Empty);

        StringBuilder sb = new StringBuilder(2048);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n");
        sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n");

        sb.Append("  <RegistrationInfo>\n");
        sb.Append("    <Description>StudyGate 锁屏答题门保活</Description>\n");
        sb.Append("  </RegistrationInfo>\n");

        sb.Append("  <Triggers>\n");
        sb.Append("    <LogonTrigger>\n");
        sb.Append("      <Repetition>\n");
        sb.Append("        <Interval>PT30S</Interval>\n");
        sb.Append("        <StopAtDurationEnd>false</StopAtDurationEnd>\n");
        sb.Append("      </Repetition>\n");
        sb.Append("      <Enabled>true</Enabled>\n");
        sb.Append("      <Delay>PT0S</Delay>\n");
        sb.Append("    </LogonTrigger>\n");
        sb.Append("  </Triggers>\n");

        sb.Append("  <Principals>\n");
        sb.Append("    <Principal id=\"Author\">\n");
        sb.Append("      <RunLevel>HighestAvailable</RunLevel>\n");
        sb.Append("    </Principal>\n");
        sb.Append("  </Principals>\n");

        sb.Append("  <Settings>\n");
        sb.Append("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n");
        sb.Append("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n");
        sb.Append("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n");
        sb.Append("    <AllowHardTerminate>false</AllowHardTerminate>\n");
        sb.Append("    <StartWhenAvailable>true</StartWhenAvailable>\n");
        sb.Append("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\n");
        sb.Append("    <AllowStartOnDemand>true</AllowStartOnDemand>\n");
        sb.Append("    <Enabled>true</Enabled>\n");
        sb.Append("    <Hidden>true</Hidden>\n");
        sb.Append("    <RunOnlyIfIdle>false</RunOnlyIfIdle>\n");
        sb.Append("    <WakeToRun>false</WakeToRun>\n");
        sb.Append("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n");
        sb.Append("    <Priority>4</Priority>\n");
        sb.Append("  </Settings>\n");

        sb.Append("  <Actions Context=\"Author\">\n");
        sb.Append("    <Exec>\n");
        sb.Append("      <Command>").Append(command).Append("</Command>\n");
        sb.Append("      <Arguments>--watchdog</Arguments>\n");
        if (workingDir.Length > 0)
        {
            sb.Append("      <WorkingDirectory>").Append(workingDir).Append("</WorkingDirectory>\n");
        }

        sb.Append("    </Exec>\n");
        sb.Append("  </Actions>\n");

        sb.Append("</Task>\n");
        return sb.ToString();
    }

    /// <summary>
    /// XML 实体转义。必须**先**替换 &，否则后续插入的 &amp; 会把已转义的 &lt; 变成 &amp;lt;。
    /// </summary>
    private static string EscapeXml(string raw)
    {
        StringBuilder sb = new StringBuilder(raw.Length + 16);

        foreach (char c in raw)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                case '\'':
                    sb.Append("&apos;");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // schtasks
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 跑一次 schtasks。参数走 ArgumentList（runtime 负责加引号），
    /// stdout/stderr 全部重定向吃掉 —— 本程序不写系统日志，也不刷控制台。
    /// </summary>
    private static int RunSchtasks(string[] args, out string errLine)
    {
        errLine = string.Empty;

        ProcessStartInfo psi = new ProcessStartInfo("schtasks")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using Process proc = Process.Start(psi)
            ?? throw new InvalidOperationException("schtasks did not start");

        string stderr = proc.StandardError.ReadToEnd();
        string stdout = proc.StandardOutput.ReadToEnd();

        if (!proc.WaitForExit(SchtasksTimeoutMs))
        {
            try
            {
                proc.Kill();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
            }

            errLine = ": timeout";
            return -1;
        }

        if (stderr.Length > 0)
        {
            errLine = ": " + FirstLine(stderr);
        }
        else if (proc.ExitCode != 0 && stdout.Length > 0)
        {
            errLine = ": " + FirstLine(stdout);
        }

        return proc.ExitCode;
    }

    private static string FirstLine(string text)
    {
        int cut = text.IndexOfAny(new[] { '\r', '\n' });
        string line = cut >= 0 ? text.Substring(0, cut) : text;
        return line.Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时文件删不掉不算失败：下次覆盖写即可。
        }
    }

    private static void WriteError(string message)
    {
        try
        {
            Console.Error.WriteLine(message);
        }
        catch (IOException)
        {
            // 无 stderr 可用（无控制台且父进程未重定向）：返回值仍有效。
        }
    }
}
