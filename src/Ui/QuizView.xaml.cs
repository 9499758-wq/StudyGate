using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using StudyGate.Core;

namespace StudyGate.Ui;

/// <summary>
/// 答题主体视图。只负责渲染题干与选项、把点击翻译成选项索引。
/// 判分、推进、落盘全部由宿主（GateWindow → Coordinator）决定；本类不持有 GateState。
/// </summary>
public partial class QuizView : UserControl
{
    /// <summary>四个选项卡片，下标即选项索引 0..3。</summary>
    private readonly List<RadioButton> _cards = new();

    /// <summary>四个选项正文，与 _cards 下标对齐。</summary>
    private readonly List<TextBlock> _texts = new();

    private IReadOnlyList<Question> _paper = Array.Empty<Question>();
    private string _gradeLabel = string.Empty;
    private int _currentIndex;
    private int _total = 20;
    private bool _suppressClick;

    public QuizView()
    {
        InitializeComponent();

        _cards.Add(OptionA);
        _cards.Add(OptionB);
        _cards.Add(OptionC);
        _cards.Add(OptionD);

        _texts.Add(OptionAText);
        _texts.Add(OptionBText);
        _texts.Add(OptionCText);
        _texts.Add(OptionDText);
    }

    /// <summary>用户选中某个选项（0..3）。由宿主转发到 Coordinator。</summary>
    public event Action<int>? OptionSelected;

    /// <summary>装载新卷：只保存题目与年级标签，不渲染（首题的渲染由 ShowQuestion 驱动）。</summary>
    public void Load(PaperSpec spec, IReadOnlyList<Question> paper, string gradeLabel)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(paper);

        _paper = paper;
        _gradeLabel = gradeLabel ?? string.Empty;
        _total = spec.TotalQuestions > 0 ? spec.TotalQuestions : paper.Count;
        _currentIndex = 0;

        GradeLabel.Text = "StudyGate · " + _gradeLabel;
    }

    /// <summary>
    /// 渲染指定题。selectedIndex = -1 表示该题尚未作答（四张卡都不选中）。
    /// answeredCount 是已答题数（含当前题若已选），只用于底部计数。
    /// </summary>
    public void Render(int index, int answeredCount, int selectedIndex)
    {
        if (_paper.Count == 0)
        {
            return;
        }

        _currentIndex = Math.Clamp(index, 0, _paper.Count - 1);
        Question question = _paper[_currentIndex];

        StemText.Text = question.Stem ?? string.Empty;

        _suppressClick = true;

        for (int i = 0; i < _cards.Count; i++)
        {
            string optionText = question.Options is not null && i < question.Options.Count
                ? question.Options[i] ?? string.Empty
                : string.Empty;

            _texts[i].Text = optionText;
            _cards[i].IsEnabled = i < (question.Options?.Count ?? 0);
            _cards[i].IsChecked = i == selectedIndex;
        }

        _suppressClick = false;

        // 07 / 30；已答 07。宽度用 2 位补零，位数变化时右侧不抖。
        IndexLabel.Text = string.Format(
            CultureInfo.InvariantCulture,
            "{0:D2} / {1:D2}",
            _currentIndex + 1,
            _total);

        AnsweredLabel.Text = string.Format(
            CultureInfo.InvariantCulture,
            "已答 {0:D2}",
            Math.Clamp(answeredCount, 0, _total));
    }

    /// <summary>宿主确认后由窗口回调，把选中态落到视觉上（避免点击后回弹）。</summary>
    public void MarkSelected(int optionIndex)
    {
        if (optionIndex < 0 || optionIndex >= _cards.Count)
        {
            return;
        }

        _suppressClick = true;
        _cards[optionIndex].IsChecked = true;
        _suppressClick = false;
    }

    private void OnOptionClick(object sender, RoutedEventArgs e)
    {
        if (_suppressClick)
        {
            return;
        }

        if (sender is not RadioButton card)
        {
            return;
        }

        if (card.Tag is not string tagText ||
            !int.TryParse(tagText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int optionIndex))
        {
            return;
        }

        OptionSelected?.Invoke(optionIndex);
    }
}
