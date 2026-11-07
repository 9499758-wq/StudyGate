using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using StudyGate.Core;
using StudyGate.Ui;

namespace StudyGate.App;

/// <summary>
/// 唯一允许同时碰 Core 与 Ui 的地方。职责：状态机 → 抽题 → 注入视图 → 判分 → 放行。
///
/// 线程模型：
///   - 公开的 On* 回调只允许在 UI 线程执行（GateWindow 事件来自 WPF 输入）。
///   - FeedHiddenChar 来自全局钩子线程（InputLock），只做纯状态机推进；
///     命中口令才 Dispatcher 切回 UI 线程落盘与放行。
///   - 所有落盘走 _store.Save，失败一律吞掉不阻断门（见 ShellExit 文件头铁律 1）。
///
/// 隐蔽口令触发源（InputLock）是第二批落盘的，本类只暴露 FeedHiddenChar 入口，
/// 不直接引用 InputLock，保证第一批三个文件自成闭合。
/// </summary>
internal sealed class Coordinator
{
    /// <summary>SPEC 第三节：每次 30 题（三科各 10）、90 分达标。</summary>
    private const int TotalQuestions = 30;
    private const int PassScore = 90;
    private const int PerSubject = 10;

    /// <summary>历史条数上限：够看趋势，又不至于把 state.json 撑大。</summary>
    private const int HistoryMax = 30;

    private readonly IGateStore _store;
    private readonly GateState _state;
    private readonly QuestionBank _bank;
    private readonly HiddenChannel _hidden;
    private readonly Vault? _vault;

    private readonly PaperSpec _spec;

    private IReadOnlyList<Question> _paper;

    /// <summary>按题索引记累计作答毫秒。重启后归零，但同进程内 TotalElapsedMs 是真实值。</summary>
    private readonly List<long> _elapsedMs = new();

    private readonly Stopwatch _questionClock = new();

    private GateWindow? _window;
    private bool _submitting;

    /// <summary>
    /// 门是否仍然关着（必须压制桌面与提权工具）。
    /// 放行或口令通道生效后立刻返回 false —— ProcessGuard 每 3 秒读一次这个状态，
    /// 允许它在此后启动 explorer 而不被自己的杀进程轮询误杀。
    /// </summary>
    internal bool IsGateClosed =>
        _state.Phase != GatePhase.Unlocked && _state.Phase != GatePhase.PanicBypassed;

    private Coordinator(
        IGateStore store,
        GateState state,
        QuestionBank bank,
        HiddenChannel hidden,
        Vault? vault)
    {
        _store = store;
        _state = state;
        _bank = bank;
        _hidden = hidden;
        _vault = vault;
        _paper = Array.Empty<Question>();
        _spec = new PaperSpec(state.Grade, state.Term, TotalQuestions, PassScore, PerSubject);
    }

    // ─────────────────────────────────────────────────────────────
    // 启动
    // ─────────────────────────────────────────────────────────────

    /// <summary>加载盘上状态、题库、口令散列。任何一项缺失都不抛，交给 Run 统一决策。</summary>
    internal static Coordinator CreateDefault()
    {
        byte[] hmacKey = GatePaths.EnsureHmacKey();
        IGateStore store = new FileGateStore(GatePaths.DataDir, hmacKey);

        GateState state;
        try
        {
            state = store.Load() ?? new GateState();
        }
        catch (Exception)
        {
            // Load 对篡改应返回 Locked；万一实现抛，这里按未解锁处理，不放行。
            state = new GateState();
        }

        QuestionBank bank = QuestionBank.LoadFromDirectory(GatePaths.BankDir);

        HiddenChannel hidden = new HiddenChannel();
        hidden.Restore(state.PasswordFailCount, state.PasswordLockedUntilUtc);

        Vault? vault = LoadVault();

        return new Coordinator(store, state, bank, hidden, vault);
    }

    private static Vault? LoadVault()
    {
        try
        {
            if (!File.Exists(GatePaths.VaultPath))
            {
                return null;
            }

            return Vault.Import(File.ReadAllBytes(GatePaths.VaultPath));
        }
        catch (FormatException)
        {
            // blob 损坏或被改：口令通道不可用，但答题门照常工作。
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 判定本次进程该做什么，返回要显示的窗口（null = 不放门，直接退出）。
    /// 三种快速出口：已在解锁窗口内、题库为空（fail-open）。
    /// </summary>
    internal Window? Run()
    {
        if (ShellExit.IsWithinUnlockWindow(_state))
        {
            ShellExit.StartExplorer();
            return null;
        }

        if (_bank.Count == 0)
        {
            // SPEC 未定义题库缺失时的行为。选择 fail-open：装错了不能把人锁在门外。
            ShellExit.StartExplorer();
            return null;
        }

        EnsurePaper();

        GateWindow window = new GateWindow();
        window.OptionSelected += OnOptionSelected;
        window.SubmitRequested += OnSubmitRequested;
        window.RetakeRequested += OnRetakeRequested;
        window.PassedCountdownElapsed += OnPassedCountdownElapsed;

        _window = window;

        Application? app = Application.Current;
        if (app is not null)
        {
            app.MainWindow = window;
        }

        // 先填内容再显示：顺序反了会先闪一帧空白卡片。
        window.LoadPaper(_spec, _paper, GradeLabel(_state.Grade, _state.Term));
        window.ShowQuestion(_state.CurrentIndex, CountAnswered(), SelectedAt(_state.CurrentIndex));

        // 没有这一句，窗口只被构建、被赋给 MainWindow，却永远不会出现在屏幕上，
        // 后面 app.Run() 就是个空转的消息循环 —— 人卡在登录后黑屏。
        window.Show();

        _questionClock.Restart();
        return window;
    }

    // ─────────────────────────────────────────────────────────────
    // 卷面
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 保证 _state.CurrentPaperIds 与 _paper 一致。
    /// 恢复靠 Pick(seed) 重放（同 seed 同卷，SPEC 第一节硬性要求），
    /// 不依赖题库提供按 id 查询；重放结果与已存 id 不符（题库被替换）时以重放为准并清答案。
    /// </summary>
    private void EnsurePaper()
    {
        if (_state.CurrentPaperIds.Count == TotalQuestions && _state.CurrentAnswers.Count == TotalQuestions)
        {
            _paper = _bank.Pick(_spec, _state.PaperSeed);

            bool same = true;
            for (int i = 0; i < TotalQuestions; i++)
            {
                if (_paper[i].Id != _state.CurrentPaperIds[i])
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                if (_state.CurrentIndex < 0 || _state.CurrentIndex >= TotalQuestions)
                {
                    _state.CurrentIndex = 0;
                }

                return;
            }
        }

        StartNewPaper(NewSeed());
    }

    private void StartNewPaper(int seed)
    {
        _state.PaperSeed = seed;
        _paper = _bank.Pick(_spec, seed);

        _state.CurrentPaperIds.Clear();
        _state.CurrentAnswers.Clear();

        for (int i = 0; i < _paper.Count; i++)
        {
            _state.CurrentPaperIds.Add(_paper[i].Id);
            _state.CurrentAnswers.Add(-1);
        }

        _elapsedMs.Clear();
        for (int i = 0; i < _paper.Count; i++)
        {
            _elapsedMs.Add(0L);
        }

        _state.CurrentIndex = 0;
        _state.Phase = GatePhase.QuizInProgress;
        _submitting = false;
        Persist();
    }

    /// <summary>不使用固定种子：重考必须换卷，且 seed 会落盘用于崩溃后重放。</summary>
    private static int NewSeed()
    {
        // 0 在契约里表示「未初始化」，避开它。
        int seed;
        do
        {
            seed = Random.Shared.Next(1, int.MaxValue);
        }
        while (seed == 0);

        return seed;
    }

    // ─────────────────────────────────────────────────────────────
    // 视图回调（UI 线程）
    // ─────────────────────────────────────────────────────────────

    private void OnOptionSelected(int optionIndex)
    {
        if (optionIndex < 0 || optionIndex > 3)
        {
            return;
        }

        int index = _state.CurrentIndex;
        if (index < 0 || index >= TotalQuestions)
        {
            return;
        }

        _state.CurrentAnswers[index] = optionIndex;

        if (index < _elapsedMs.Count)
        {
            _elapsedMs[index] += _questionClock.ElapsedMilliseconds;
        }

        _questionClock.Restart();

        Persist();

        // 最后一题答完即自动判定（SPEC 第三节），不依赖视图再发 SubmitRequested。
        if (CountAnswered() >= TotalQuestions)
        {
            Submit();
            return;
        }

        // 前进到下一道未答题；已到尾部（中间有跳答）就停在原地等用户补。
        int next = index + 1;
        while (next < TotalQuestions && _state.CurrentAnswers[next] >= 0)
        {
            next++;
        }

        if (next < TotalQuestions)
        {
            _state.CurrentIndex = next;
            Persist();
        }

        _window?.ShowQuestion(_state.CurrentIndex, CountAnswered(), SelectedAt(_state.CurrentIndex));
    }

    private void OnSubmitRequested()
    {
        Submit();
    }

    private void OnRetakeRequested()
    {
        StartNewPaper(NewSeed());

        _window?.LoadPaper(_spec, _paper, GradeLabel(_state.Grade, _state.Term));
        _window?.ShowQuestion(0, 0, -1);
        _questionClock.Restart();
    }

    private void OnPassedCountdownElapsed()
    {
        // 只有真正达标过才放行。防视图在未达标页误发倒计时事件。
        if (_state.Phase != GatePhase.Unlocked)
        {
            return;
        }

        ShellExit.UnlockAndStartExplorer(_store, _state, UnlockKind.Passed);
    }

    // ─────────────────────────────────────────────────────────────
    // 判分
    // ─────────────────────────────────────────────────────────────

    private void Submit()
    {
        if (_submitting)
        {
            return;
        }

        _submitting = true;

        // 以 state 为准重建答案记录：进程重启后内存里没有逐题记录，
        // 但 CurrentAnswers 里有完整作答。缺失项按未作答（-1）补，Grader 会算错。
        List<AnswerRecord> answers = new List<AnswerRecord>(_paper.Count);
        for (int i = 0; i < _paper.Count; i++)
        {
            Question q = _paper[i];
            int selected = SelectedAt(i);
            long elapsed = i < _elapsedMs.Count ? _elapsedMs[i] : 0L;
            answers.Add(new AnswerRecord(q.Id, selected, selected == q.AnswerIndex, elapsed));
        }

        QuizResult result = Grader.Score(_paper, answers, PassScore);

        // 必须在 Phase 被改写之前留痕：历史里的 Passed 取自 result，而不是后来的 Phase。
        RecordAttempt(result);

        _state.Phase = result.Passed ? GatePhase.Unlocked : GatePhase.QuizReady;
        if (result.Passed)
        {
            // 先占位成 Unlocked 供 OnPassedCountdownElapsed 校验；
            // 真正写 UnlockUntilUtc 与放行由 ShellExit 在 3 秒倒计时结束后完成。
            _state.UnlockUntilUtc = null;
        }
        else
        {
            _state.AttemptsToday += 1;
            _state.LastAttemptUtc = DateTimeOffset.UtcNow;
        }

        Persist();

        _window?.ShowResult(result);

        if (!result.Passed)
        {
            _submitting = false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 隐蔽口令（唯一入口，UI 全程不参与）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 由 InputLock 的键盘钩子线程调用：未被吞掉的字符逐个进来。
    /// 命中候选口令才切回 UI 线程；未命中时零开销。
    /// </summary>
    internal void FeedHiddenChar(char c)
    {
        string? candidate = _hidden.Feed(c);
        if (candidate is null)
        {
            return;
        }

        Application? app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess() == false)
        {
            // 钩子线程：切到 UI 线程再落盘/放行。
            app?.Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(() => OnHiddenCandidate(candidate)));
            return;
        }

        OnHiddenCandidate(candidate);
    }

    private void OnHiddenCandidate(string candidate)
    {
        if (_vault is null)
        {
            // 没有口令散列：通道不可用。不计数，避免把失败次数写歪。
            return;
        }

        if (_hidden.IsLockedOut())
        {
            return;
        }

        bool ok = _vault.Verify(candidate);

        _hidden.ReportResult(ok);
        _state.PasswordFailCount = _hidden.FailCount;
        _state.PasswordLockedUntilUtc = _hidden.LockedUntilUtc;

        if (ok)
        {
            Persist();
            ShellExit.UnlockAndStartExplorer(_store, _state, UnlockKind.Panic);
            return;
        }

        // 失败：只落盘计数，UI 无任何反馈（SPEC 第五节）。
        Persist();
    }

    // ─────────────────────────────────────────────────────────────
    // 工具
    // ─────────────────────────────────────────────────────────────

    private int CountAnswered()
    {
        int n = 0;
        for (int i = 0; i < _state.CurrentAnswers.Count; i++)
        {
            if (i < TotalQuestions && _state.CurrentAnswers[i] >= 0)
            {
                n++;
            }
        }

        return n;
    }

    private int SelectedAt(int index)
    {
        if (index < 0 || index >= _state.CurrentAnswers.Count)
        {
            return -1;
        }

        return _state.CurrentAnswers[index];
    }

    /// <summary>
    /// 把本次作答写进 state.History（最新在前，最多 HistoryMax 条）。
    /// CorrectCount / TotalCount 从 BySubject 累加：QuizResult 本身不带这两个标量，
    /// 因为三科分布是唯一真相，单独再存一份会有一致性风险。
    /// </summary>
    private void RecordAttempt(QuizResult result)
    {
        int[] subjectCorrect = new int[AttemptRecord.SubjectCount];

        if (result.BySubject is not null)
        {
            for (int i = 0; i < result.BySubject.Count; i++)
            {
                SubjectScore entry = result.BySubject[i];
                int slot = (int)entry.Subject;
                if (slot >= 0 && slot < subjectCorrect.Length)
                {
                    subjectCorrect[slot] = entry.Correct;
                }
            }
        }

        int correctCount = 0;
        int totalCount = 0;
        if (result.BySubject is not null)
        {
            for (int i = 0; i < result.BySubject.Count; i++)
            {
                correctCount += result.BySubject[i].Correct;
                totalCount += result.BySubject[i].Total;
            }
        }

        _state.History.Insert(0, new AttemptRecord(
            DateTimeOffset.UtcNow,
            _state.Grade,
            _state.Term,
            result.Score,
            result.PassScore,
            result.Passed,
            correctCount,
            totalCount,
            result.TotalElapsedMs,
            subjectCorrect));

        while (_state.History.Count > HistoryMax)
        {
            _state.History.RemoveAt(_state.History.Count - 1);
        }
    }

    /// <summary>最近若干次作答记录（最新在前）。Ui 只读，不得修改。</summary>
    internal IReadOnlyList<AttemptRecord> RecentAttempts => _state.History;

    private void Persist()
    {
        try
        {
            _store.Save(_state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 见 ShellExit 文件头铁律 1。
        }
    }

    private static string GradeLabel(int grade, string term)
    {
        string g = grade switch
        {
            6 => "六年级",
            7 => "初一",
            8 => "初二",
            9 => "初三",
            _ => grade.ToString(System.Globalization.CultureInfo.InvariantCulture) + "年级",
        };

        return g + term;
    }
}
