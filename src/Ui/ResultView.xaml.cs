using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StudyGate.Core;

namespace StudyGate.Ui;

/// <summary>
/// 结果视图。达标 → 64px 沙金主状态 + 三科分布，静默 3 秒后回调放行（不显示倒计时数字）。
/// 未达标 → 64px 主文色主状态 + 三科分布 + 错题号 + 唯一按钮「重考」。
/// 本类不判分、不改状态，只把 QuizResult 渲染出来并把「重考」意图抛给宿主。
/// </summary>
public partial class ResultView : UserControl
{
    /// <summary>达标后的静默等待时长。SPEC 第三节：3 秒。</summary>
    private static readonly TimeSpan PassDelay = TimeSpan.FromSeconds(3);

    /// <summary>历史明细最多显示几行。再多就只给摘要，避免结果页被记录淹没。</summary>
    private const int HistoryRowMax = 6;

    /// <summary>
    /// 每科题量，与 PaperSpec.PerSubject 的唯一取值一致（30 题 = 三科各 10）。
    /// 不用 AttemptRecord.TotalCount / 3 求分母：那个字段语义是「整卷总数」，
    /// 除不尽时会静默丢余数，卷面规格一变就跟着算错。
    /// </summary>
    private const int PerSubjectTotal = 10;

    private readonly DispatcherTimer _timer;
    private Action? _onPassed;
    private string _gradeLabel = string.Empty;

    public ResultView()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = PassDelay,
        };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>结果页点「重考」。由宿主转发到 Coordinator。</summary>
    public event Action? RetryRequested;

    /// <summary>顶部小签的年级标签（形如「六年级下」）。与答题页同源，由宿主注入。</summary>
    public void SetGradeLabel(string gradeLabel)
    {
        _gradeLabel = gradeLabel ?? string.Empty;
        GradeLabel.Text = "StudyGate · " + _gradeLabel;
    }

    /// <summary>
    /// 注入历史作答记录（最新在前，最多 30 条）。同步、无副作用、不触发任何事件，
    /// 也不碰 3 秒倒计时。里面不排序、不重算，进来什么样就显示什么样。
    /// </summary>
    public void SetHistory(IReadOnlyList<AttemptRecord> history)
    {
        if (history is null || history.Count == 0)
        {
            HistorySummary.Text = "还没有记录";
            HistoryList.Children.Clear();
            return;
        }

        // 摘要：「共 N 次 · 上次 S 分 · 达标/未达标」。最新一条在索引 0（宿主已排好）。
        AttemptRecord latest = history[0];
        HistorySummary.Text = string.Format(
            CultureInfo.InvariantCulture,
            "共 {0} 次 · 上次 {1} 分 · {2}",
            history.Count,
            latest.Score,
            latest.Passed ? "达标" : "未达标");

        HistoryList.Children.Clear();

        int shown = Math.Min(history.Count, HistoryRowMax);
        for (int i = 0; i < shown; i++)
        {
            HistoryList.Children.Add(CreateHistoryRow(history[i]));
        }
    }

    /// <summary>
    /// 渲染结果。passed 时启动 3 秒静默倒计时，到点调用 onPassedCountdownElapsed 一次。
    /// 未达标时不启动倒计时、不隐藏「重考」。
    /// </summary>
    public void Show(QuizResult result, Action? onPassedCountdownElapsed)
    {
        ArgumentNullException.ThrowIfNull(result);

        StopCountdown();

        StatusText.Text = result.Passed
            ? string.Format(CultureInfo.InvariantCulture, "已达标 · {0} 分", result.Score)
            : "未达标 · 仍需 90 分";

        // 达标才允许出现强调色；未达标用主文字色（同字号只降颜色，不做渐层）。
        StatusText.Foreground = result.Passed ? AccentBrush() : TextBrush();

        SubjectLine.Text = FormatSubjects(result.BySubject);

        if (result.WrongQuestionIds is { Count: > 0 } wrongIds)
        {
            WrongList.Text = FormatWrongIds(wrongIds);
            WrongBlock.Visibility = Visibility.Visible;
        }
        else
        {
            // 无错题时整块留空，不写「全对」。
            WrongList.Text = string.Empty;
            WrongBlock.Visibility = Visibility.Collapsed;
        }

        // 达标无按钮；未达标唯一按钮「重考」。
        RetakeButton.Visibility = result.Passed ? Visibility.Collapsed : Visibility.Visible;

        if (result.Passed && onPassedCountdownElapsed is not null)
        {
            _onPassed = onPassedCountdownElapsed;
            _timer.Start();
        }
    }

    /// <summary>停掉未走完的 3 秒倒计时（重考、释放、换卷时必须调用，否则会把旧结果带进新卷）。</summary>
    public void StopCountdown()
    {
        _timer.Stop();
        _onPassed = null;
    }

    // ─────────────────────────────────────────────────────────────
    // 文案
    // ─────────────────────────────────────────────────────────────

    /// <summary>语文 {c}/{t} · 数学 {c}/{t} · 英语 {c}/{t}（分隔符逐字照 CONTRACTS.md D 节）。</summary>
    private static string FormatSubjects(IReadOnlyList<SubjectScore>? bySubject)
    {
        if (bySubject is null || bySubject.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder(64);

        for (int i = 0; i < bySubject.Count; i++)
        {
            SubjectScore entry = bySubject[i];
            if (i > 0)
            {
                sb.Append(" · ");
            }

            sb.Append(SubjectName(entry.Subject))
              .Append(' ')
              .Append(entry.Correct.ToString(CultureInfo.InvariantCulture))
              .Append('/')
              .Append(entry.Total.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string SubjectName(Subject subject)
    {
        return subject switch
        {
            Subject.Chinese => "语文",
            Subject.Math => "数学",
            Subject.English => "英语",
            _ => subject.ToString(),
        };
    }

    /// <summary>错题号列表：每行一个，等宽字体对齐；id 过长时按行折行显示。</summary>
    private static string FormatWrongIds(IReadOnlyList<string> wrongIds)
    {
        StringBuilder sb = new StringBuilder(wrongIds.Count * 16);

        for (int i = 0; i < wrongIds.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }

            sb.Append(wrongIds[i] ?? string.Empty);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 单条历史行的视觉。一行放完，空间不够就让等宽字体自动截断，
    /// 不折行 —— 结果页已有错题区在抢高度，把一条记录拆成两行会把版面顶穿。
    /// 达标条目比未达标亮一档，两档都不进主文色 #E8E6E1。
    /// </summary>
    private TextBlock CreateHistoryRow(AttemptRecord record)
    {
        return new TextBlock
        {
            Text = FormatHistoryLine(record),
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = HistoryBrush(record.Passed),
        };
    }

    /// <summary>历史行前景色：达标 #9BA0A8、未达标 #5E636B。优先取 Theme 里的资源，兜底用同色值。</summary>
    private Brush HistoryBrush(bool passed)
    {
        string key = passed ? "Brush.HistoryPassed" : "Brush.HistoryMiss";
        return TryFindResource(key) as Brush
            ?? new SolidColorBrush(
                passed ? Color.FromRgb(0x9B, 0xA0, 0xA8) : Color.FromRgb(0x5E, 0x63, 0x6B));
    }

    /// <summary>「11/07 14:37　六年级下　90 分　10/10 8/10 9/10　00:33」。</summary>
    private static string FormatHistoryLine(AttemptRecord record)
    {
        StringBuilder sb = new StringBuilder(96);

        sb.Append(record.AtUtc.ToLocalTime().ToString("MM/dd HH:mm", CultureInfo.InvariantCulture))
          .Append("  ")
          .Append(GradeName(record.Grade))
          .Append(record.Term)
          .Append("  ")
          .Append(record.Score.ToString(CultureInfo.InvariantCulture))
          .Append(" 分  ");

        if (record.SubjectCorrect is { Count: > 0 } perSubject)
        {
            for (int i = 0; i < perSubject.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(perSubject[i].ToString(CultureInfo.InvariantCulture))
                  .Append('/')
                  .Append(PerSubjectTotal.ToString(CultureInfo.InvariantCulture));
            }

            sb.Append("  ");
        }

        // 负数是历史脏数据（老存档或时钟回拨），显示成 00:00 而不是负号。
        long ms = record.ElapsedMs < 0 ? 0 : record.ElapsedMs;
        long totalSeconds = ms / 1000;

        sb.Append(totalSeconds >= 3600
            ? string.Format(
                CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}",
                totalSeconds / 3600, (totalSeconds / 60) % 60, totalSeconds % 60)
            : string.Format(
                CultureInfo.InvariantCulture, "{0:00}:{1:00}",
                totalSeconds / 60, totalSeconds % 60));

        return sb.ToString();
    }

    /// <summary>年级显示名。6 = 六年级（云南六三制），7/8/9 = 初一/初二/初三。</summary>
    private static string GradeName(int grade)
    {
        return grade switch
        {
            6 => "六年级",
            7 => "初一",
            8 => "初二",
            9 => "初三",
            _ => grade.ToString(CultureInfo.InvariantCulture) + "年级",
        };
    }

    // ─────────────────────────────────────────────────────────────
    // 事件
    // ─────────────────────────────────────────────────────────────

    private void OnRetakeClick(object sender, RoutedEventArgs e)
    {
        // 点击即停表：本次结果不再有资格触发放行。
        StopCountdown();
        RetryRequested?.Invoke();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        Action? callback = _onPassed;
        _onPassed = null;

        callback?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────
    // 画笔（优先取 Window.Resources 合并进来的 Theme，兜底用同一色值）
    // ─────────────────────────────────────────────────────────────

    private Brush AccentBrush()
    {
        return TryFindResource("Brush.Accent") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0xC8, 0xA2, 0x4A));
    }

    private Brush TextBrush()
    {
        return TryFindResource("Brush.Text") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0xE8, 0xE6, 0xE1));
    }
}
