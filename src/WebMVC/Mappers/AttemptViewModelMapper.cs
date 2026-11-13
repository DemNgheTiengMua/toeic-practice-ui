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

    public static PlacementResultViewModel PlacementResult(PlacementResultViewDto result, DisplayDates dates,
        string? error, AiInsightDto? insight, ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        var isTwoSkill = result.Scope == PlacementScopeDto.ListeningAndReading;
        var bands = result.CefrBands
            .OrderBy(b => b.MinTotal)
            .Select(b => new PlacementBandColumnViewModel(b.Code, b.Name, b.MinTotal, b.MaxTotal))
            .ToList();
        var rows = PlacementHorizon.Build(result, bands, dates, log);

        return new(result, error, insight)
        {
            TakenAtDisplay = dates.FormatCustom(result.TakenAt, "d MMMM yyyy"),
            IsAwaitingGrading = !result.IsGraded,
            // #152: same rule as the mock result â€” stored tenths are divided at the edge.
            TotalScoreDisplay = result.IsGraded ? ScoreDisplay.Format(result.TotalScore, result.ScoreDisplayFactor) : "â€”",
            ScoreSummary = result.IsGraded
                ? $"{ScoreDisplay.Format(result.TotalScore, result.ScoreDisplayFactor)} points Â· {result.ResultBandName} level"
                : "Composite score pending AI grading",
            MaximumScore = result.CefrBands.Count > 0
                ? ScoreDisplay.Format(result.CefrBands.Max(b => b.MaxTotal), result.ScoreDisplayFactor)
                // #161: no bands means no declared ceiling. The page says so; inventing 990 made every
                // certificate without band data look like a TOEIC paper scored out of 990.
                : "",
            ScopeNotice = isTwoSkill
                ? "This sitting covered 2 skills (Listening and Reading). Speaking and Writing were not attempted; any previous measurements for those skills survive untouched."
                : null,
            Skills = result.SkillScores,
            SkillResults = result.SkillResults ?? [],
            Bands = bands,
            Rows = rows,
            MeasuredRows = rows.Where(r => r.IsMeasured).ToList(),
            Floor = Floor(rows, bands, rows.Any(r => r.State == "grading")),
            Dates = dates,
            RetryUrl = $"/placement/result/{result.PlacementId}/retry"
        };
    }

    /// <summary>
    /// #218 / LRN-17: the overall is the weakest MEASURED skill, and the placard says so in words â€”
    /// naming the skill that holds it down and the one that reached highest. An unmeasured skill is
    /// excluded and named as excluded, never treated as a zero (that zero was the #217 defect).
    /// </summary>
    private static PlacementFloorViewModel Floor(IReadOnlyList<PlacementSkillRowViewModel> rows,
        IReadOnlyList<PlacementBandColumnViewModel> bands, bool anyGrading)
    {
        var measured = rows.Where(r => r.IsMeasured).ToList();
        if (measured.Count == 0) return new PlacementFloorViewModel();

        // Ranked by the certificate's own band order, then by position inside the band. A measured row
        // whose level matches no band column still ranks (index null sorts last) rather than vanishing.
        var ordered = measured
            .OrderBy(r => r.BandIndex ?? int.MaxValue)
            .ThenBy(r => r.PositionInBand ?? 0)
            .ToList();
        var floor = ordered[0];
        var ceiling = ordered[^1];
        var band = floor.BandIndex is int at && at < bands.Count ? bands[at].Label : floor.Coordinate;

        var excluded = rows.Where(r => r.State == "not-measured").Select(r => r.SkillName).ToList();
        // "Uneven" is a claim about the numbers, so it is only made when they actually differ. A tie
        // printed as "uneven" is the kind of sentence a learner stops trusting the page over.
        var differs = measured.Count > 1 && ceiling.Coordinate != floor.Coordinate;

        return new PlacementFloorViewModel
        {
            HasFloor = true,
            Coordinate = floor.Coordinate,
            BandName = band,
            IsProvisional = anyGrading,
            // The tether names the limiting skill whenever the overall is a choice between skills.
            LimitingSkillText = measured.Count > 1 ? $"{floor.SkillName} holds this floor." : string.Empty,
            ReasonText = measured.Count == 1
                ? $"Only {floor.SkillName} was measured in this sitting, so the overall is that one measurement."
                : differs
                    ? $"Your skills are uneven: {ceiling.SkillName} reached {ceiling.Coordinate}, while "
                      + $"{floor.SkillName} is at {floor.Coordinate}. The overall is anchored to the lowest "
                      + "measured skill, so this floor is what your path works on first."
                    : $"Every measured skill is at {floor.Coordinate}, so the overall is that level rather "
                      + "than an average that could read higher than any skill actually reached.",
            ExcludedText = excluded.Count == 0
                ? string.Empty
                : $"{string.Join(" and ", excluded)} {(excluded.Count == 1 ? "was" : "were")} not measured, so "
                  + $"{(excluded.Count == 1 ? "it is" : "they are")} excluded from this floor â€” not counted as zero.",
        };
    }

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
        // #161: a band the certificate named is printed as it named it. The "<A1" rewrite stays for the
        // one code the seed uses to mean "below the lowest band", because that code describes a gap in
        // the scale rather than a level; every other code is data and is shown verbatim.
        band.Code == "below_A1" ? "<A1" : band.Code, band.MinTotal,
        "cefr__band" + (showState && band.IsCurrent ? " is-current" : showState && band.IsReached ? " is-reached" : ""));

    private static int Percent(int raw, int count) => count > 0 ? (int)Math.Round((double)raw / count * 100) : 0;
    private static string SkillScores(IReadOnlyList<AttemptSkillScoreDto> scores) => string.Join(" Â· ", scores.Select(s => $"{s.SkillCode} {s.ScaledScore}"));
}
