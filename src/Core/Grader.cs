namespace StudyGate.Core;

public sealed class Grader
{
    // 判分口径（严格解释，见交付说明）：
    // 1. 以题库里的 Question.AnswerIndex 为准，不信任 AnswerRecord.Correct。
    // 2. SelectedIndex < 0、记录缺失、答案索引越界、题库 AnswerIndex 自身越界 → 一律算错，绝不抛。
    // 3. answers 与 paper 按下标对齐（与 tools/core-reference.mjs 一致）；多出的记录忽略。
    // 4. Score 为向下取整的百分制整数：179/200 = 89.5% 记 89 分，不通过。
    // 5. TotalElapsedMs 累加本卷命中记录里 >= 0 的耗时。
    public static QuizResult Score(
        IReadOnlyList<Question> paper,
        IReadOnlyList<AnswerRecord> answers,
        int passScore)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(answers);

        var perSubject = new Dictionary<Subject, (int Correct, int Total)>();
        var wrongIds = new List<string>();
        var correctCount = 0;
        var totalCount = 0;
        long elapsed = 0;

        for (var index = 0; index < paper.Count; index++)
        {
            var question = paper[index];
            if (question is null)
            {
                continue;
            }

            totalCount++;
            var questionId = string.IsNullOrEmpty(question.Id) ? string.Empty : question.Id;
            var optionCount = question.Options is null ? 0 : question.Options.Count;
            var bankAnswerKey = question.AnswerIndex >= 0 && question.AnswerIndex < optionCount;

            var answer = index < answers.Count ? answers[index] : null;
            var isCorrect = false;

            if (answer is not null)
            {
                if (answer.ElapsedMs >= 0)
                {
                    elapsed += answer.ElapsedMs;
                }

                isCorrect = bankAnswerKey
                    && answer.SelectedIndex >= 0
                    && answer.SelectedIndex < optionCount
                    && answer.SelectedIndex == question.AnswerIndex;
            }

            if (isCorrect)
            {
                correctCount++;
            }
            else
            {
                wrongIds.Add(questionId);
            }

            if (!perSubject.TryGetValue(question.Subject, out var aggregate))
            {
                aggregate = (0, 0);
            }

            perSubject[question.Subject] = (
                aggregate.Correct + (isCorrect ? 1 : 0),
                aggregate.Total + 1);
        }

        // 向下取整：89.5% 记 89 分，不通过。SPEC「答不到 90 分不给桌面」按最严格解释。
        var score = totalCount == 0
            ? 0
            : (int)((long)correctCount * 100L / totalCount);

        var passed = totalCount > 0 && score >= passScore;

        var bySubject = perSubject
            .OrderBy(entry => (int)entry.Key)
            .Select(entry => new SubjectScore(entry.Key, entry.Value.Correct, entry.Value.Total))
            .ToArray();

        return new QuizResult(
            score,
            passScore,
            passed,
            bySubject,
            wrongIds.ToArray(),
            elapsed);
    }
}
