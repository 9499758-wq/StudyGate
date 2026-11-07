namespace StudyGate.Core;

using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>
/// 状态正文的规范化 JSON 读写器。
///
/// 为什么不用 JsonSerializer 直接序列化 GateState：HMAC 是逐字节签名的，
/// 字段顺序、时间格式、null 的写法只要差一个字节，C# 的签名就和
/// tools/core-reference.mjs 的 canonicalJson() 对不上。
/// 这里手写固定字段序（见 FieldOrder）、camelCase 键、ISO-8601 毫秒 UTC 时间，
/// 与 Node 的 JSON.stringify(obj) 输出逐字节一致。
///
/// 反序列化同样手写：JSON 的 null 映射到 DateTimeOffset.MinValue（CONTRACTS A 的"从未尝试"），
/// 类型不符则抛 JsonException 由调用方 fail-closed。
/// </summary>
public static class GateStateCodec
{
    private static readonly string[] FieldOrder =
    {
        "phase",
        "unlockUntilUtc",
        "unlockBootUtc",
        "grade",
        "term",
        "attemptsToday",
        "lastAttemptUtc",
        "passwordFailCount",
        "passwordLockedUntilUtc",
        "paperSeed",
        "currentPaperIds",
        "currentAnswers",
        "currentIndex",
        "history",
    };

    public static IReadOnlyList<string> CanonicalFieldOrder => FieldOrder;

    /// <summary>规范正文。这是唯一被签名的字节序列，也是 state.json 的正文。</summary>
    public static string Serialize(GateState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sb = new StringBuilder(256);
        sb.Append('{');

        for (var i = 0; i < FieldOrder.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append('"').Append(FieldOrder[i]).Append("\":");

            switch (FieldOrder[i])
            {
                case "phase":
                    sb.Append('"').Append(PhaseName(state.Phase)).Append('"');
                    break;
                case "unlockUntilUtc":
                    AppendTimestamp(sb, state.UnlockUntilUtc);
                    break;
                case "unlockBootUtc":
                    AppendTimestamp(sb, state.UnlockBootUtc);
                    break;
                case "grade":
                    sb.Append(state.Grade.ToString(CultureInfo.InvariantCulture));
                    break;
                case "term":
                    AppendString(sb, state.Term);
                    break;
                case "attemptsToday":
                    sb.Append(state.AttemptsToday.ToString(CultureInfo.InvariantCulture));
                    break;
                case "lastAttemptUtc":
                    AppendTimestamp(sb, state.LastAttemptUtc);
                    break;
                case "passwordFailCount":
                    sb.Append(state.PasswordFailCount.ToString(CultureInfo.InvariantCulture));
                    break;
                case "passwordLockedUntilUtc":
                    AppendTimestamp(sb, state.PasswordLockedUntilUtc);
                    break;
                case "paperSeed":
                    sb.Append(state.PaperSeed.ToString(CultureInfo.InvariantCulture));
                    break;
                case "currentPaperIds":
                    AppendStringArray(sb, state.CurrentPaperIds);
                    break;
                case "currentAnswers":
                    AppendIntArray(sb, state.CurrentAnswers);
                    break;
                case "currentIndex":
                    sb.Append(state.CurrentIndex.ToString(CultureInfo.InvariantCulture));
                    break;
                case "history":
                    AppendHistory(sb, state.History);
                    break;
                default:
                    throw new InvalidOperationException($"未知状态字段：{FieldOrder[i]}");
            }
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>解析规范正文。结构或类型不符抛 JsonException。</summary>
    public static GateState Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("状态正文根节点必须是对象。");
        }

        var root = doc.RootElement;
        var state = new GateState();

        if (TryGet(root, "phase", out var phaseElement))
        {
            state.Phase = ParsePhase(phaseElement);
        }

        if (TryGet(root, "unlockUntilUtc", out var unlockElement))
        {
            state.UnlockUntilUtc = ParseNullableTimestamp(unlockElement, "unlockUntilUtc");
        }

        // unlockBootUtc 比 unlockUntilUtc 更宽容：缺失、null、非字符串、非法时间
        // 一律回落 null，不抛。理由是这个字段是"开机指纹"，
        // 一个读不懂的指纹等于"没有指纹"，判定侧本来就当"不在窗口内"处理，
        // 没必要让整份存档因为这一格不可读而 fail-closed 到默认 Locked。
        if (TryGet(root, "unlockBootUtc", out var bootElement))
        {
            state.UnlockBootUtc = TryParseTimestamp(bootElement, out var bootUtc)
                ? bootUtc.ToUniversalTime()
                : null;
        }

        if (TryGet(root, "grade", out var gradeElement))
        {
            state.Grade = ParseInt32(gradeElement, "grade");
        }

        if (TryGet(root, "term", out var termElement))
        {
            state.Term = ParseString(termElement, "term");
        }

        if (TryGet(root, "attemptsToday", out var attemptsElement))
        {
            state.AttemptsToday = ParseInt32(attemptsElement, "attemptsToday");
        }

        if (TryGet(root, "lastAttemptUtc", out var lastAttemptElement))
        {
            var parsed = ParseNullableTimestamp(lastAttemptElement, "lastAttemptUtc");
            state.LastAttemptUtc = parsed ?? DateTimeOffset.MinValue;
        }

        if (TryGet(root, "passwordFailCount", out var failElement))
        {
            state.PasswordFailCount = ParseInt32(failElement, "passwordFailCount");
        }

        if (TryGet(root, "passwordLockedUntilUtc", out var lockedElement))
        {
            state.PasswordLockedUntilUtc = ParseNullableTimestamp(lockedElement, "passwordLockedUntilUtc");
        }

        if (TryGet(root, "paperSeed", out var seedElement))
        {
            state.PaperSeed = ParseInt32(seedElement, "paperSeed");
        }

        if (TryGet(root, "currentPaperIds", out var idsElement))
        {
            state.CurrentPaperIds = ParseStringList(idsElement, "currentPaperIds");
        }

        if (TryGet(root, "currentAnswers", out var answersElement))
        {
            state.CurrentAnswers = ParseIntList(answersElement, "currentAnswers");
        }

        if (TryGet(root, "currentIndex", out var indexElement))
        {
            state.CurrentIndex = ParseInt32(indexElement, "currentIndex");
        }

        // 老 state.json 没有这一段：缺失即空表，不抛。元素自身不合格则跳过该条。
        if (TryGet(root, "history", out var historyElement))
        {
            state.History = ParseHistory(historyElement);
        }

        return state;
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        if (root.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string PhaseName(GatePhase phase) => phase switch
    {
        GatePhase.Locked => "Locked",
        GatePhase.QuizReady => "QuizReady",
        GatePhase.QuizInProgress => "QuizInProgress",
        GatePhase.Unlocked => "Unlocked",
        GatePhase.PanicBypassed => "PanicBypassed",
        _ => throw new InvalidOperationException($"未知 GatePhase 值：{(int)phase}"),
    };

    private static GatePhase ParsePhase(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numeric))
        {
            return numeric switch
            {
                0 => GatePhase.Locked,
                1 => GatePhase.QuizReady,
                2 => GatePhase.QuizInProgress,
                3 => GatePhase.Unlocked,
                4 => GatePhase.PanicBypassed,
                _ => throw new JsonException($"phase 数值越界：{numeric}"),
            };
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("phase 必须是字符串或整数。");
        }

        return element.GetString() switch
        {
            "Locked" => GatePhase.Locked,
            "QuizReady" => GatePhase.QuizReady,
            "QuizInProgress" => GatePhase.QuizInProgress,
            "Unlocked" => GatePhase.Unlocked,
            "PanicBypassed" => GatePhase.PanicBypassed,
            var other => throw new JsonException($"phase 取值非法：{other}"),
        };
    }

    private static string ParseString(JsonElement element, string field)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{field} 必须是字符串。");
        }

        return element.GetString() ?? throw new JsonException($"{field} 为 null。");
    }

    private static int ParseInt32(JsonElement element, string field)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw new JsonException($"{field} 必须是 32 位整数。");
        }

        return value;
    }

    private static DateTimeOffset? ParseNullableTimestamp(JsonElement element, string field)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{field} 必须是 ISO-8601 字符串或 null。");
        }

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new JsonException($"{field} 为空字符串。");
        }

        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            throw new JsonException($"{field} 不是合法时间：{text}");
        }

        return parsed.ToUniversalTime();
    }

    private static List<string> ParseStringList(JsonElement element, string field)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return new List<string>();
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"{field} 必须是字符串数组。");
        }

        var list = new List<string>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"{field} 的元素必须是字符串。");
            }

            list.Add(item.GetString() ?? string.Empty);
        }

        return list;
    }

    private static List<int> ParseIntList(JsonElement element, string field)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return new List<int>();
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"{field} 必须是整数数组。");
        }

        var list = new List<int>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var value))
            {
                throw new JsonException($"{field} 的元素必须是 32 位整数。");
            }

            list.Add(value);
        }

        return list;
    }

    /// <summary>与 JS Date.prototype.toISOString() 完全相同：UTC、毫秒、尾 Z。</summary>
    private static void AppendTimestamp(StringBuilder sb, DateTimeOffset? value)
    {
        if (value is null || value.Value == DateTimeOffset.MinValue)
        {
            // CONTRACTS A：从未尝试 = DateTimeOffset.MinValue，规范正文里写作 null。
            sb.Append("null");
            return;
        }

        var utc = value.Value.ToUniversalTime();
        sb.Append('"')
          .Append(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
          .Append('"');
    }

    private static void AppendString(StringBuilder sb, string? value)
    {
        if (value is null)
        {
            sb.Append("null");
            return;
        }

        AppendEscaped(sb, value);
    }

    private static void AppendStringArray(StringBuilder sb, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            sb.Append("null");
            return;
        }

        sb.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            AppendString(sb, values[i]);
        }

        sb.Append(']');
    }

    private static void AppendIntArray(StringBuilder sb, IReadOnlyList<int>? values)
    {
        if (values is null)
        {
            sb.Append("null");
            return;
        }

        sb.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }

        sb.Append(']');
    }

    /// <summary>
    /// 历史记录数组。每条固定 10 个键，顺序与 canonical 契约一致。
    /// 追加在 canonical 末尾，老存档没有这一段也不会验签失败（签名认磁盘原字节）。
    /// </summary>
    private static void AppendHistory(StringBuilder sb, IReadOnlyList<AttemptRecord>? records)
    {
        if (records is null)
        {
            sb.Append("[]");
            return;
        }

        sb.Append('[');
        for (var i = 0; i < records.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            AttemptRecord? record = records[i];
            if (record is null)
            {
                // 调用方塞了 null 元素：写空对象占位，读回时会被跳过，不破坏后续元素下标。
                sb.Append("{}");
                continue;
            }

            sb.Append("{\"atUtc\":");
            AppendTimestamp(sb, record.AtUtc);
            sb.Append(",\"grade\":").Append(record.Grade.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"term\":");
            AppendString(sb, record.Term);
            sb.Append(",\"score\":").Append(record.Score.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"passScore\":").Append(record.PassScore.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"passed\":").Append(record.Passed ? "true" : "false");
            sb.Append(",\"correctCount\":").Append(record.CorrectCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"totalCount\":").Append(record.TotalCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"elapsedMs\":").Append(record.ElapsedMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"subjectCorrect\":");
            AppendIntArray(sb, record.SubjectCorrect);
            sb.Append('}');
        }

        sb.Append(']');
    }

    /// <summary>宽容解析：非数组 → 空表；元素不是对象或必填字段缺失/类型不符 → 跳过该条。</summary>
    private static List<AttemptRecord> ParseHistory(JsonElement element)
    {
        var records = new List<AttemptRecord>();

        // null / 对象 / 字符串 / 数字一律当"没有历史"，不抛。
        if (element.ValueKind != JsonValueKind.Array)
        {
            return records;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryReadHistoryItem(item, out var record))
            {
                continue;
            }

            records.Add(record);
        }

        return records;
    }

    private static bool TryReadHistoryItem(JsonElement item, out AttemptRecord record)
    {
        record = null!;

        if (!item.TryGetProperty("atUtc", out var atElement)
            || !TryParseTimestamp(atElement, out var atUtc))
        {
            return false;
        }

        if (!TryReadInt32(item, "grade", out var grade)
            || !TryReadInt32(item, "score", out var score)
            || !TryReadInt32(item, "passScore", out var passScore)
            || !TryReadInt32(item, "correctCount", out var correctCount)
            || !TryReadInt32(item, "totalCount", out var totalCount)
            || !TryReadBool(item, "passed", out var passed)
            || !TryReadInt64(item, "elapsedMs", out var elapsedMs))
        {
            return false;
        }

        if (!item.TryGetProperty("term", out var termElement) || termElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var term = termElement.GetString();
        if (term is null)
        {
            return false;
        }

        var subjectCorrect = new int[AttemptRecord.SubjectCount];
        if (item.TryGetProperty("subjectCorrect", out var subjectElement)
            && subjectElement.ValueKind == JsonValueKind.Array
            && subjectElement.GetArrayLength() == AttemptRecord.SubjectCount)
        {
            var cursor = 0;
            foreach (var value in subjectElement.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed))
                {
                    return false;
                }

                subjectCorrect[cursor++] = parsed;
            }
        }
        else
        {
            // 缺失或长度不对：按 0 补齐，长度契约由类型保证（永远 3）。
            Array.Fill(subjectCorrect, 0);
        }

        record = new AttemptRecord(
            atUtc,
            grade,
            term,
            score,
            passScore,
            passed,
            correctCount,
            totalCount,
            elapsedMs,
            subjectCorrect);

        return true;
    }

    private static bool TryReadInt32(JsonElement parent, string name, out int value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetInt32(out value);
    }

    private static bool TryReadInt64(JsonElement parent, string name, out long value)
    {
        value = 0;
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (element.TryGetInt64(out value))
        {
            return true;
        }

        // JS 侧 Number 可能写 1e3 之类，退化成 double 再截断，保持宽容。
        if (element.TryGetDouble(out var asDouble)
            && asDouble >= long.MinValue && asDouble <= long.MaxValue)
        {
            value = (long)asDouble;
            return true;
        }

        return false;
    }

    private static bool TryReadBool(JsonElement parent, string name, out bool value)
    {
        value = false;
        if (!parent.TryGetProperty(name, out var element))
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>单个时间戳宽容解析：非法值与 null 都当"没有"，不做整体失败。</summary>
    private static bool TryParseTimestamp(JsonElement element, out DateTimeOffset value)
    {
        value = default;

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out value);
    }

    private static void AppendEscaped(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }
}
