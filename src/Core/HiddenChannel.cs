namespace StudyGate.Core;

/// <summary>
/// 隐蔽口令解析器。纯状态机，与 UI / 钩子无关，可单测。
///
/// 规则：
///   * '\u0001' → 进入捕获态并清空缓冲
///   * '\r'     → 结束捕获，捕获态且缓冲非空时返回候选口令；否则返回 null
///   * '\n' / '\u001b' → 复位
///   * 其他控制字符 → 忽略，不打断捕获
///   * 可打印字符 → 仅在捕获态进缓冲；非捕获态回显给不了任何东西（返回 null 即不误触）
///   * 缓冲超过上限 → 复位并丢弃
///
/// 锁定归属：候选提取在 Core，落盘在调用方（GateState.PasswordFailCount / PasswordLockedUntilUtc）。
/// ReportResult 只改内存状态；Restore 让调用方把落盘值灌回来。
/// </summary>
public sealed class HiddenChannel
{
    public const int MaxPasswordFails = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromHours(1);

    private const int MaxBuffer = 512;
    private const char ArmChar = '\u0001';
    private const char Terminator = '\r';

    private readonly Func<DateTimeOffset> _clock;

    private bool _armed;
    private string _buffer = string.Empty;

    public HiddenChannel()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    public HiddenChannel(Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public int FailCount { get; private set; }

    public DateTimeOffset? LockedUntilUtc { get; private set; }

    public bool IsArmed => _armed;

    /// <summary>命中返回候选口令；任何其他情况返回 null。锁定期直接丢弃输入，不累积缓冲。</summary>
    public string? Feed(char c)
    {
        if (IsLockedOut())
        {
            _armed = false;
            _buffer = string.Empty;
            return null;
        }

        if (c == ArmChar)
        {
            _armed = true;
            _buffer = string.Empty;
            return null;
        }

        if (c == Terminator)
        {
            var candidate = _armed && _buffer.Length > 0 ? _buffer : null;
            _armed = false;
            _buffer = string.Empty;
            return candidate;
        }

        if (c == '\n' || c == '\u001b')
        {
            _armed = false;
            _buffer = string.Empty;
            return null;
        }

        if (char.IsControl(c))
        {
            return null;
        }

        if (!_armed)
        {
            return null;
        }

        if (_buffer.Length >= MaxBuffer)
        {
            _armed = false;
            _buffer = string.Empty;
            return null;
        }

        _buffer += c;
        return null;
    }

    public void ReportResult(bool isCorrect)
    {
        ReportResult(isCorrect, _clock());
    }

    /// <summary>调用方校验后回写结果。第 5 次连续错误锁 1 小时。</summary>
    public void ReportResult(bool isCorrect, DateTimeOffset nowUtc)
    {
        if (isCorrect)
        {
            FailCount = 0;
            LockedUntilUtc = null;
            return;
        }

        FailCount++;
        if (FailCount >= MaxPasswordFails)
        {
            LockedUntilUtc = nowUtc + LockoutDuration;
        }
    }

    public bool IsLockedOut() => IsLockedOut(_clock());

    public bool IsLockedOut(DateTimeOffset nowUtc) => LockedUntilUtc is { } until && nowUtc < until;

    /// <summary>把落盘的 failCount / lockedUntil 灌回来（进程重启后调用）。</summary>
    public void Restore(int failCount, DateTimeOffset? lockedUntilUtc)
    {
        FailCount = failCount < 0 ? 0 : failCount;
        LockedUntilUtc = lockedUntilUtc;
    }

    public void Reset()
    {
        _armed = false;
        _buffer = string.Empty;
    }
}
