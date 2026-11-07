namespace StudyGate.Core;

// 值固定为 0/1/2：落盘顺序、UI 三科分布顺序都依赖它，不得重排。
public enum Subject
{
    Chinese = 0,
    Math = 1,
    English = 2,
}

public sealed record Question(
    string Id,
    Subject Subject,
    int Grade,
    string Term,
    string Stem,
    IReadOnlyList<string> Options,
    int AnswerIndex,
    string Explanation);

public sealed record PaperSpec(
    int Grade,
    string Term,
    int TotalQuestions,
    int PassScore,
    int PerSubject);

public sealed record AnswerRecord(
    string QuestionId,
    int SelectedIndex,
    bool Correct,
    long ElapsedMs);

public sealed record SubjectScore(
    Subject Subject,
    int Correct,
    int Total);

public sealed record QuizResult(
    int Score,
    int PassScore,
    bool Passed,
    IReadOnlyList<SubjectScore> BySubject,
    IReadOnlyList<string> WrongQuestionIds,
    long TotalElapsedMs);
