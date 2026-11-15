using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearnerService
{
    /// <summary>#159: Goal Standing - current band, target band and date, this week's planned vs done
    /// minutes. Every figure is the one the goal, the attempt and the path rows already hold; nothing
    /// is recomputed here, so this block cannot disagree with the pages that own those numbers.</summary>
    private async Task<GoalStanding> BuildGoalStandingAsync(
        int userId,
        int certId,
        LearnerStanding? standing,
        CancellationToken cancellationToken)
    {
        // The standing is the newest of the latest graded mock and the latest done placement: a
        // placement after a mock moves it, and a mock after a placement moves it back.
        var latestMock = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId
                && a.CertId == certId
                && a.Status == AttemptStatus.Graded.ToCode()
                && a.Exam.Purpose == ExamPurpose.Mock.ToCode()
                && a.TotalScore != null)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .Select(a => new { Score = a.TotalScore!.Value, a.BandCode, TakenAt = a.SubmittedAt ?? a.StartedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId
                && p.CertId == certId
                && p.Status == PlacementStatus.Done.ToCode()
                && p.TotalScore != null)
            .OrderByDescending(p => p.TakenAt)
            .Select(p => new { Score = p.TotalScore!.Value, BandCode = p.ResultBandCode, p.TakenAt })
            .FirstOrDefaultAsync(cancellationToken);

        var target = await db.TargetBands
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.CertId == certId)
            .Select(t => new
            {
                t.TargetBandCode,
                t.CurrentBandCode,
                BandName = t.ScoreBand.Name,
                MinScore = (int?)t.ScoreBand.MinTotal,
                t.TargetDate,
                t.WeeklyStudyHours,
                t.FeasibilityStatus,
                t.SetAt,
                // #241: D21a's "remaining guided hours" is the stored ΔH, not a fresh calculation.
                // #234's no-proration rule means a certificate whose bands carry no study hours has
                // no ΔH at all, so this stays NULL rather than being estimated.
                t.RequiredTotalHours,
            })
            .FirstOrDefaultAsync(cancellationToken);

        // #380: one certificate, one level, one source. The goal row's starting band is the band the
        // placement-result mapping handed THIS certificate (PlacementService.SetGoalAsync ->
        // LatestAssessedBandCodeAsync, #311), so it is what "level against target" must print - a mock
        // band here made the card disagree with the placement page it is judged against. A measurement
        // taken AFTER the goal was set still wins, so a later mock moves the level as it always did.
        string? measuredBandCode;
        int? measuredScore;
        string? measuredSource;
        DateTimeOffset measuredAt;

        if (latestMock is not null && (latestPlacement is null || latestMock.TakenAt >= latestPlacement.TakenAt))
        {
            measuredBandCode = latestMock.BandCode;
            measuredScore = latestMock.Score;
            measuredSource = "mock";
            measuredAt = latestMock.TakenAt;
        }
        else if (latestPlacement is not null)
        {
            measuredBandCode = latestPlacement.BandCode;
            measuredScore = latestPlacement.Score;
            measuredSource = "placement";
            measuredAt = latestPlacement.TakenAt;
        }
        else
        {
            measuredBandCode = null;
            measuredScore = null;
            measuredSource = null;
            measuredAt = default;
        }

        var goalBandCode = target?.CurrentBandCode;
        var useGoalBand = goalBandCode is not null
            && (measuredBandCode is null || measuredAt <= target!.SetAt);

        var currentBandCode = useGoalBand ? goalBandCode : measuredBandCode;
        var currentScore = useGoalBand ? null : measuredScore;
        var currentSource = currentBandCode is null ? null : useGoalBand ? "goal" : measuredSource;

        // Band names live in one table for the whole certificate, so the current band's name is read
        // there rather than from whichever attempt happened to carry it.
        var currentBandName = currentBandCode is null
            ? null
            : await db.ScoreBands
                .AsNoTracking()
                .Where(b => b.CertId == certId && b.Code == currentBandCode)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(cancellationToken);

        var activePathId = await db.LearnerPaths
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == LearnerPathStatus.Active.ToCode())
            .Select(p => (int?)p.LearnerPathId)
            .FirstOrDefaultAsync(cancellationToken);

        var pathSteps = activePathId is null
            ? []
            : await db.LearnerPathSteps
                .AsNoTracking()
                .Where(s => s.LearnerPathId == activePathId)
                .Select(s => new
                {
                    s.WeekNumber,
                    Minutes = s.EstimatedMinutes ?? 0,
                    IsDone = s.Status == LearnerPathStepStatus.Done.ToCode(),
                })
                .ToListAsync(cancellationToken);

        // #154: "this week" is the earliest week the plan has not finished. A path built before #154
        // carries no week numbers, so it reports no week rather than inventing one.
        var unfinishedWeeks = pathSteps
            .Where(s => s.WeekNumber is not null && !s.IsDone)
            .Select(s => s.WeekNumber!.Value)
            .ToList();
        int? currentWeek = unfinishedWeeks.Count > 0 ? unfinishedWeeks.Min() : null;

        var thisWeek = currentWeek is null ? [] : pathSteps.Where(s => s.WeekNumber == currentWeek).ToList();

        return new GoalStanding(
            CurrentBandCode: currentBandCode,
            CurrentBandName: currentBandName,
            CurrentScore: currentScore,
            CurrentSource: currentSource,
            TargetBandCode: target?.TargetBandCode,
            TargetBandName: target?.BandName,
            TargetMinScore: target?.MinScore,
            TargetDate: target?.TargetDate,
            WeeklyStudyHours: target?.WeeklyStudyHours,
            Status: target?.FeasibilityStatus,
            CompletedPathSteps: pathSteps.Count(s => s.IsDone),
            TotalPathSteps: pathSteps.Count,
            CurrentWeekNumber: currentWeek,
            WeekPlannedMinutes: thisWeek.Sum(s => s.Minutes),
            WeekCompletedMinutes: thisWeek.Where(s => s.IsDone).Sum(s => s.Minutes),
            ScoreDisplayFactor: await ScoreDisplayFactorAsync(certId, cancellationToken),
            RequiredTotalHours: target?.RequiredTotalHours);
    }
}
