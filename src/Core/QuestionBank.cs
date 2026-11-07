namespace StudyGate.Core;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class QuestionBank
{
    private const int RequiredOptions = 4;

    private readonly List<Question> _questions;

    public QuestionBank(IEnumerable<Question> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        _questions = new List<Question>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rejected = 0;

        foreach (var candidate in questions)
        {
            if (!IsAcceptable(candidate, seen))
            {
                rejected++;
                continue;
            }

            seen.Add(candidate.Id);
            _questions.Add(candidate);
        }

        RejectedCount = rejected;
    }

    private QuestionBank(List<Question> questions, int rejectedCount)
    {
        _questions = questions;
        RejectedCount = rejectedCount;
    }

    public int Count => _questions.Count;

    /// <summary>被拒条目累计数（非法 JSON、字段缺失、答案越界、id 重复）。不静默吞掉。</summary>
    public int RejectedCount { get; }

    /// <summary>已加载题目的只读快照。</summary>
    public IReadOnlyList<Question> Questions => _questions;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>读目录下 *.json。目录不存在返回空实例（Count=0），不抛。</summary>
    public static QuestionBank LoadFromDirectory(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return new QuestionBank(new List<Question>(), 0);
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception)
        {
            return new QuestionBank(new List<Question>(), 0);
        }

        var questions = new List<Question>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rejected = 0;

        foreach (var file in files)
        {
            BankDocument? doc;
            try
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                doc = JsonSerializer.Deserialize<BankDocument>(text, JsonOptions);
            }
            catch (Exception)
            {
                rejected++;
                continue;
            }

            if (doc is null || doc.Items is null || doc.Grade is null
                || string.IsNullOrWhiteSpace(doc.Term) || string.IsNullOrWhiteSpace(doc.Subject)
                || !TryParseSubject(doc.Subject!, out var fileSubject))
            {
                rejected++;
                continue;
            }

            var fileGrade = doc.Grade.Value;
            var fileTerm = doc.Term!;

            foreach (var item in doc.Items)
            {
                if (item is null || item.Answer is null)
                {
                    rejected++;
                    continue;
                }

                var options = item.Options is null
                    ? Array.Empty<string>()
                    : item.Options.Select(option => option ?? string.Empty).ToArray();

                var question = new Question(
                    item.Id ?? string.Empty,
                    fileSubject,
                    fileGrade,
                    fileTerm,
                    item.Stem ?? string.Empty,
                    options,
                    item.Answer.Value,
                    item.Explanation ?? string.Empty);

                if (!IsAcceptable(question, seen))
                {
                    rejected++;
                    continue;
                }

                seen.Add(question.Id);
                questions.Add(question);
            }
        }

        return new QuestionBank(questions, rejected);
    }

    private static bool TryParseSubject(string raw, out Subject subject)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "chinese":
            case "语文":
                subject = Subject.Chinese;
                return true;
            case "math":
            case "数学":
                subject = Subject.Math;
                return true;
            case "english":
            case "英语":
                subject = Subject.English;
                return true;
            default:
                subject = Subject.Chinese;
                return false;
        }
    }

    private static bool IsAcceptable(Question? question, HashSet<string> seen)
    {
        if (question is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(question.Id) || seen.Contains(question.Id))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(question.Stem))
        {
            return false;
        }

        if (question.Options is null || question.Options.Count != RequiredOptions)
        {
            return false;
        }

        foreach (var option in question.Options)
        {
            if (option is null)
            {
                return false;
            }
        }

        if (question.AnswerIndex < 0 || question.AnswerIndex >= question.Options.Count)
        {
            return false;
        }

        if (!Enum.IsDefined(question.Subject))
        {
            return false;
        }

        if (question.Grade < 1 || string.IsNullOrWhiteSpace(question.Term))
        {
            return false;
        }

        return true;
    }

    /// <summary>确定性抽题：同 seed 必得同卷，每科至少 spec.PerSubject 题，题量不足抛 InvalidOperationException。</summary>
    public IReadOnlyList<Question> Pick(PaperSpec spec, int seed)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.TotalQuestions <= 0)
        {
            throw new InvalidOperationException(
                $"PaperSpec.TotalQuestions 必须为正数，实际 {spec.TotalQuestions}（grade={spec.Grade} term={spec.Term}）。");
        }

        if (spec.PerSubject < 0)
        {
            throw new InvalidOperationException(
                $"PaperSpec.PerSubject 不得为负，实际 {spec.PerSubject}（grade={spec.Grade} term={spec.Term}）。");
        }

        // 病态输入防护：三科下限之和已经超过卷面题数时，
        // remaining 会是负数，配额轮转永远补不上，只能判死。
        // 在进入池构造之前就抛，避免白洗三遍牌。
        if (spec.PerSubject > 0 && (long)spec.PerSubject * 3L > spec.TotalQuestions)
        {
            throw new InvalidOperationException(
                $"PaperSpec.PerSubject 三科合计 {spec.PerSubject}×3 = {(long)spec.PerSubject * 3L} 超过 TotalQuestions={spec.TotalQuestions}"
                + $"（grade={spec.Grade} term={spec.Term}）：每科 {spec.PerSubject} 题的下限无法满足。");
        }

        var subjects = new[] { Subject.Chinese, Subject.Math, Subject.English };
        var pools = new Dictionary<Subject, List<Question>>();

        foreach (var subject in subjects)
        {
            var eligible = _questions
                .Where(q => q.Subject == subject
                            && q.Grade == spec.Grade
                            && string.Equals(q.Term, spec.Term, StringComparison.Ordinal))
                .ToList();

            Shuffle(eligible, unchecked(seed + (int)subject * 7919));
            pools[subject] = eligible;
        }

        // 配额：每科先给 PerSubject，余量按科目轮转补齐，不越过该科实际题量。
        var quota = new Dictionary<Subject, int>();
        foreach (var subject in subjects)
        {
            quota[subject] = spec.PerSubject;
        }

        var assigned = spec.PerSubject * subjects.Length;
        var remaining = spec.TotalQuestions - assigned;
        var cursor = 0;
        var guard = 0;
        var roundGuard = spec.TotalQuestions * 8;

        while (remaining > 0)
        {
            if (guard++ > roundGuard)
            {
                break;
            }

            var subject = subjects[cursor % subjects.Length];
            cursor++;

            if (quota[subject] >= pools[subject].Count)
            {
                continue;
            }

            quota[subject]++;
            remaining--;
        }

        // 某科题量不够时，把缺口让给有富余的科。
        foreach (var subject in subjects)
        {
            if (quota[subject] > pools[subject].Count)
            {
                var deficit = quota[subject] - pools[subject].Count;
                quota[subject] = pools[subject].Count;

                foreach (var donor in subjects)
                {
                    if (donor == subject || deficit <= 0)
                    {
                        continue;
                    }

                    var room = pools[donor].Count - quota[donor];
                    if (room <= 0)
                    {
                        continue;
                    }

                    var take = Math.Min(room, deficit);
                    quota[donor] += take;
                    remaining -= take;
                    deficit -= take;
                }
            }
        }

        var totalHave = subjects.Sum(subject => quota[subject]);
        if (totalHave < spec.TotalQuestions)
        {
            throw BuildShortage(spec, subjects, pools, quota, totalHave);
        }

        // 硬性下限：PerSubject 是契约下限，该科题量本身不足就必须报错，不能靠别科补。
        foreach (var subject in subjects)
        {
            if (pools[subject].Count < spec.PerSubject)
            {
                throw BuildShortage(spec, subjects, pools, quota, totalHave);
            }
        }

        var chosen = new List<Question>(totalHave);
        foreach (var subject in subjects)
        {
            chosen.AddRange(pools[subject].Take(quota[subject]));
        }

        Shuffle(chosen, unchecked(seed + 104729));

        if (chosen.Count > spec.TotalQuestions)
        {
            chosen.RemoveRange(spec.TotalQuestions, chosen.Count - spec.TotalQuestions);
        }

        return chosen;
    }

    private static InvalidOperationException BuildShortage(
        PaperSpec spec,
        Subject[] subjects,
        Dictionary<Subject, List<Question>> pools,
        Dictionary<Subject, int> quota,
        int available)
    {
        var detail = string.Join("；", subjects.Select(subject =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} (grade={1},term={2},subject={3}) 需要 {4} 题，实际 {5} 题",
                SubjectLabel(subject),
                spec.Grade,
                spec.Term,
                SubjectSlug(subject),
                Math.Max(spec.PerSubject, quota[subject]),
                pools[subject].Count)));

        return new InvalidOperationException(
            $"题量不足：grade={spec.Grade} term={spec.Term} 需要 {spec.TotalQuestions} 题，实际可抽 {available} 题。{detail}");
    }

    private static string SubjectLabel(Subject subject) => subject switch
    {
        Subject.Chinese => "语文",
        Subject.Math => "数学",
        Subject.English => "英语",
        _ => subject.ToString(),
    };

    private static string SubjectSlug(Subject subject) => subject switch
    {
        Subject.Chinese => "chinese",
        Subject.Math => "math",
        Subject.English => "english",
        _ => "unknown",
    };

    /// <summary>Fisher-Yates，底随机数为 mulberry32，与 tools/core-reference.mjs 逐位一致。</summary>
    private static void Shuffle(List<Question> list, int seed)
    {
        var state = unchecked((uint)seed);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = (int)(NextDouble(ref state) * (i + 1));
            if (j < 0)
            {
                j = 0;
            }
            else if (j > i)
            {
                j = i;
            }

            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>mulberry32。返回 [0,1)，与 Node 实现按位一致。</summary>
    private static double NextDouble(ref uint state)
    {
        unchecked
        {
            state += 0x6D2B79F5u;
            var t = state;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            var result = t ^ (t >> 14);
            return result / 4294967296.0;
        }
    }

    private sealed class BankDocument
    {
        [JsonPropertyName("grade")]
        public int? Grade { get; set; }

        [JsonPropertyName("term")]
        public string? Term { get; set; }

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("items")]
        public List<BankItem?>? Items { get; set; }
    }

    private sealed class BankItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("stem")]
        public string? Stem { get; set; }

        [JsonPropertyName("options")]
        public List<string?>? Options { get; set; }

        [JsonPropertyName("answer")]
        public int? Answer { get; set; }

        [JsonPropertyName("explanation")]
        public string? Explanation { get; set; }
    }
}
