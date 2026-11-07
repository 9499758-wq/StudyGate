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
