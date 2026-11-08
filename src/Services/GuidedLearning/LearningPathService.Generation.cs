using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearningPathService
{
    public async Task<LearningPathSummaryView> GeneratePathAsync(int userId, string? targetBandCode = null, CancellationToken cancellationToken = default, bool archiveExisting = false)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var existingActive = await db.LearnerPaths
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);
        if (existingActive != null)
        {
            if (archiveExisting)
            {
                existingActive.Status = "reset";
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new LearningPathException(LearningPathException.PathAlreadyActive, "An active learning path already exists for this certificate.");
            }
        }

        var savedTarget = await db.TargetBands
            .AsNoTracking()
            .FirstOrDefaultAsync(tb => tb.UserId == userId && tb.CertId == activeCertId, cancellationToken);

        var currentBand = await ResolveStartingBandAsync(userId, activeCertId, savedTarget, cancellationToken);

        var toBand = targetBandCode ?? "";
        if (string.IsNullOrEmpty(toBand))
        {
            toBand = savedTarget?.TargetBandCode ?? "";
        }

        if (string.IsNullOrEmpty(toBand))
        {
            throw new LearningPathException(
                LearningPathException.TargetBandMissing,
                "No target band is set for this certificate. Set your goal first ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â a path aimed at an invented band would not be your path.");
        }

        var scoreBands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == activeCertId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        RefuseUnrealisticGoal(savedTarget, currentBand, toBand, scoreBands);

        // #232 / D10: the plan is built from each skill's OWN level, resolved from the certificate's
        // per-skill cut scores. There is no template, no module and no section rank: a step is a
        // coordinate, and the skill's level is the axis it is pitched on.
        var skillLevels = await LoadSkillLevelsAsync(userId, activeCertId, cancellationToken);
        var blocks = await BuildSkillPlanBlocksAsync(
            userId, activeCertId, skillLevels, toBand, cancellationToken);

        if (blocks.Count == 0)
        {
            throw new LearningPathException(
                LearningPathException.NoQuestionsAvailable,
                "This certificate has no skill with a level and drillable stock yet, so a path would have no steps. Author content for at least one skill first.");
        }

        var targetBand = scoreBands.FirstOrDefault(b => b.Code == toBand);
        var targetAccuracy = LearningPathPlanner.TargetAccuracyFor(
            isTopBand: targetBand is not null && targetBand.DisplayOrder >= scoreBands.Max(b => b.DisplayOrder));

        var weeklyHours = savedTarget?.WeeklyStudyHours ?? LearningPathPlanner.DefaultWeeklyHours;

        // #234 / PLC-51: the week's volume is derived — `H_required` from the study hours between the
        // starting and target bands over the runway, then `T_weekly`. The tier the learner's own mastery
        // sets is what a sitting costs, so the count falls out of the minutes.
        var fromHours = scoreBands.FirstOrDefault(b => b.Code == currentBand)?.CumulativeStudyHours;
        var toHours = scoreBands.FirstOrDefault(b => b.Code == toBand)?.CumulativeStudyHours;
        var remainingHours = fromHours is { } from && toHours is { } to && to > from ? to - from : (int?)null;
        var daysRemaining = savedTarget?.TargetDate is { } targetDate
            ? Math.Max(0, targetDate.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber)
            : 0;

        var plan = LearningPathPlanner.Plan(
            new LearningPathPlanner.WeeklyPacing(weeklyHours, remainingHours, daysRemaining), blocks);

        if (plan.Count == 0)
        {
            throw new LearningPathException(
                LearningPathException.NoQuestionsAvailable,
                "This certificate's stock cannot fill any of the four weeks, so a path would have no steps. Add items first.");
        }

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == activeCertId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        var learnerPath = new LearnerPath
        {
            UserId = userId,
            CertId = activeCertId,
            CurrentBandCode = currentBand,
            PlacementId = latestPlacement?.PlacementId,
            Status = "active",
            StartedAt = DateTimeOffset.UtcNow
        };

        try
        {
            db.LearnerPaths.Add(learnerPath);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(learnerPath).State = EntityState.Detached;
            var concurrent = await db.LearnerPaths
                .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);
            if (concurrent is not null)
            {
                throw new LearningPathException(LearningPathException.PathAlreadyActive, "An active learning path already exists for this certificate.");
            }
            throw;
        }

        var blocksById = blocks.ToDictionary(b => b.SkillId);
        var isFirst = true;

        foreach (var planned in plan)
        {
            var block = blocksById[planned.SkillId];

            db.LearnerPathSteps.Add(new LearnerPathStep
            {
                LearnerPathId = learnerPath.LearnerPathId,
                CertId = activeCertId,
                SkillId = planned.SkillId,
                Modality = block.Modality.ToCode(),
                TopicId = planned.TopicId,
                TargetCefr = planned.TargetCefr.Code,
                StepType = planned.StepType.ToCode(),
                SizingTier = planned.SizingTier.ToCode(),
                // A drill names no resource: the selector decides its items at run time, which is what
                // lets it route around a cooling cell instead of blocking the step (D11/PLC-60).
                RefLessonId = planned.StepType is PathStepType.ConceptLesson or PathStepType.ExpandedRemedial
                    ? block.LessonId
                    : null,
                RefExamId = planned.StepType is PathStepType.MockSection ? block.MockExamId : null,
                Status = isFirst ? "available" : "locked",
                Score = null,
                WeekNumber = planned.WeekNumber,
                WeekSequence = planned.WeekSequence,
                PlannedItemCount = planned.PlannedItemCount,
                EstimatedMinutes = planned.EstimatedMinutes,
                TargetAccuracyPct = targetAccuracy,
                ActualAccuracyPct = null,
                StartedAt = null,
                CompletedAt = null
            });
            isFirst = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        return await GetActivePathAsync(userId, cancellationToken);
    }

    /// <summary>
    /// #153/#156: the band the learner starts from. A calibration the learner accepted is the newest
    /// statement of their level and wins over the placement it corrected; otherwise the latest
    /// placement, then the latest graded mock, then the goal's own snapshot. Never null: a certificate
    /// that cannot say where the learner starts is refused rather than planned from an invented band.
    /// </summary>
    private async Task<string> ResolveStartingBandAsync(
        int userId, int certId, TargetBand? savedTarget, CancellationToken cancellationToken)
    {
        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        var startingBand = latestPlacement?.ResultBandCode;
        if (string.IsNullOrEmpty(startingBand))
        {
            var latestMock = await db.ExamAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.CertId == certId && a.Status == AttemptStatus.Graded.ToCode() && a.Exam.Purpose == ExamPurpose.Mock.ToCode() && a.BandCode != null)
                .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            startingBand = latestMock?.BandCode;
        }

        // #156: accepting a lower band and then regenerating must not rebuild at the old band the
        // placement still claims.
        if (savedTarget?.CurrentBandCode is { Length: > 0 } calibrated)
        {
            var correctedAt = await db.CalibrationDecisions
                .AsNoTracking()
                .Where(d => d.UserId == userId && d.CertId == certId)
                .OrderByDescending(d => d.DecidedAt)
                .Select(d => (DateTimeOffset?)d.DecidedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (correctedAt is not null && (latestPlacement is null || correctedAt >= latestPlacement.TakenAt))
            {
                startingBand = calibrated;
            }
        }

        // #153: the band the goal recorded is the fallback ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â never a hardcoded band code that matches
        // no certificate's rows. With neither, the certificate cannot say where the learner starts.
        startingBand ??= savedTarget?.CurrentBandCode;

        if (string.IsNullOrEmpty(startingBand))
        {
            throw new LearningPathException(
                LearningPathException.StartingBandUnknown,
                "This certificate cannot say which band you start from. Take the free placement test first, or set your current band in your goal.");
        }

        return startingBand;
    }

    /// <summary>
    /// #232: one block per examinable skill, weakest first, so the plan can alternate skills and drill
    /// the weakest hardest. A skill with no level, no topic at that level, or no stock supplies no
    /// block at all ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â a step at that skill would have nothing to serve.
    /// </summary>
    private async Task<IReadOnlyList<SkillPlanBlock>> BuildSkillPlanBlocksAsync(
        int userId, int certId, IReadOnlyDictionary<int, CefrLevel> skillLevels, string targetBandCode,
        CancellationToken cancellationToken)
    {
        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SkillId)
            .Select(s => new { s.SkillId, s.Modality })
            .ToListAsync(cancellationToken);

        var hints = await LoadPlacementTopicHintsAsync(userId, certId, cancellationToken);
        // #234: the skew needs each skill's distance to the target, not just its level.
        var distances = await LoadDistancesToTargetAsync(userId, certId, targetBandCode, cancellationToken);
        var blocks = new List<SkillPlanBlock>(skills.Count);

        foreach (var skill in skills)
        {
            if (!skillLevels.TryGetValue(skill.SkillId, out var level))
            {
                continue;
            }

            var topics = await LoadDrillableTopicsAsync(userId, certId, skill.Modality, level, cancellationToken);
            if (topics.Count == 0)
            {
                continue;
            }

            var mastery = await LoadCellMasteryAsync(userId, certId, topics[0], level, cancellationToken);
            // #240: the lesson is looked up at this skill's own level, and its topic comes back with it,
            // so a lesson step's coordinate is the lesson's own cell.
            var lesson = await LoadLessonForLevelAsync(certId, skill.SkillId, level, cancellationToken);

            // #219: the placement's weak topics for THIS skill at THIS level. A hint from a different
            // level is not a hint about this cell, and keeping it would let the plan order week one
            // by evidence from a band the learner is not working at.
            var weakAtLevel = hints.TryGetValue((skill.SkillId, level), out var weak) ? weak : [];

            blocks.Add(new SkillPlanBlock(
                skill.SkillId,
                EnumCodes.Parse<Modality>(skill.Modality),
                level,
                mastery,
                topics,
                lesson?.LessonId,
                await LoadMockExamForSkillAsync(certId, skill.SkillId, cancellationToken),
                weakAtLevel,
                lesson?.TopicId,
                distances.TryGetValue(skill.SkillId, out var distance) ? distance : null));
        }

        // Weakest first: a cell marked Low outranks Developing, which outranks Mastered. Ties keep the
        // certificate's own skill order, so two runs on the same data plan the same path.
        return blocks
            .OrderBy(b => b.Mastery)
            .ToList();
    }

    /// <summary>
    /// #153: an Unrealistic goal blocks path generation at the service layer, not just in the screen ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â
    /// a caller that skips the onboarding form still cannot get a path built on a goal the learner has
    /// no hours for. The verdict is recomputed here rather than read from the stored column, so a goal
    /// whose date has since passed is judged on today's runway. The message carries the two numbers
    /// (hours needed, hours available) because a refusal without them is indistinguishable from a bug.
    /// </summary>
    private static void RefuseUnrealisticGoal(
        TargetBand? goal,
        string startingBandCode,
        string targetBandCode,
        IReadOnlyList<ScoreBand> scoreBands)
    {
        if (goal is null)
        {
            return;
        }

        // AC4: the stored verdict is the contract. A goal an admin or a learner has already been
        // told is Unrealistic must not quietly become buildable because a band's study hours moved.
        if (goal.FeasibilityStatus is { } stored)
        {
            if (!string.Equals(stored, FeasibilityStatus.Unrealistic.ToCode(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Both numbers, recomputed from the stored date and pace, so the refusal is never a
            // claim the learner has to take on trust.
            var weeksAvailable = goal.TargetDate is { } date
                ? Math.Max(1, (int)Math.Ceiling((date.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber) / 7.0))
                : 0;
            var hoursAvailable = weeksAvailable * (goal.WeeklyStudyHours ?? 0m);
            var hoursNeeded = goal.RequiredTotalHours
                ?? scoreBands.FirstOrDefault(b => b.Code == targetBandCode)?.CumulativeStudyHours
                - scoreBands.FirstOrDefault(b => b.Code == startingBandCode)?.CumulativeStudyHours
                ?? 0;

            throw new LearningPathException(
                LearningPathException.GoalUnrealistic,
                $"This goal is marked unrealistic: it needs {hoursNeeded} hours of study, and you have " +
                $"{hoursAvailable:0.#} hours ({weeksAvailable} weeks at {goal.WeeklyStudyHours:0.#} h/week). " +
                "Move the exam date back, lower the target band, or raise your weekly pace ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â then your path can be built.");
        }

        // No stored verdict (an older row, or bands re-bandered since): judge it now rather than
        // assuming it is fine.
        if (goal.TargetDate is not { } targetDate || goal.WeeklyStudyHours is not { } weeklyHours)
        {
            return;   // no deadline: nothing to refuse
        }

        var fromHours = scoreBands.FirstOrDefault(b => b.Code == startingBandCode)?.CumulativeStudyHours;
        var toHours = scoreBands.FirstOrDefault(b => b.Code == targetBandCode)?.CumulativeStudyHours;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var feasibility = GoalFeasibilityCalculator.Evaluate(fromHours, toHours, targetDate, weeklyHours, today);
        if (feasibility?.Status != FeasibilityStatus.Unrealistic)
        {
            return;
        }

        throw new LearningPathException(
            LearningPathException.GoalUnrealistic,
            $"This goal does not fit the time you have: it needs {feasibility.RequiredHours} hours of study, " +
            $"and {feasibility.WeeksAvailable} weeks at {weeklyHours:0.#} h/week gives you {feasibility.AvailableHours:0.#} hours. " +
            "Move the exam date back, lower the target band, or raise your weekly pace ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â then your path can be built.");
    }
}