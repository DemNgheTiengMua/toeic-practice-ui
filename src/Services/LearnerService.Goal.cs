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

        string? currentBandCode;
        int? currentScore;
        string? currentSource;

        if (latestMock is not null && (latestPlacement is null || latestMock.TakenAt >= latestPlacement.TakenAt))
        {
            currentBandCode = latestMock.BandCode;
            currentScore = latestMock.Score;
            currentSource = "mock";
        }
        else if (latestPlacement is not null)
        {
            currentBandCode = latestPlacement.BandCode;
            currentScore = latestPlacement.Score;
            currentSource = "placement";
        }
        else
        {
            currentBandCode = null;
            currentScore = null;
            currentSource = null;
        }

        var target = await db.TargetBands
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.CertId == certId)
            .Select(t => new
            {
                t.TargetBandCode,
                BandName = t.ScoreBand.Name,
                MinScore = (int?)t.ScoreBand.MinTotal,
                t.TargetDate,
                t.WeeklyStudyHours,
                t.FeasibilityStatus,
            })
            .FirstOrDefaultAsync(cancellationToken);

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
            WeekCompletedMinutes: thisWeek.Where(s => s.IsDone).Sum(s => s.Minutes));
    }
}
