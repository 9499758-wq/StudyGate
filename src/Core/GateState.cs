namespace StudyGate.Core;

public enum GatePhase
{
    Locked = 0,
    QuizReady = 1,
    QuizInProgress = 2,
    Unlocked = 3,
    PanicBypassed = 4,
}

// 字段与默认值逐字对齐 docs/CONTRACTS.md A 节，不得改名或重排序列化顺序。
public sealed class GateState
{
    public GatePhase Phase { get; set; } = GatePhase.Locked;

    /// <summary>解锁截止时间（UTC）。null = 未解锁。</summary>
    public DateTimeOffset? UnlockUntilUtc { get; set; }

    /// <summary>
    /// 放行那一刻的系统启动时刻（UTC）。语义：只有"同一次开机"内才认这份放行。
    ///
    /// 计算式：DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(GetTickCount64())。
    /// GetTickCount64 是自开机以来的毫秒数，重启归零，所以重启后算出的 bootUtc
    /// 与上次记录的必然相差数小时 —— 判定侧用 ±2 分钟容忍 tick 换算抖动。
    ///
    /// null = 从来没成功放行过（或老存档）。判定侧一律视为"不在窗口内"。
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
    ///
    /// 这是第 13 个字段，序列化位置固定排在 canonical 末尾 ——
    /// 老 state.json 没有这一段时，Deserialize 按"缺失即空表"宽容处理，
    /// 且 HMAC 验的是磁盘上的原始字节，所以加字段不会让历史存档失效。
    /// 上限由调用方维护（建议 30 条）。
    /// </summary>
    public List<AttemptRecord> History { get; set; } = new();

    /// <summary>默认锁状态。与 new GateState() 完全等价，仅作语义入口。</summary>
    public static GateState Locked() => new();
}

/// <summary>
/// 一次完整作答的留痕。字段刻意做成扁平标量，便于直接落盘做趋势统计，
/// 也避免嵌套对象在 canonical JSON 里再开一层。
/// </summary>
public sealed record AttemptRecord(
    DateTimeOffset AtUtc,
    int Grade,
    string Term,
    int Score,
    int PassScore,
    bool Passed,
    int CorrectCount,
    int TotalCount,
    long ElapsedMs,
    IReadOnlyList<int> SubjectCorrect)
{
    /// <summary>SubjectCorrect 的长度恒为 3，下标即 Subject。</summary>
    public const int SubjectCount = 3;
}
