using Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Models;
using DisplayDates = WebMVC.Formatters.DateTimeFormatter;

namespace WebMVC.Mappers;

internal static class AttemptViewModelMapper
{
    public static ExamResultViewModel Result(AttemptResultDto result, DisplayDates dates, string? error, AiInsightDto? insight, MockResultProjectionDto? projection = null, ILogger? logger = null)
    {
        var raw = result.SkillScores.Sum(s => s.RawScore);
        var count = result.SkillScores.Sum(s => s.TotalQuestions);
        var hours = result.DurationSeconds / 3600;
        var minutes = result.DurationSeconds % 3600 / 60;
        var seconds = result.DurationSeconds % 60;
        return new(result, error, insight)
        {
            SubmittedAtDisplay = dates.Format(result.SubmittedAt),
            // #152: the stored total is in the certificate's own units â€” tenths on IELTS, so 65 is
            // 6.5. Dividing at the edge is what stops the learner reading "65 out of 9".
            TotalScoreDisplay = result.Status == AttemptStatusDto.Graded
                ? ScoreDisplay.Format(result.TotalScore, result.ScoreDisplayFactor)
                : "â€”",
            // #222 / PRN-03: this label says "certificate score" and never "level". The projection is a
            // separate placard with its own label, because TOEIC 785-940 is not CEFR B2.
            TotalScoreLabel = result.Status == AttemptStatusDto.Graded
                ? $"Certificate score Â· {result.CertCode}"
                : "Composite score pending AI grading",
            ScoreDeltaDisplay = result.ScoreDelta is int delta
                ? (delta >= 0 ? "+" : "") + ScoreDisplay.Format(delta, result.ScoreDisplayFactor)
                : "",
            DurationDisplay = hours > 0 ? $"{hours}h {minutes}m {seconds}s" : $"{minutes}m {seconds}s",
            IsGraded = result.Status == AttemptStatusDto.Graded,
            IsAwaitingGrading = result.Status == AttemptStatusDto.Submitted,
            PendingFreeResponses = Math.Max(0, result.FreeResponseCount - result.GradedFreeResponseCount - result.FailedFreeResponseCount),
            TotalRawScore = raw,
            TotalQuestions = count,
            AccuracyPercent = Percent(raw, count),
            Skills = result.SkillScores.Select(s => new ExamSkillScoreViewModel(s, Percent(s.RawScore, s.TotalQuestions), result.ScoreDisplayFactor)).ToList(),
            Projection = MockProjectionViewModelMapper.Map(projection, result.SkillScores, result.ScoreDisplayFactor, logger ?? NullLogger.Instance)
        };
    }

    public static ExamHistoryViewModel History(ExamHistoryDto history, DisplayDates dates, string? error)
    {
        var ceiling = history.CefrBands.Count > 0 ? Math.Max(1, history.CefrBands.Max(b => b.MaxTotal)) : Math.Max(1, history.BestScore ?? 100);
        return new(history, error)
        {
            Attempts = history.Attempts.Select(a => new ExamHistoryAttemptViewModel(a, dates.Format(a.SubmittedAt), SkillScores(a.SkillScores))).ToList(),
            ChartPoints = history.Attempts.OrderBy(a => a.SubmittedAt).TakeLast(10)
                .Select(a => new ExamHistoryChartPointViewModel(a.ExamName, a.TotalScore,
                    Math.Max(20, (int)Math.Round((double)a.TotalScore / ceiling * 140)), a.TotalScore == history.BestScore)).ToList()
        };
    }

    public static ExamReviewViewModel Review(AttemptReviewDto review, DisplayDates dates, string? error) => new(review, error)
    {
        SubmittedAtDisplay = dates.Format(review.SubmittedAt),
        SkillScoresDisplay = SkillScores(review.SkillScores),
        Sections = review.Questions.GroupBy(q => new { q.SectionCode, q.SectionName })
            .Select(g => new ExamReviewSectionViewModel(g.Key.SectionCode, g.Key.SectionName, g.Count())).ToList(),
        Questions = review.Questions.Select(q => new ExamReviewQuestionViewModel(q,
            q.IsCorrect == true ? "correct" : q.IsCorrect == false ? "incorrect" : "blank",
            string.IsNullOrEmpty(q.AudioPath) ? null : new ExamAudioPlayerModel(q.AudioPath, AllowReplay: true,
                SectionTitle: "Review Question Audio", BadgeText: "READY", StartMs: q.AudioStartMs, EndMs: q.AudioEndMs, Mode: "study"),
            q.Options.Select(o => new ExamReviewOptionViewModel(o, o.OptionId == q.SelectedOptionId,
                o.IsCorrect ? "option--correct" : o.OptionId == q.SelectedOptionId ? "option--wrong" : "")).ToList())).ToList(),
        Navigator = review.Navigator.Select(n => new ExamReviewNavigatorViewModel(n,
            (n.IsAnswered ? "is-answered" : "is-unanswered") + (n.IsFlagged ? " is-marked" : ""),
            $"Question {n.DisplayOrder}" + (n.IsAnswered ? " Â· Answered" : " Â· Unanswered") + (n.IsFlagged ? " Â· Flagged" : "")
                + (n.IsCorrect == true ? " Â· Correct" : n.IsCorrect == false ? " Â· Incorrect" : ""))).ToList()
    };

    private static int Percent(int raw, int count) => count > 0 ? (int)Math.Round((double)raw / count * 100) : 0;
    private static string SkillScores(IReadOnlyList<AttemptSkillScoreDto> scores) => string.Join(" Â· ", scores.Select(s => $"{s.SkillCode} {s.ScaledScore}"));
}
