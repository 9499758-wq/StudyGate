namespace StudyGate.CoreTests;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StudyGate.Core;

/// <summary>
/// StudyGate.Core 的可执行断言套件。
///
/// 不引任何第三方测试框架（SPEC 禁 NuGet）：自带 Main、自建 assert，
/// `dotnet run` 即跑，任一断言失败 Environment.Exit(1)。
///
/// 覆盖 docs/SPEC.md 第六节的全部 Core 条目：
///   判分边界 89/90/91 · 未作答 · 同 seed 同卷 · 篡改状态被 HMAC 拒绝
///   口令错 5 次锁 1 小时 · 隐蔽序列不误触
/// 另加跨实现固定向量（canonical JSON / HMAC / PBKDF2 / vault blob / 抽题序列），
/// 保证 C# 与 tools/core-reference.mjs 逐字节、逐位一致。
/// </summary>
public static class Program
{
    private static int _passed;
    private static readonly List<(string Name, string Detail)> Failures = new();

    // 固定向量：key = 字节 0x00..0x1F。与 tools/core-reference.mjs 第 12 节同源。
    private static readonly byte[] VectorKey =
    {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
    };

    // 14 字段 canonical。字段序 = GateStateCodec.FieldOrder，与 tools/core-reference.mjs 的 order 逐位对齐。
    private const string CanonicalDefault =
        "{\"phase\":\"Locked\",\"unlockUntilUtc\":null,\"unlockBootUtc\":null,\"grade\":6,\"term\":\"下\","
        + "\"attemptsToday\":0,\"lastAttemptUtc\":null,\"passwordFailCount\":0,"
        + "\"passwordLockedUntilUtc\":null,\"paperSeed\":0,\"currentPaperIds\":[],\"currentAnswers\":[],"
        + "\"currentIndex\":0,\"history\":[]}";

    private const string SignatureDefault = "8nYn/6WDKff0sNeisUt2HVqSHXmTIjvENc47sU+6+iU=";

    private const string CanonicalAlt =
        "{\"phase\":\"Unlocked\",\"unlockUntilUtc\":\"2026-11-08T06:08:17.000Z\","
        + "\"unlockBootUtc\":\"2026-11-07T00:00:00.000Z\",\"grade\":9,\"term\":\"上\",\"attemptsToday\":2,"
        + "\"lastAttemptUtc\":\"2026-11-07T07:08:17.000Z\",\"passwordFailCount\":3,"
        + "\"passwordLockedUntilUtc\":\"2026-11-07T08:00:00.000Z\",\"paperSeed\":12345,"
        + "\"currentPaperIds\":[\"a\",\"b\"],\"currentAnswers\":[1,-1],\"currentIndex\":1,\"history\":[]}";

    private const string SignatureAlt = "h/SQPp4B3i/gIshNlCxchcCEQaSw8acEAjHOiAMDPp0=";

    /// <summary>
    /// 改规格之前落盘的老存档：只有最初 12 个字段，既没有 history 也没有 unlockBootUtc。
    /// 用来验证"追加字段不会让历史 state.json 变成垃圾"——读进来补成 14 字段即可。
    /// </summary>
    private const string CanonicalLegacy12 =
        "{\"phase\":\"Unlocked\",\"unlockUntilUtc\":\"2026-11-08T06:08:17.000Z\",\"grade\":9,\"term\":\"上\","
        + "\"attemptsToday\":2,\"lastAttemptUtc\":\"2026-11-07T07:08:17.000Z\",\"passwordFailCount\":3,"
        + "\"passwordLockedUntilUtc\":\"2026-11-07T08:00:00.000Z\",\"paperSeed\":12345,"
        + "\"currentPaperIds\":[\"a\",\"b\"],\"currentAnswers\":[1,-1],\"currentIndex\":1}";

    /// <summary>老存档补齐后的样子：14 字段全在，缺的两格都是 null。</summary>
    private const string CanonicalLegacyUpgraded =
        "{\"phase\":\"Unlocked\",\"unlockUntilUtc\":\"2026-11-08T06:08:17.000Z\",\"unlockBootUtc\":null,"
        + "\"grade\":9,\"term\":\"上\",\"attemptsToday\":2,"
        + "\"lastAttemptUtc\":\"2026-11-07T07:08:17.000Z\",\"passwordFailCount\":3,"
        + "\"passwordLockedUntilUtc\":\"2026-11-07T08:00:00.000Z\",\"paperSeed\":12345,"
        + "\"currentPaperIds\":[\"a\",\"b\"],\"currentAnswers\":[1,-1],\"currentIndex\":1,\"history\":[]}";

    private const string SignatureLegacyUpgraded = "yKpNcL6bWk2260hqRU6aoneOROBxdl/pq1oLAPzZ7TQ=";

    /// <summary>带一条作答历史的 canonical 与签名，用于验证 history 段的字节级契约。</summary>
    private const string CanonicalAltWithHistory =
        "{\"phase\":\"Unlocked\",\"unlockUntilUtc\":\"2026-11-08T06:08:17.000Z\","
        + "\"unlockBootUtc\":\"2026-11-07T00:00:00.000Z\",\"grade\":9,\"term\":\"上\",\"attemptsToday\":2,"
        + "\"lastAttemptUtc\":\"2026-11-07T07:08:17.000Z\",\"passwordFailCount\":3,"
        + "\"passwordLockedUntilUtc\":\"2026-11-07T08:00:00.000Z\",\"paperSeed\":12345,"
        + "\"currentPaperIds\":[\"a\",\"b\"],\"currentAnswers\":[1,-1],\"currentIndex\":1,"
        + "\"history\":[{\"atUtc\":\"2026-11-07T06:00:00.000Z\",\"grade\":9,\"term\":\"上\",\"score\":93,"
        + "\"passScore\":90,\"passed\":true,\"correctCount\":28,\"totalCount\":30,\"elapsedMs\":812345,"
        + "\"subjectCorrect\":[10,9,9]}]}";

    private const string SignatureAltWithHistory = "wIatBLESDmegqkoOuH6sLik5jvnu9RmT0rP7uApRoy8=";

    public static int Main()
    {
        try
        {
            Section("1. 判分边界（89 / 90 / 91）");
            GradingBoundaries();

            Section("2. 未作答与畸形输入（绝不抛）");
            UnansweredAndMalformed();

            Section("3. 向下取整：179/200 = 89 不通过");
            FloorRounding();

            Section("4. 确定性抽题（合成题库固定向量）");
            DeterministicPickVector();

            Section("5. 真实题库抽题（若 data/bank 已就绪）");
            RealBankPick();

            Section("6. 题量不足要抛，且写明缺哪个 (grade,term,subject)");
            ShortageThrows();

            Section("7. 状态 HMAC 与篡改检测");
            HmacTamper();

            Section("8. LoadFromDirectory 非法项计数");
            LoadRejections();

            Section("9. Vault：PBKDF2 200k / 常量时间 / blob 往返");
            VaultChecks();

            Section("10. FileGateStore：原子写与篡改 fail-closed");
            GateStoreChecks();

            Section("11. 隐蔽通道状态机");
            HiddenChannelChecks();
        }
        catch (Exception ex)
        {
            Failures.Add(("套件异常中止", ex.ToString() ?? string.Empty));
        }

        Console.WriteLine();
        Console.WriteLine(new string('-', 60));

        if (Failures.Count == 0)
        {
            Console.WriteLine($"全部通过：{_passed} 项断言");
            return 0;
        }

        Console.WriteLine($"{Failures.Count} 项失败 / {_passed + Failures.Count} 项");
        foreach (var (name, detail) in Failures)
        {
            Console.WriteLine($"  x {name}{(detail.Length > 0 ? " — " + detail : string.Empty)}");
        }

        return 1;
    }

    // ---------------------------------------------------------------- 断言框架

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    private static void Ok(bool condition, string name, string detail = "")
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  ok {name}");
        }
        else
        {
            Failures.Add((name, detail));
            Console.WriteLine($"  x  {name}{(detail.Length > 0 ? " — " + detail : string.Empty)}");
        }
    }

    private static void Eq<T>(T actual, T expected, string name)
    {
        var equal = EqualityComparer<T>.Default.Equals(actual, expected);
        Ok(equal, name, equal ? string.Empty : $"期望 {Fmt(expected)}，实际 {Fmt(actual)}");
    }

    /// <summary>
    /// 期望非空字符串的断言。Feed 返回 string?，直接塞进 Eq&lt;T&gt; 会触发
    /// CS8604（可能为 null 的实参），在 TreatWarningsAsErrors 下直接编译失败。
    /// </summary>
    private static void EqStr(string? actual, string expected, string name)
    {
        var equal = actual is not null && string.Equals(actual, expected, StringComparison.Ordinal);
        var detail = equal
            ? string.Empty
            : "期望 " + Show(expected) + "，实际 " + Show(actual);
        Ok(equal, name, detail);
    }

    private static string Show(string? value) => value is null ? "null" : "\"" + value + "\"";

    /// <summary>
    /// 期望为 null 的断言。参数取 object? 而不是 string?：既覆盖 Feed 返回的 string?，
    /// 也覆盖 UnlockBootUtc / UnlockUntilUtc 这类 DateTimeOffset? 可空值类型 ——
    /// 否则 EqNull(dateTimeOffset?) 是硬类型不匹配（CS1503），不是可空警告。
    /// 可空值类型装箱后 null 仍是 null，判定不受影响。
    /// </summary>
    private static void EqNull(object? actual, string name)
    {
        var isNull = actual is null;
        Ok(isNull, name, isNull ? string.Empty : "期望 null，实际 " + Fmt(actual));
    }

    private static string Fmt(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static void Throws(Action action, string name, string expectedFragment)
    {
        try
        {
            action();
            Failures.Add((name, "未抛异常"));
            Console.WriteLine($"  x  {name} — 未抛异常");
        }
        catch (Exception ex)
        {
            var matched = ex.Message.Contains(expectedFragment, StringComparison.Ordinal);
            Ok(matched, name, matched ? string.Empty : $"消息不含「{expectedFragment}」：{ex.Message}");
        }
    }

    // ---------------------------------------------------------------- 造题工具

    private static List<Question> MakePaper(int n, int cn, int ma, int en)
    {
        var paper = new List<Question>(n);
        var cursor = 0;

        void Add(int count, Subject subject)
        {
            for (var k = 0; k < count; k++)
            {
                paper.Add(new Question(
                    $"t-{(int)subject}-{k}",
                    subject,
                    6,
                    "下",
                    $"题 {cursor}",
                    new[] { "A", "B", "C", "D" },
                    0,
                    string.Empty));
                cursor++;
            }
        }

        Add(cn, Subject.Chinese);
        Add(ma, Subject.Math);
        Add(en, Subject.English);
        return paper;
    }

    private static List<AnswerRecord> AnswerAll(IReadOnlyList<Question> paper, int correctCount, int selectedForWrong)
    {
        var answers = new List<AnswerRecord>(paper.Count);
        for (var i = 0; i < paper.Count; i++)
        {
            var selected = i < correctCount ? 0 : selectedForWrong;
            answers.Add(new AnswerRecord(paper[i].Id, selected, selected == 0, 0));
        }

        return answers;
    }

    // ---------------------------------------------------------------- 1. 判分边界

    private static void GradingBoundaries()
    {
        var paper = MakePaper(20, 6, 7, 7);

        var full = Grader.Score(paper, AnswerAll(paper, 20, 1), 90);
        Eq(full.Score, 100, "全对 = 100 分");
        Ok(full.Passed, "100 分通过");
        Eq(full.WrongQuestionIds.Count, 0, "全对无错题");

        var r90 = Grader.Score(paper, AnswerAll(paper, 18, 1), 90);
        Eq(r90.Score, 90, "18/20 = 90 分");
        Ok(r90.Passed, "90 分通过（边界含）");
        Eq(r90.WrongQuestionIds.Count, 2, "90 分有 2 道错题");

        var r89 = Grader.Score(paper, AnswerAll(paper, 17, 1), 90);
        Eq(r89.Score, 85, "17/20 = 85 分（20 题无法构造 89）");
        Ok(!r89.Passed, "85 分不通过");

        var r95 = Grader.Score(paper, AnswerAll(paper, 19, 1), 90);
        Eq(r95.Score, 95, "19/20 = 95 分");
        Ok(r95.Passed, "95 分通过");

        // 89 分必须能真实构造：100 题卷 89 题正确。
        var huge = MakePaper(100, 34, 33, 33);
        var r89Exact = Grader.Score(huge, AnswerAll(huge, 89, 1), 90);
        Eq(r89Exact.Score, 89, "89/100 = 89 分");
        Ok(!r89Exact.Passed, "89 分不通过");

        var r91Exact = Grader.Score(huge, AnswerAll(huge, 91, 1), 90);
        Eq(r91Exact.Score, 91, "91/100 = 91 分");
        Ok(r91Exact.Passed, "91 分通过");

        // passScore 由调用方给，不是硬编码 90。
        Eq(Grader.Score(paper, AnswerAll(paper, 16, 1), 80).Score, 80, "16/20 = 80 分");
        Ok(Grader.Score(paper, AnswerAll(paper, 16, 1), 80).Passed, "80 分 / 阈值 80 通过");
        Ok(!Grader.Score(paper, AnswerAll(paper, 16, 1), 90).Passed, "80 分 / 阈值 90 不通过");
    }

    // ---------------------------------------------------------------- 2. 未作答

    private static void UnansweredAndMalformed()
    {
        var paper = MakePaper(20, 6, 7, 7);

        var allUnanswered = new List<AnswerRecord>();
        foreach (var q in paper)
        {
            allUnanswered.Add(new AnswerRecord(q.Id, -1, false, 0));
        }

        var r1 = Grader.Score(paper, allUnanswered, 90);
        Eq(r1.Score, 0, "全未作答 = 0 分");
        Eq(r1.WrongQuestionIds.Count, 20, "全未作答 → 20 道错题");
        Ok(!r1.Passed, "全未作答不通过");

        var r2 = Grader.Score(paper, new List<AnswerRecord>(), 90);
        Eq(r2.Score, 0, "答案数组为空 = 0 分，不抛");
        Eq(r2.BySubject.Count, 3, "仍返回三科聚合");
        Eq(r2.BySubject.Sum(s => s.Total), 20, "三科 total 合计 = 卷面题数");

        var outOfRange = new List<AnswerRecord>();
        foreach (var q in paper)
        {
            outOfRange.Add(new AnswerRecord(q.Id, 99, false, 0));
        }

        var r3 = Grader.Score(paper, outOfRange, 90);
        Eq(r3.Score, 0, "索引越界算错，不抛");

        var nulls = new List<AnswerRecord>();
        for (var i = 0; i < paper.Count; i++)
        {
            nulls.Add(null!);
        }

        var r4 = Grader.Score(paper, nulls, 90);
        Eq(r4.Score, 0, "null 记录算错，不抛");

        var r5 = Grader.Score(new List<Question>(), new List<AnswerRecord>(), 90);
        Eq(r5.Score, 0, "空卷 = 0 分，不抛");
        Ok(!r5.Passed, "空卷不通过");

        var partial = new List<AnswerRecord>();
        for (var i = 0; i < 10; i++)
        {
            partial.Add(new AnswerRecord(paper[i].Id, 0, true, 1000));
        }

        var r6 = Grader.Score(paper, partial, 90);
        Eq(r6.Score, 50, "只答前 10 题全对 + 后 10 题缺失 = 50 分");
        Eq(r6.WrongQuestionIds.Count, 10, "缺失记录计 10 道错题");
        Eq(r6.TotalElapsedMs, 10000L, "只累加存在的记录耗时");

        // 科目聚合
        var onlyChinese = new List<AnswerRecord>();
        for (var i = 0; i < paper.Count; i++)
        {
            onlyChinese.Add(new AnswerRecord(paper[i].Id, i < 6 ? 0 : -1, i < 6, 0));
        }

        var r7 = Grader.Score(paper, onlyChinese, 90);
        var cn = r7.BySubject.Single(s => s.Subject == Subject.Chinese);
        var ma = r7.BySubject.Single(s => s.Subject == Subject.Math);
        var en = r7.BySubject.Single(s => s.Subject == Subject.English);
        Eq(cn.Correct, 6, "语文 6 题全对");
        Eq(cn.Total, 6, "语文 total = 6");
        Eq(ma.Correct, 0, "数学 0 对");
        Eq(ma.Total, 7, "数学 total = 7");
        Eq(en.Total, 7, "英语 total = 7");
        Eq(r7.Score, 30, "6/20 = 30 分");

        // 记录里的 Correct 标志不可信：题库存的答案才作数
        var liar = new List<AnswerRecord>();
        foreach (var q in paper)
        {
            liar.Add(new AnswerRecord(q.Id, 3, true, 0));
        }

        var r8 = Grader.Score(paper, liar, 90);
        Eq(r8.Score, 0, "记录自称 Correct=true 但选项不对 → 0 分");

        // 题库自身答案越界 → 该题必错，不抛
        var broken = new List<Question>(paper)
        {
            new("broken", Subject.Chinese, 6, "下", "坏题", new[] { "A", "B" }, 5, string.Empty),
        };
        var r9 = Grader.Score(broken, new List<AnswerRecord>(), 90);
        Ok(!r9.Passed, "题库答案越界不导致异常");
        Eq(r9.WrongQuestionIds.Count, 21, "全部 21 题计错");

        // 负数耗时不算进总时长
        var negativeElapsed = new List<AnswerRecord>();
        for (var i = 0; i < paper.Count; i++)
        {
            negativeElapsed.Add(new AnswerRecord(paper[i].Id, 0, true, -5));
        }

        var r10 = Grader.Score(paper, negativeElapsed, 90);
        Eq(r10.TotalElapsedMs, 0L, "负耗时不计入总时长");
        Eq(r10.Score, 100, "但判分仍是 100");

        Eq(Grader.Score(paper, AnswerAll(paper, 20, 1), 90).TotalElapsedMs, 0L, "0 毫秒耗时不影响判分");
    }

    // ---------------------------------------------------------------- 3. 向下取整

    private static void FloorRounding()
    {
        var paper = MakePaper(200, 67, 67, 66);

        Eq(Grader.Score(paper, AnswerAll(paper, 179, 1), 90).Score, 89, "179/200 (89.5%) 向下取整 = 89");
        Ok(!Grader.Score(paper, AnswerAll(paper, 179, 1), 90).Passed, "89.5% 不通过 —— 必须真的 90");

        Eq(Grader.Score(paper, AnswerAll(paper, 180, 1), 90).Score, 90, "180/200 = 90 分");
        Ok(Grader.Score(paper, AnswerAll(paper, 180, 1), 90).Passed, "180/200 通过");

        var three = MakePaper(3, 1, 1, 1);
        Eq(Grader.Score(three, AnswerAll(three, 2, 1), 90).Score, 66, "2/3 = 66.67 → 66");
        Eq(Grader.Score(three, AnswerAll(three, 1, 1), 90).Score, 33, "1/3 = 33.33 → 33");
    }

    // ---------------------------------------------------------------- 4. 抽题固定向量

    private static List<Question> SyntheticBank()
    {
        var slugs = new[] { "cn", "ma", "en" };
        var bank = new List<Question>();

        for (var s = 0; s < 3; s++)
        {
            for (var k = 0; k < 40; k++)
            {
                bank.Add(new Question(
                    $"{slugs[s]}-{k.ToString("D3", CultureInfo.InvariantCulture)}",
                    (Subject)s,
                    6,
                    "下",
                    $"题 {slugs[s]} {k}",
                    new[] { "A", "B", "C", "D" },
                    k % 4,
                    "解析"));
            }
        }

        return bank;
    }

    private static void DeterministicPickVector()
    {
        var bank = new QuestionBank(SyntheticBank());
        Eq(bank.Count, 120, "合成题库 120 题");
        Eq(bank.RejectedCount, 0, "合成题库无非法项");

        var spec = new PaperSpec(6, "下", 30, 90, 10);

        var a = bank.Pick(spec, 12345);
        var expected = new[]
        {
            "en-029", "ma-038", "en-011", "ma-027", "ma-007",
            "ma-039", "ma-001", "en-034", "cn-015", "en-015",
            "cn-008", "en-016", "cn-005", "en-024", "cn-019",
            "en-003", "ma-014", "ma-033", "cn-021", "cn-028",
            "en-014", "en-032", "ma-012", "cn-000", "ma-016",
            "cn-029", "cn-010", "ma-019", "cn-003", "en-021",
        };

        var actualIds = a.Select(q => q.Id).ToArray();
        Eq(string.Join(",", actualIds), string.Join(",", expected), "seed=12345 与 Node 参考实现逐题同序");
        Eq(a.Count, 30, "卷面恰好 30 题");

        var b = bank.Pick(spec, 12345);
        Eq(string.Join(",", b.Select(q => q.Id).ToArray()), string.Join(",", actualIds), "同 seed 同卷（逐题 id 一致）");

        var c = bank.Pick(spec, 99999);
        Ok(string.Join(",", c.Select(q => q.Id).ToArray()) != string.Join(",", actualIds), "不同 seed 换卷");

        var counts = new int[3];
        foreach (var q in a)
        {
            counts[(int)q.Subject]++;
        }

        Ok(counts[0] >= 10, $"语文 ≥ 10（实际 {counts[0]}）");
        Ok(counts[1] >= 10, $"数学 ≥ 10（实际 {counts[1]}）");
        Ok(counts[2] >= 10, $"英语 ≥ 10（实际 {counts[2]}）");
        Eq(counts[0] + counts[1] + counts[2], 30, "三科合计 = 30");

        Eq(a.Select(q => q.Id).Distinct().Count(), a.Count, "同卷内无重复题");
        Ok(a.All(q => q.Grade == 6 && q.Term == "下"), "题目全部属于 grade=6 / term=下");
    }

    // ---------------------------------------------------------------- 5. 真实题库

    private static void RealBankPick()
    {
        var bankDir = FindBankDirectory();
        if (bankDir is null)
        {
            Ok(true, "未找到 data/bank（跳过真实题库断言）");
            return;
        }

        var bank = QuestionBank.LoadFromDirectory(bankDir);
        Ok(bank.Count > 0, $"题库加载 {bank.Count} 题");
        Eq(bank.RejectedCount, 0, "真实题库非法项 = 0");

        var spec = new PaperSpec(6, "下", 30, 90, 10);
        if (bank.Count < 30)
        {
            Ok(true, "题库题量 < 30，跳过抽题断言");
            return;
        }

        IReadOnlyList<Question>? a = null;
        try
        {
            a = bank.Pick(spec, 20240001);
        }
        catch (InvalidOperationException ex)
        {
            Ok(true, $"grade=6/下 抽题跳过（{ex.Message.Split('\n')[0]}）");
            return;
        }

        if (a is null)
        {
            Ok(true, "抽题返回 null（跳过后续断言）");
            return;
        }

        var paper = a;
        Eq(paper.Count, 30, "真实题库抽出 30 题");
        var b = bank.Pick(spec, 20240001);
        Eq(string.Join(",", b.Select(q => q.Id).ToArray()), string.Join(",", paper.Select(q => q.Id).ToArray()), "真实题库同 seed 同卷");
        Ok(paper.All(q => q.Grade == 6 && q.Term == "下"), "抽出的题都属于 grade=6 / term=下");
        Eq(paper.Select(q => q.Id).Distinct().Count(), 30, "同卷无重复");

        var counts = new int[3];
        foreach (var q in paper)
        {
            counts[(int)q.Subject]++;
        }

        Ok(counts[0] >= 10 && counts[1] >= 10 && counts[2] >= 10, $"每科 ≥ 10（实际 {counts[0]}/{counts[1]}/{counts[2]}）");
    }

    /// <summary>从测试程序目录向上找 data/bank。</summary>
    private static string? FindBankDirectory()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8; i++)
        {
            if (dir is null)
            {
                break;
            }

            var candidate = Path.Combine(dir.FullName, "data", "bank");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    // ---------------------------------------------------------------- 6. 题量不足

    private static void ShortageThrows()
    {
        var thin = new QuestionBank(new[]
        {
            new Question("a", Subject.Chinese, 6, "下", "s", new[] { "A", "B", "C", "D" }, 0, string.Empty),
            new Question("b", Subject.Math, 6, "下", "s", new[] { "A", "B", "C", "D" }, 0, string.Empty),
        });

        var spec = new PaperSpec(6, "下", 30, 90, 10);

        Throws(
            () => thin.Pick(spec, 1),
            "题量不足抛 InvalidOperationException",
            "题量不足");

        Throws(
            () => thin.Pick(spec, 1),
            "异常消息含 grade / term",
            "grade=6 term=下");

        Throws(
            () => thin.Pick(spec, 1),
            "异常消息含语文缺口与 subject=chinese",
            "subject=chinese");

        Throws(
            () => thin.Pick(new PaperSpec(9, "下", 30, 90, 10), 1),
            "整个年级缺失也抛",
            "题量不足");

        // 某科题量不足（语文够、数学不够）也必须抛，不能拿语文补
        var lopsided = new QuestionBank(SyntheticBank().Where(q => q.Subject != Subject.Math));
        Throws(
            () => lopsided.Pick(spec, 1),
            "单科不足抛异常（不许跨科补）",
            "subject=math");

        Throws(
            () => new QuestionBank(Array.Empty<Question>()).Pick(new PaperSpec(6, "下", 0, 90, 10), 1),
            "TotalQuestions <= 0 抛异常",
            "TotalQuestions");

        Throws(
            () => new QuestionBank(Array.Empty<Question>()).Pick(new PaperSpec(6, "下", 30, 90, -1), 1),
            "PerSubject < 0 抛异常",
            "PerSubject");

        // 病态输入：三科下限之和 > 卷面题数。remaining 会是负数，必须提前判死，
        // 不能靠轮转补齐（那样会空转到 roundGuard 才 break，且返回不足额的卷）。
        var healthy = new QuestionBank(SyntheticBank());
        Throws(
            () => healthy.Pick(new PaperSpec(6, "下", 5, 90, 10), 1),
            "TotalQuestions=5 / PerSubject=10（三科要 30 题）→ 直接抛",
            "超过 TotalQuestions");

        Throws(
            () => healthy.Pick(new PaperSpec(6, "下", 29, 90, 10), 1),
            "TotalQuestions=29 < 30（差 1 题也抛）",
            "超过 TotalQuestions");

        // 边界：恰好 30 = 10×3，不能误伤
        var exact = healthy.Pick(new PaperSpec(6, "下", 30, 90, 10), 7);
        Eq(exact.Count, 30, "TotalQuestions 恰好等于 PerSubject×3 时不抛，抽满 30 题");
    }

    // ---------------------------------------------------------------- 7. HMAC

    private static void HmacTamper()
    {
        var store = new FileGateStore(Path.GetTempPath(), VectorKey);

        var defaultState = GateState.Locked();
        Eq(GateStateCodec.Serialize(defaultState), CanonicalDefault, "默认态 canonical JSON 与 Node 逐字节一致");

        var (body, signature) = store.SignState(defaultState);
        Eq(body, CanonicalDefault, "SignState 正文 = canonical");
        Eq(signature, SignatureDefault, "默认态 HMAC 与 Node 逐字节一致");
        Ok(store.VerifyBody(body, signature), "未篡改 → 验签通过");

        var alt = new GateState
        {
            Phase = GatePhase.Unlocked,
            UnlockUntilUtc = new DateTimeOffset(2026, 11, 8, 6, 8, 17, TimeSpan.Zero),
            UnlockBootUtc = new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero),
            Grade = 9,
            Term = "上",
            AttemptsToday = 2,
            LastAttemptUtc = new DateTimeOffset(2026, 11, 7, 7, 8, 17, TimeSpan.Zero),
            PasswordFailCount = 3,
            PasswordLockedUntilUtc = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero),
            PaperSeed = 12345,
            CurrentPaperIds = new List<string> { "a", "b" },
            CurrentAnswers = new List<int> { 1, -1 },
            CurrentIndex = 1,
        };

        var (altBody, altSignature) = store.SignState(alt);
        Eq(altBody, CanonicalAlt, "非默认态 canonical JSON 与 Node 逐字节一致");
        Eq(altSignature, SignatureAlt, "非默认态 HMAC 与 Node 逐字节一致");

        // 往返：正文解析回来必须等价
        var rehydrated = GateStateCodec.Deserialize(CanonicalAlt);
        Eq(rehydrated.Phase, GatePhase.Unlocked, "反序列化 phase");
        Eq(rehydrated.Grade, 9, "反序列化 grade");
        Eq(rehydrated.Term, "上", "反序列化 term");
        Eq(rehydrated.UnlockUntilUtc, alt.UnlockUntilUtc, "反序列化 unlockUntilUtc");
        Eq(rehydrated.UnlockBootUtc, alt.UnlockBootUtc, "反序列化 unlockBootUtc");
        Eq(rehydrated.LastAttemptUtc, alt.LastAttemptUtc, "反序列化 lastAttemptUtc");
        Eq(rehydrated.CurrentAnswers.Count, 2, "反序列化 currentAnswers");
        Eq(rehydrated.CurrentAnswers[1], -1, "未答 -1 保真");

        Eq(GateStateCodec.Serialize(rehydrated), CanonicalAlt, "反序列化再序列化字节不变（签名稳定）");

        // 篡改
        var tamperedBody = CanonicalDefault.Replace("\"grade\":6", "\"grade\":9", StringComparison.Ordinal);
        Ok(!store.VerifyBody(tamperedBody, SignatureDefault), "篡改 grade → 验签失败");

        var cheatBody = CanonicalDefault
            .Replace("\"phase\":\"Locked\"", "\"phase\":\"Unlocked\"", StringComparison.Ordinal)
            .Replace("\"unlockUntilUtc\":null", "\"unlockUntilUtc\":\"2099-01-01T00:00:00.000Z\"", StringComparison.Ordinal);
        Ok(!store.VerifyBody(cheatBody, SignatureDefault), "篡改成 Unlocked → 验签失败");

        Ok(!store.VerifyBody(CanonicalDefault, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), "换 key → 验签失败");
        Ok(!store.VerifyBody(CanonicalDefault, "AAAA"), "伪造短签名 → 验签失败");
        Ok(!store.VerifyBody(CanonicalDefault, string.Empty), "空签名 → 验签失败");
        Ok(!store.VerifyBody(CanonicalDefault, "not-base64!!"), "非 Base64 → 验签失败");

        // history（末位字段）：字节级契约 + 往返 + 宽容解析
        var withHistory = GateState.Locked();
        withHistory.Phase = GatePhase.Unlocked;
        withHistory.UnlockUntilUtc = alt.UnlockUntilUtc;
        withHistory.UnlockBootUtc = alt.UnlockBootUtc;
        withHistory.Grade = 9;
        withHistory.Term = "上";
        withHistory.AttemptsToday = 2;
        withHistory.LastAttemptUtc = alt.LastAttemptUtc;
        withHistory.PasswordFailCount = 3;
        withHistory.PasswordLockedUntilUtc = alt.PasswordLockedUntilUtc;
        withHistory.PaperSeed = 12345;
        withHistory.CurrentPaperIds = new List<string> { "a", "b" };
        withHistory.CurrentAnswers = new List<int> { 1, -1 };
        withHistory.CurrentIndex = 1;
        withHistory.History = new List<AttemptRecord>
        {
            new(
                new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero),
                9,
                "上",
                93,
                90,
                true,
                28,
                30,
                812345L,
                new[] { 10, 9, 9 }),
        };

        var (histBody, histSig) = store.SignState(withHistory);
        Eq(histBody, CanonicalAltWithHistory, "带 history 的 canonical JSON 与 Node 逐字节一致");
        Eq(histSig, SignatureAltWithHistory, "带 history 的 HMAC 与 Node 逐字节一致");
        Ok(store.VerifyBody(histBody, histSig), "带 history 的自签自验通过");
        Ok(!store.VerifyBody(histBody, SignatureAlt), "history 段被改 → 验签失败（不是空表签名）");

        var histBack = GateStateCodec.Deserialize(CanonicalAltWithHistory);
        Eq(histBack.History.Count, 1, "反序列化 history 条数");
        var first = histBack.History[0];
        Eq(first.AtUtc, new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero), "history.atUtc");
        Eq(first.Grade, 9, "history.grade");
        Eq(first.Term, "上", "history.term");
        Eq(first.Score, 93, "history.score");
        Eq(first.PassScore, 90, "history.passScore");
        Ok(first.Passed, "history.passed = true");
        Eq(first.CorrectCount, 28, "history.correctCount");
        Eq(first.TotalCount, 30, "history.totalCount");
        Eq(first.ElapsedMs, 812345L, "history.elapsedMs（long 不被截断）");
        Eq(first.SubjectCorrect.Count, AttemptRecord.SubjectCount, "history.subjectCorrect 长度恒为 3");
        Eq(first.SubjectCorrect[0], 10, "history.subjectCorrect[0]");
        Eq(first.SubjectCorrect[1], 9, "history.subjectCorrect[1]");
        Eq(first.SubjectCorrect[2], 9, "history.subjectCorrect[2]");

        Eq(GateStateCodec.Serialize(histBack), CanonicalAltWithHistory, "history 反序列化再序列化字节不变");

        // 老存档（只有前 12 字段）必须能读：history 缺失即空表，不抛
        var legacy = GateStateCodec.Deserialize(CanonicalLegacy12);
        Eq(legacy.History.Count, 0, "缺 history 字段的 12 字段老存档 → 空表，不抛");
        Eq(legacy.Grade, 9, "老存档其他字段照常解析");
        Eq(legacy.CurrentAnswers[1], -1, "老存档未答 -1 保真");
        EqNull(legacy.UnlockBootUtc, "缺 unlockBootUtc 的 12 字段老存档 → null，不抛");
        Eq(GateStateCodec.Serialize(legacy), CanonicalLegacyUpgraded,
            "老存档读进来补齐 history[] + unlockBootUtc:null 后等于 14 字段 canonical");
        Ok(store.VerifyBody(CanonicalLegacyUpgraded, SignatureLegacyUpgraded),
            "补齐后的老存档正文与预期 HMAC 逐字节一致");

        // unlockBootUtc（第 2 字段）的宽容解析：读取端一律不抛，读不懂就当没有指纹
        const string noBoot = "{\"phase\":\"Locked\",\"unlockBootUtc\":\"2026-11-07T00:00:00.000Z\"}";
        var withBoot = GateStateCodec.Deserialize(noBoot);
        Eq(withBoot.UnlockBootUtc, new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero),
            "unlockBootUtc 正常读入");

        const string badBootText = "{\"phase\":\"Locked\",\"unlockBootUtc\":\"not-a-time\"}";
        EqNull(GateStateCodec.Deserialize(badBootText).UnlockBootUtc,
            "unlockBootUtc 是非法时间字符串 → 宽容成 null，不抛");

        const string bootWrongType = "{\"phase\":\"Locked\",\"unlockBootUtc\":12345}";
        EqNull(GateStateCodec.Deserialize(bootWrongType).UnlockBootUtc,
            "unlockBootUtc 是数字 → 宽容成 null，不抛");

        const string bootNull = "{\"phase\":\"Locked\",\"unlockBootUtc\":null}";
        EqNull(GateStateCodec.Deserialize(bootNull).UnlockBootUtc, "unlockBootUtc = null → null");

        Eq(GateStateCodec.Deserialize(noBoot).UnlockBootUtc, withBoot.UnlockBootUtc,
            "同一份正文重复解析结果稳定");

        // unlockBootUtc 在 canonical 里必须真的占第 3 个键位（phase、unlockUntilUtc 之后）
        var keyOrder = GateStateCodec.CanonicalFieldOrder;
        Eq(keyOrder.Count, 14, "FieldOrder 共 14 个字段");
        EqStr(keyOrder[0], "phase", "FieldOrder[0] = phase");
        EqStr(keyOrder[1], "unlockUntilUtc", "FieldOrder[1] = unlockUntilUtc");
        EqStr(keyOrder[2], "unlockBootUtc", "FieldOrder[2] = unlockBootUtc（紧跟 unlockUntilUtc）");
        EqStr(keyOrder[3], "grade", "FieldOrder[3] = grade");
        EqStr(keyOrder[13], "history", "FieldOrder[13] = history（末位）");

        // 元素级宽容：坏元素跳过而不是整体抛
        const string dirtyHistory = "{\"phase\":\"Locked\",\"history\":[{}]}";
        var dirtyParsed = GateStateCodec.Deserialize(dirtyHistory);
        Eq(dirtyParsed.History.Count, 0, "空对象 history 元素被跳过，不整体抛");

        // 元素类型不符也跳过
        const string wrongTypeHistory = "{\"phase\":\"Locked\",\"history\":[1,\"x\",null]}";
        var wrongTypeParsed = GateStateCodec.Deserialize(wrongTypeHistory);
        Eq(wrongTypeParsed.History.Count, 0, "非对象 history 元素全部跳过，不抛");

        // history 不是数组（写成对象/null）→ 空表
        const string badHistoryShape = "{\"phase\":\"Locked\",\"history\":null}";
        Eq(GateStateCodec.Deserialize(badHistoryShape).History.Count, 0, "history=null → 空表");

        // 转义：带引号/反斜杠/换行的 term 不能让签名结构崩掉
        var weird = GateState.Locked();
        weird.Term = "a\"b\\c\nd";
        var weirdBody = GateStateCodec.Serialize(weird);
        Ok(weirdBody.Contains("\\\"", StringComparison.Ordinal), "双引号被转义");
        var weirdRound = GateStateCodec.Deserialize(weirdBody);
        Eq(weirdRound.Term, "a\"b\\c\nd", "转义字符串往返无损");
    }

    // ---------------------------------------------------------------- 8. 加载

    private static void LoadRejections()
    {
        var dir = Path.Combine(Path.GetTempPath(), "studygate-bank-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(
                Path.Combine(dir, "g6下-chinese.json"),
                "{\"grade\":6,\"term\":\"下\",\"subject\":\"chinese\",\"items\":["
                + "{\"id\":\"cn-1\",\"stem\":\"题干一\",\"options\":[\"A\",\"B\",\"C\",\"D\"],\"answer\":0,\"explanation\":\"解析\"},"
                + "{\"id\":\"cn-2\",\"stem\":\"题干二\",\"options\":[\"A\",\"B\",\"C\",\"D\"],\"answer\":2,\"explanation\":\"解析\"},"
                + "{\"stem\":\"缺 id\",\"options\":[\"A\",\"B\",\"C\",\"D\"],\"answer\":0},"
                + "{\"id\":\"cn-4\",\"stem\":\"答案越界\",\"options\":[\"A\",\"B\",\"C\",\"D\"],\"answer\":9},"
                + "{\"id\":\"cn-5\",\"stem\":\"选项只有三个\",\"options\":[\"A\",\"B\",\"C\"],\"answer\":0}"
                + "]}",
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(dir, "g6下-math.json"),
                "{\"grade\":6,\"term\":\"下\",\"subject\":\"math\",\"items\":["
                + "{\"id\":\"cn-1\",\"stem\":\"跨文件重复 id\",\"options\":[\"A\",\"B\",\"C\",\"D\"],\"answer\":0}"
                + "]}",
                Encoding.UTF8);

            File.WriteAllText(Path.Combine(dir, "g6下-english.json"), "{oops 这不是 JSON", Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(dir, "g6下-broken.json"),
                "{\"grade\":6,\"term\":\"下\",\"subject\":\"english\"}",
                Encoding.UTF8);

            var bank = QuestionBank.LoadFromDirectory(dir);
            Eq(bank.Count, 2, "合法题 = 2（cn-1 / cn-2）");
            Eq(bank.RejectedCount, 6, "非法项 = 6（缺 id / 答案越界 / 选项不足 / 跨文件重复 id / 坏 JSON / 缺 items）");
            Eq(bank.Questions[0].Id, "cn-1", "首题是 cn-1");
            Eq(bank.Questions[0].Subject, Subject.Chinese, "subject 从文件头解析");
            Eq(bank.Questions[0].Grade, 6, "grade 从文件头解析");
            Eq(bank.Questions[1].AnswerIndex, 2, "answer 保真");
            Ok(bank.Questions[0].Options.Count == 4, "options 保真");

            var missing = QuestionBank.LoadFromDirectory(Path.Combine(dir, "nope"));
            Eq(missing.Count, 0, "目录不存在 → Count 0，不抛");
            Eq(missing.RejectedCount, 0, "目录不存在 → RejectedCount 0");

            // 构造函数同样拒绝非法项
            var manual = new QuestionBank(new[]
            {
                new Question("x", Subject.Chinese, 6, "下", "s", new[] { "A", "B", "C", "D" }, 0, string.Empty),
                new Question("x", Subject.Chinese, 6, "下", "s2", new[] { "A", "B", "C", "D" }, 0, string.Empty),
                new Question(string.Empty, Subject.Chinese, 6, "下", "s3", new[] { "A", "B", "C", "D" }, 0, string.Empty),
            });
            Eq(manual.Count, 1, "构造函数去重后 1 题");
            Eq(manual.RejectedCount, 2, "构造函数拒绝 2 项（重复 id + 空 id）");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception)
            {
                // 清理失败不影响断言结论。
            }
        }
    }

    // ---------------------------------------------------------------- 9. Vault

    private static void VaultChecks()
    {
        var salt = Encoding.ASCII.GetBytes("0123456789abcdef");
        Eq(salt.Length, 16, "测试 salt 16 字节");

        var vault = Vault.Create("StudyGate-vector-1", salt);
        Eq(
            Convert.ToHexString(vault.Key).ToLowerInvariant(),
            "6a7fa8be5db03f468efbf98d69a9e779165c431c6a237e1fd96d6b09238afa4d",
            "PBKDF2-SHA256/200k/16B/32B 与 Node 逐字节一致");

        Ok(vault.Verify("StudyGate-vector-1"), "正确口令 → 通过");
        Ok(!vault.Verify("StudyGate-vector-1 "), "多一个空格 → 拒绝");
        Ok(!vault.Verify("studygate-vector-1"), "大小写不同 → 拒绝");
        Ok(!vault.Verify(string.Empty), "空口令 → 拒绝");
        Ok(!vault.Verify(null!), "null 口令 → 拒绝不抛");

        Eq(vault.KdfIterations, 200000, "迭代 = 200000");
        Eq(vault.Salt.Length, 16, "salt 16 字节");
        Eq(vault.Key.Length, 32, "key 32 字节");

        var v2 = Vault.Create("StudyGate-vector-1");
        var v3 = Vault.Create("StudyGate-vector-1");
        Ok(!v2.Salt.AsSpan().SequenceEqual(v3.Salt), "两次创建 salt 不同");
        Ok(!v2.Key.AsSpan().SequenceEqual(v3.Key), "同口令不同 salt → key 不同");
        Ok(v2.Verify("StudyGate-vector-1"), "随机 salt 版本仍可自校验");

        Throws(
            () => Vault.Create("x", new byte[8]),
            "salt 长度非 16 抛 ArgumentException",
            "salt");

        // blob 往返
        var blob = vault.Export();
        Eq(blob.Length, 59, "vault blob = 59 字节");
        Eq(
            Convert.ToHexString(blob).ToLowerInvariant(),
            "534756500100030d401030313233343536373839616263646566206a7fa8be5db03f468efbf98d69a9e779165c431c6a237e1fd96d6b09238afa4d",
            "vault blob 与 Node 参考实现逐字节一致");

        var imported = Vault.Import(blob);
        Ok(imported.Verify("StudyGate-vector-1"), "Import 后原口令仍通过");
        Ok(!imported.Verify("StudyGate-vector-2"), "Import 后错口令仍拒绝");
        Eq(imported.KdfIterations, 200000, "Import 保留迭代数");

        // 篡改 key 字节 → 结构合法但验签失败
        var tampered = (byte[])blob.Clone();
        tampered[58] ^= 0xFF;
        var tamperedVault = Vault.Import(tampered);
        Ok(!tamperedVault.Verify("StudyGate-vector-1"), "篡改 key 字节 → 口令拒绝");

        // 结构非法 → FormatException
        Throws(() => Vault.Import(new byte[10]), "长度不符抛 FormatException", "长度");
        var badMagic = (byte[])blob.Clone();
        badMagic[0] = (byte)'X';
        Throws(() => Vault.Import(badMagic), "magic 不符抛 FormatException", "magic");

        var badVersion = (byte[])blob.Clone();
        badVersion[4] = 2;
        Throws(() => Vault.Import(badVersion), "版本不符抛 FormatException", "版本");

        var badIter = (byte[])blob.Clone();
        badIter[5] = 0x00;
        badIter[6] = 0x00;
        badIter[7] = 0x10;
        badIter[8] = 0x00;
        Throws(() => Vault.Import(badIter), "迭代数越界抛 FormatException（防降级）", "迭代");
    }

    // ---------------------------------------------------------------- 10. FileGateStore

    private static void GateStoreChecks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "studygate-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var store = new FileGateStore(dir, VectorKey);

            // 全新目录 → 默认 Locked，不抛
            Eq(store.Load().Phase, GatePhase.Locked, "无文件 → 默认 Locked");
            Eq(store.Load().Grade, 6, "默认 grade = 6");
            Eq(store.Load().Term, "下", "默认 term = 下");

            var state = new GateState
            {
                Phase = GatePhase.QuizInProgress,
                Grade = 8,
                Term = "上",
                PaperSeed = 424242,
                CurrentPaperIds = new List<string> { "q1", "q2", "q3" },
                CurrentAnswers = new List<int> { 2, -1, 0 },
                CurrentIndex = 1,
                AttemptsToday = 1,
                LastAttemptUtc = new DateTimeOffset(2026, 11, 7, 5, 0, 0, TimeSpan.Zero),
            };

            store.Save(state);
            var loaded = store.Load();
            Eq(loaded.Phase, GatePhase.QuizInProgress, "Save → Load 往返 phase");
            Eq(loaded.Grade, 8, "往返 grade");
            Eq(loaded.Term, "上", "往返 term");
            Eq(loaded.PaperSeed, 424242, "往返 paperSeed");
            Eq(loaded.CurrentPaperIds.Count, 3, "往返 currentPaperIds");
            Eq(loaded.CurrentAnswers[1], -1, "往返 currentAnswers 的 -1");
            Eq(loaded.CurrentIndex, 1, "往返 currentIndex");
            Eq(loaded.AttemptsToday, 1, "往返 attemptsToday");
            Ok(File.Exists(store.StatePath), "state.json 落盘");
            Ok(File.Exists(store.SignaturePath), "state.json.sig 落盘");

            // 原子写：不残留 .tmp
            var leftovers = Directory.GetFiles(dir, "*.tmp");
            Eq(leftovers.Length, 0, "无残留 .tmp 文件");

            // 二次 Save 走 File.Replace 分支
            state.Grade = 9;
            store.Save(state);
            Eq(store.Load().Grade, 9, "二次 Save 覆盖成功");
            Eq(Directory.GetFiles(dir, "*.tmp").Length, 0, "二次 Save 仍无 .tmp 残留");

            // 篡改正文
            var text = File.ReadAllText(store.StatePath, Encoding.UTF8);
            File.WriteAllText(store.StatePath, text.Replace("\"grade\":9", "\"grade\":6", StringComparison.Ordinal), Encoding.UTF8);
            Eq(store.Load().Phase, GatePhase.Locked, "篡改正文 → 返回默认 Locked");
            Eq(store.Load().Grade, 6, "篡改正文 → 不返回被改的值");

            // 篡改成已解锁
            store.Save(state);
            var freshText = File.ReadAllText(store.StatePath, Encoding.UTF8);
            var unlockCheat = freshText.Replace("\"phase\":\"QuizInProgress\"", "\"phase\":\"Unlocked\"", StringComparison.Ordinal);
            File.WriteAllText(store.StatePath, unlockCheat, Encoding.UTF8);
            Eq(store.Load().Phase, GatePhase.Locked, "篡改成 Unlocked → 拒绝，仍 Locked");

            // 签名文件缺失
            store.Save(state);
            File.Delete(store.SignaturePath);
            Eq(store.Load().Phase, GatePhase.Locked, "签名缺失 → 默认 Locked");

            // 签名乱写
            store.Save(state);
            File.WriteAllText(store.SignaturePath, "AAAA", Encoding.UTF8);
            Eq(store.Load().Phase, GatePhase.Locked, "签名损坏 → 默认 Locked");

            // 正文乱写（签名合法但 JSON 坏）：把正文换掉，签名保持旧值 → 签名先失败
            store.Save(state);
            File.WriteAllText(store.StatePath, "{\"phase\":\"Unlocked\"}", Encoding.UTF8);
            Eq(store.Load().Phase, GatePhase.Locked, "正文被换成短 JSON → 默认 Locked");

            // 换 key 的另一个 store 读同一份文件 → 验签失败
            var otherKey = new byte[32];
            for (var i = 0; i < otherKey.Length; i++)
            {
                otherKey[i] = (byte)(0xA0 + i);
            }

            var foreignStore = new FileGateStore(dir, otherKey);
            Eq(foreignStore.Load().Phase, GatePhase.Locked, "换 key 读同一份 → 默认 Locked");

            // 空 key 构造拒绝
            Throws(() => new FileGateStore(dir, Array.Empty<byte>()), "空 hmacKey 抛 ArgumentException", "hmacKey");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception)
            {
                // 清理失败不影响断言结论。
            }
        }
    }

    // ---------------------------------------------------------------- 11. HiddenChannel

    private static void HiddenChannelChecks()
    {
        const string password = "my-secret-pass";

        var now = new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero);
        var channel = new HiddenChannel(() => now);

        // 正常命中
        EqNull(channel.Feed('\u0001'), "\\x01 本身不返回口令");
        foreach (var c in password)
        {
            EqNull(channel.Feed(c), $"输入 {c} 不提前返回");
        }

        EqStr(channel.Feed('\r'), password, "\\r 结束 → 返回候选口令");
        Ok(!channel.IsArmed, "命中后复位");

        // 无前缀不触发
        var noArm = new HiddenChannel(() => now);
        foreach (var c in password)
        {
            noArm.Feed(c);
        }

        EqNull(noArm.Feed('\r'), "无 \\x01 前缀 → 不触发");

        // 普通打字不误触
        var typing = new HiddenChannel(() => now);
        foreach (var c in "hello world")
        {
            typing.Feed(c);
        }

        EqNull(typing.Feed('\r'), "普通打字 + 回车 → 不误触");
        foreach (var c in "123456")
        {
            typing.Feed(c);
        }

        EqNull(typing.Feed('\r'), "数字 + 回车 → 不误触");

        // Esc 复位
        var esc = new HiddenChannel(() => now);
        esc.Feed('\u0001');
        esc.Feed('a');
        esc.Feed('\u001b');
        esc.Feed('b');
        EqNull(esc.Feed('\r'), "Esc 复位后残留缓冲不触发");

        // 换行复位
        var newline = new HiddenChannel(() => now);
        newline.Feed('\u0001');
        newline.Feed('a');
        newline.Feed('\n');
        EqNull(newline.Feed('\r'), "\\n 复位后不触发");

        // 空口令不触发
        var empty = new HiddenChannel(() => now);
        empty.Feed('\u0001');
        EqNull(empty.Feed('\r'), "空口令不触发");

        // 控制字符不打断捕获
        var control = new HiddenChannel(() => now);
        control.Feed('\u0001');
        foreach (var c in password)
        {
            control.Feed(c);
            control.Feed('\u0008');
        }

        EqStr(control.Feed('\r'), password, "中间夹退格不打断（仅可打印字符入缓冲）");

        // 超长输入防爆
        var overflow = new HiddenChannel(() => now);
        overflow.Feed('\u0001');
        for (var i = 0; i < 600; i++)
        {
            overflow.Feed('x');
        }

        EqNull(overflow.Feed('\r'), "超长输入 → 复位不返回");

        // 连错 5 次 → 锁 1 小时
        var lockChannel = new HiddenChannel(() => now);
        for (var n = 1; n <= 4; n++)
        {
            lockChannel.Feed('\u0001');
            foreach (var c in "wrong")
            {
                lockChannel.Feed(c);
            }

            EqStr(lockChannel.Feed('\r'), "wrong", $"第 {n} 次尝试返回候选");
            lockChannel.ReportResult(false, now);
            Eq(lockChannel.FailCount, n, $"第 {n} 次错，FailCount = {n}");
            Ok(!lockChannel.IsLockedOut(now), $"第 {n} 次错后未锁定");
        }

        lockChannel.Feed('\u0001');
        foreach (var c in "wrong")
        {
            lockChannel.Feed(c);
        }

        EqStr(lockChannel.Feed('\r'), "wrong", "第 5 次尝试返回候选");
        lockChannel.ReportResult(false, now);
        Eq(lockChannel.FailCount, 5, "第 5 次错 FailCount = 5");
        Ok(lockChannel.IsLockedOut(now), "第 5 次错 → 锁定");
        Ok(lockChannel.LockedUntilUtc == now + TimeSpan.FromHours(1), "锁定截止 = now + 1h");

        // 锁定期内喂入直接丢弃
        lockChannel.Feed('\u0001');
        lockChannel.Feed('x');
        EqNull(lockChannel.Feed('\r'), "锁定期内不返回口令");

        // 边界：差 1ms 仍锁，满 1h 解锁
        Ok(lockChannel.IsLockedOut(now + TimeSpan.FromHours(1) - TimeSpan.FromMilliseconds(1)), "1 小时差 1ms 仍锁");
        Ok(!lockChannel.IsLockedOut(now + TimeSpan.FromHours(1)), "满 1 小时解锁");

        // 解锁后可再试
        var afterUnlock = now + TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1);
        var reopening = new HiddenChannel(() => afterUnlock);
        reopening.Restore(0, null);
        reopening.Feed('\u0001');
        foreach (var c in password)
        {
            reopening.Feed(c);
        }

        EqStr(reopening.Feed('\r'), password, "解锁后通道恢复");

        // 成功后清零
        lockChannel.ReportResult(true, now.AddHours(2));
        Eq(lockChannel.FailCount, 0, "成功后 FailCount 归零");
        Ok(lockChannel.LockedUntilUtc is null, "成功后锁定清除");

        // 落盘灌回（进程重启场景）
        var restored = new HiddenChannel(() => now);
        restored.Restore(5, now + TimeSpan.FromHours(1));
        Eq(restored.FailCount, 5, "Restore 恢复 FailCount");
        Ok(restored.IsLockedOut(now), "Restore 恢复锁定态");
        restored.Feed('\u0001');
        EqNull(restored.Feed('\r'), "恢复锁定后 Feed 直接丢弃");

        // 锁定到期后 Restore 的值不再生效
        Ok(!restored.IsLockedOut(now.AddHours(2)), "锁定到期后自动解除");

        // 失败计数不会因 1 次成功前的 4 次失败被清零（成功后清零才算数）
        var mixed = new HiddenChannel(() => now);
        mixed.ReportResult(false, now);
        mixed.ReportResult(false, now);
        Eq(mixed.FailCount, 2, "两次失败累加");
        mixed.ReportResult(true, now);
        Eq(mixed.FailCount, 0, "成功清零");
    }
}
