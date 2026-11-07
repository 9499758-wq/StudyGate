using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using StudyGate.Core;

namespace StudyGate.Ui;

/// <summary>
/// 锁屏宿主窗口。无参构造，InitializeComponent() 之后即可被 Coordinator 注入数据。
///
/// 本类只做三件事：承载 QuizView / ResultView 两个子视图、把子视图的意图转发给宿主、
/// 在 UI 层再做第二道取消拦截（第一道是 App 层的全局钩子）。
/// 不读盘、不碰 GateStore、不订阅键盘钩子、不做口令匹配。
/// </summary>
public partial class GateWindow : Window, IGateViewHost
{
    private bool _released;
    private bool _showingResult;
    private bool _passedRaised;

    public GateWindow()
    {
        InitializeComponent();

        // 子视图 → 宿主的意图转发。子视图只知道自己被点了，不知道怎么判分。
        // PreviewKeyDown / Closing 已在 XAML 上绑定（同名同签名），这里不再重复订阅，
        // 否则处理器会被调用两次（Closing 双次 e.Cancel 无害但脏，键事件双次判断纯冗余）。
        Quiz.OptionSelected += OnQuizOptionSelected;
        Result.RetryRequested += OnResultRetryRequested;
    }

    // ─────────────────────────────────────────────────────────────
    // IGateViewHost
    // ─────────────────────────────────────────────────────────────

    public event Action<int>? OptionSelected;
    public event Action? SubmitRequested;
    public event Action? RetakeRequested;
    public event Action? PassedCountdownElapsed;

    public void LoadPaper(PaperSpec spec, IReadOnlyList<Question> paper, string gradeLabel)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(gradeLabel);

        if (paper.Count != spec.TotalQuestions)
        {
            throw new ArgumentException(
                $"paper.Count={paper.Count} 与 spec.TotalQuestions={spec.TotalQuestions} 不一致。",
                nameof(paper));
        }

        Quiz.Load(spec, paper, gradeLabel);

        // 结果页顶部小签与答题页同源，跟着同一份 gradeLabel 走。
        Result.SetGradeLabel(gradeLabel);

        // 重考时从结果页切回答题页。
        _showingResult = false;
        Result.StopCountdown();
        Result.Visibility = Visibility.Collapsed;
        Quiz.Visibility = Visibility.Visible;
        VersionText.Visibility = Visibility.Visible;

        // 结果页放行过的事件不该残留到新卷：每卷只允许发一次。
        _passedRaised = false;
    }

    public void ShowQuestion(int index, int answeredCount, int selectedIndex)
    {
        Quiz.Render(index, answeredCount, selectedIndex);
    }

    public void ShowResult(QuizResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Quiz.Visibility = Visibility.Collapsed;
        Result.Visibility = Visibility.Visible;
        VersionText.Visibility = Visibility.Visible;
        _showingResult = true;

        // 达标：静默 3 秒后触发放行。未达标：显示分布与错题，等"重考"。
        Result.Show(result, OnPassedCountdownElapsed);
    }

    public void ReleaseAndClose()
    {
        _released = true;
        Result.StopCountdown();

        Close();
    }

    // ─────────────────────────────────────────────────────────────
    // 子视图转发
    // ─────────────────────────────────────────────────────────────

    private void OnQuizOptionSelected(int optionIndex)
    {
        if (optionIndex < 0 || optionIndex > 3)
        {
            return;
        }

        // 先让视图自己表现选中态，再通知宿主；视图不等待宿主回执。
        Quiz.MarkSelected(optionIndex);
        OptionSelected?.Invoke(optionIndex);
    }

    private void OnResultRetryRequested()
    {
        RetakeRequested?.Invoke();
    }

    private void OnPassedCountdownElapsed()
    {
        if (_passedRaised || !_showingResult)
        {
            return;
        }

        _passedRaised = true;
        PassedCountdownElapsed?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────
    // 取消拦截（第二道；第一道在 App 的 InputLock 钩子）
    // ─────────────────────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc、Alt+F4、Alt+Tab、Tab 一律在 UI 层吞掉。
        // Alt+Tab 由系统处理，WPF 拿不到，但这里仍拦截一次降低成本；
        // Alt+F4 实际走 Closing，见 OnClosing。
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Tab:
                e.Handled = true;
                break;

            case Key.System:
                if (e.SystemKey == Key.F4 || e.SystemKey == Key.Tab)
                {
                    e.Handled = true;
                }
                break;
        }

        if (e.Key == Key.F12 || (e.Key == Key.System && e.SystemKey == Key.F12))
        {
            e.Handled = true;
        }
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // 未放行前窗口不可关：没有关闭按钮，也没有任何键盘组合能绕过。
        if (!_released)
        {
            e.Cancel = true;
        }
    }
}
