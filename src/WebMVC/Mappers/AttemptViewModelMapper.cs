using Contracts;
using WebMVC.Models;
using DisplayDates = WebMVC.Formatters.DateTimeFormatter;

namespace WebMVC.Mappers;

internal static class AttemptViewModelMapper
{
    public static ExamResultViewModel Result(AttemptResultDto result, DisplayDates dates, string? error, AiInsightDto? insight)
    {
        var raw = result.SkillScores.Sum(s => s.RawScore);
        var count = result.SkillScores.Sum(s => s.TotalQuestions);
        var hours = result.DurationSeconds / 3600;
        var minutes = result.DurationSeconds % 3600 / 60;
        var seconds = result.DurationSeconds % 60;
        return new(result, error, insight)
        {
            SubmittedAtDisplay = dates.Format(result.SubmittedAt),
            TotalScoreDisplay = result.Status == AttemptStatusDto.Graded ? result.TotalScore.ToString() : "—",
            TotalScoreLabel = result.Status == AttemptStatusDto.Graded ? $"Total scaled score · {result.CertCode}" : "Composite score pending AI grading",
            ScoreDeltaDisplay = result.ScoreDelta >= 0 ? $"+{result.ScoreDelta}" : result.ScoreDelta?.ToString() ?? "",
            DurationDisplay = hours > 0 ? $"{hours}h {minutes}m {seconds}s" : $"{minutes}m {seconds}s",
            IsGraded = result.Status == AttemptStatusDto.Graded,
            IsAwaitingGrading = result.Status == AttemptStatusDto.Submitted,
            PendingFreeResponses = Math.Max(0, result.FreeResponseCount - result.GradedFreeResponseCount - result.FailedFreeResponseCount),
            TotalRawScore = raw,
            TotalQuestions = count,
            AccuracyPercent = Percent(raw, count),
            Skills = result.SkillScores.Select(s => new ExamSkillScoreViewModel(s, Percent(s.RawScore, s.TotalQuestions))).ToList()
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
                    Math.Max(20, (int)Math.Round((double)a.TotalScore / ceiling * 140)), a.TotalScore == history.BestScore)).ToList(),
            Bands = history.CefrBands.Select(b => new AttemptBandViewModel(b.Code, b.Code, b.MinTotal,
                "cefr__band " + (b.Code == history.BestScoreBandCode ? "is-current" : history.BestScore >= b.MinTotal ? "is-reached" : ""))).ToList()
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
            $"Question {n.DisplayOrder}" + (n.IsAnswered ? " · Answered" : " · Unanswered") + (n.IsFlagged ? " · Flagged" : "")
                + (n.IsCorrect == true ? " · Correct" : n.IsCorrect == false ? " · Incorrect" : ""))).ToList()
    };

    public static PlacementResultViewModel PlacementResult(PlacementResultViewDto result, DisplayDates dates, string? error, AiInsightDto? insight) => new(result, error, insight)
    {
        TakenAtDisplay = dates.FormatCustom(result.TakenAt, "d MMMM yyyy"),
        IsAwaitingGrading = !result.IsGraded,
        TotalScoreDisplay = result.IsGraded ? result.TotalScore.ToString() : "—",
        ScoreSummary = result.IsGraded ? $"{result.TotalScore} points · {result.ResultBandName} level" : "Composite score pending AI grading",
        MaximumScore = result.CefrBands.Count > 0 ? result.CefrBands.Max(b => b.MaxTotal) : 990,
        Listening = result.SkillScores.FirstOrDefault(s => s.SkillCode == "LIST" || s.SkillName.Contains("Listening", StringComparison.OrdinalIgnoreCase)),
        Reading = result.SkillScores.FirstOrDefault(s => s.SkillCode == "READ" || s.SkillName.Contains("Reading", StringComparison.OrdinalIgnoreCase)),
        Bands = result.CefrBands.Select(b => Band(b, result.IsGraded)).ToList()
    };

    public static TargetBandViewModel TargetBand(TargetBandViewDto view, string? error, string? success)
    {
        var model = new TargetBandViewModel(view, error, success);
        var target = view.AvailableBands.FirstOrDefault(b => b.Code == model.TargetCode);
        return model with
        {
            PointsNeeded = target != null && model.CurrentScore > 0 ? Math.Max(0, target.MinTotal - model.CurrentScore) : 0,
            NeededDisplay = target != null ? $"{target.MinTotal} needed" : "",
            Bands = view.CefrBands.Select(b => Band(b, true)).ToList()
        };
    }

    private static AttemptBandViewModel Band(CefrBandInfoDto band, bool showState) => new(band.Code,
        band.Code == "below_A1" ? "<A1" : band.Code, band.MinTotal,
        "cefr__band" + (showState && band.IsCurrent ? " is-current" : showState && band.IsReached ? " is-reached" : ""));

    private static int Percent(int raw, int count) => count > 0 ? (int)Math.Round((double)raw / count * 100) : 0;
    private static string SkillScores(IReadOnlyList<AttemptSkillScoreDto> scores) => string.Join(" · ", scores.Select(s => $"{s.SkillCode} {s.ScaledScore}"));
}
