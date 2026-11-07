using System;
using System.Collections.Generic;
using StudyGate.Core;

namespace StudyGate.Ui;

/// <summary>
/// App 层（Coordinator）与 Ui 层之间唯一契约。
/// Ui 只负责呈现与收集意图；判分、抽题、落盘、放行全部在 App 层。
/// 签名见 docs/CONTRACTS.md B 节，不可改名、不可改参数。
/// </summary>
public interface IGateViewHost
{
    /// <summary>装载一份新卷。gradeLabel 形如 "六年级下"。调用后视图应显示第一题。</summary>
    void LoadPaper(PaperSpec spec, IReadOnlyList<Question> paper, string gradeLabel);

    /// <summary>切到指定题。answeredCount 是已答题数（含当前已选），用于底部 "已答 N"。</summary>
    void ShowQuestion(int index, int answeredCount, int selectedIndex);

    /// <summary>显示结果页。视图内部决定达标/未达标的呈现，3 秒倒计时达标时触发 PassedCountdownElapsed。</summary>
    void ShowResult(QuizResult result);

    /// <summary>
    /// 注入历史作答记录（最新在前，最多 30 条）。宿主在装卷时调一次，
    /// 视图自行决定显示几条、怎么排。只读，视图不得修改。
    /// </summary>
    void SetHistory(IReadOnlyList<AttemptRecord> history);

    /// <summary>宿主强制切走（解锁后）。视图不再拦截 Closing。</summary>
    void ReleaseAndClose();

    /// <summary>用户选中某个选项。参数为 0..3 的选项索引。</summary>
    event Action<int>? OptionSelected;

    /// <summary>用户答完最后一题或主动交卷。宿主收到后判分。</summary>
    event Action? SubmitRequested;

    /// <summary>结果页点"重考"。</summary>
    event Action? RetakeRequested;

    /// <summary>达标后的 3 秒静默倒计时结束。宿主据此调 ShellExit。</summary>
    event Action? PassedCountdownElapsed;
}
