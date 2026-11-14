using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed partial class LearningPathService
{
    public const int LearningPathCostCredits = 5;
    public const int WeekCount = 4;

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
                "No target band is set for this certificate. Set your goal first - a target band and a date - then generate the path.");
        }

        var scoreBands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == activeCertId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        RefuseUnrealisticGoal(savedTarget, currentBand, toBand, scoreBands);

        // #313: Block Before Charge — verify wallet has at least 5 credits before building & persisting the path.
        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);
        if (balance < LearningPathCostCredits)
        {
            throw new CreditLedgerException(
                CreditLedgerException.InsufficientCredits,
                $"Insufficient credits to generate a learning path. Balance is {balance}, required is {LearningPathCostCredits}.",
                402);
        }

        var plan = await BuildDeterministic4WeekPlanAsync(
            userId, activeCertId, currentBand, toBand, scoreBands, savedTarget, cancellationToken);

        if (plan.Count == 0)
        {
            throw new LearningPathException(
                LearningPathException.NoQuestionsAvailable,
                "This certificate has no skills or content yet, so a path would have no steps. Author content for at least one skill first.");
        }

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.CertId == activeCertId ? 1 : 0)
            .ThenByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(cancellationToken) : null;

        await _creditLedger.ChargeLearningPathAsync(userId, LearningPathCostCredits, cancellationToken);

        var learnerPath = new LearnerPath
        {
            UserId = userId,
            CertId = activeCertId,
            CurrentBandCode = currentBand,
            PlacementId = latestPlacement?.PlacementId,
            Status = "active",
            StartedAt = DateTimeOffset.UtcNow,
            WindowStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ActiveWeek = 1,
            WindowSlides = 0
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

        var isFirst = true;
        foreach (var step in plan)
        {
            db.LearnerPathSteps.Add(new LearnerPathStep
            {
                LearnerPathId = learnerPath.LearnerPathId,
                CertId = activeCertId,
                SkillId = step.SkillId,
                Modality = step.Modality,
                TopicId = step.TopicId,
                TargetCefr = step.TargetCefr,
                StepType = step.StepType,
                SizingTier = step.SizingTier,
                RefLessonId = step.RefLessonId,
                RefExamId = step.RefExamId,
                Status = isFirst ? "available" : "locked",
                Score = null,
                WeekNumber = step.WeekNumber,
                WeekSequence = step.WeekSequence,
                PlannedItemCount = step.PlannedItemCount,
                EstimatedMinutes = step.EstimatedMinutes,
                TargetAccuracyPct = step.TargetAccuracyPct,
                ActualAccuracyPct = null,
                StartedAt = null,
                CompletedAt = null
            });
            isFirst = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return await GetActivePathAsync(userId, cancellationToken);
    }

    private sealed record PlannedStepRow(
        int SkillId,
        string Modality,
        int TopicId,
        string TargetCefr,
        string StepType,
        string SizingTier,
        int? RefLessonId,
        int? RefExamId,
        int WeekNumber,
        int WeekSequence,
        int? PlannedItemCount,
        int EstimatedMinutes,
        int TargetAccuracyPct);

    private static bool MatchesTargetBands(string? targetBands, ISet<string> bandCodes)
    {
        if (string.IsNullOrWhiteSpace(targetBands) ||
            targetBands.Trim().Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokens = targetBands.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(t => t.Equals("ALL", StringComparison.OrdinalIgnoreCase) || bandCodes.Contains(t));
    }

    private static CefrLevel ResolveStepCefr(string bandCode, CefrLevel? fallbackItemCefr)
    {
        if (CefrLevel.TryParseAuthored(bandCode, out var parsed))
        {
            return parsed;
        }

        if (fallbackItemCefr is { } itemLevel && itemLevel != CefrLevel.PreA1)
        {
            return itemLevel;
        }

        return CefrLevel.B1;
    }

    private async Task<List<PlannedStepRow>> BuildDeterministic4WeekPlanAsync(
        int userId,
        int certId,
        string currentBand,
        string toBand,
        IReadOnlyList<ScoreBand> scoreBands,
        TargetBand? savedTarget,
        CancellationToken cancellationToken)
    {
        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SkillId)
            .Select(s => new { s.SkillId, s.Modality })
            .ToListAsync(cancellationToken);

        if (skills.Count == 0)
        {
            return [];
        }

        // Determine band range [CurrentBand .. TargetBand]
        var fromIdx = scoreBands.ToList().FindIndex(b => b.Code.Equals(currentBand, StringComparison.OrdinalIgnoreCase));
        var toIdx = scoreBands.ToList().FindIndex(b => b.Code.Equals(toBand, StringComparison.OrdinalIgnoreCase));
        if (fromIdx < 0) fromIdx = 0;
        if (toIdx < fromIdx) toIdx = fromIdx;

        var fullRangeBands = scoreBands
            .Skip(fromIdx)
            .Take(toIdx - fromIdx + 1)
            .Select(b => b.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        fullRangeBands.Add(currentBand);
        fullRangeBands.Add(toBand);

        var foundationBands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentBand };
        var targetRangeBands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { toBand };
        if (scoreBands.Count > 0 && toIdx > fromIdx)
        {
            for (var i = fromIdx + 1; i <= toIdx; i++)
            {
                targetRangeBands.Add(scoreBands[i].Code);
            }
        }

        // Rank skills by placement accuracy (weakest first; unmeasured skills treated as priority foundation)
        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.CertId == certId ? 1 : 0)
            .ThenByDescending(p => p.TakenAt)
            .Select(p => new { p.AttemptId, p.ListeningPct, p.ReadingPct, p.SpeakingPct, p.WritingPct })
            .FirstOrDefaultAsync(cancellationToken);

        var skillScoreBySkillId = latestPlacement?.AttemptId is { } attId
            ? await db.AttemptSkillScores
                .AsNoTracking()
                .Where(s => s.AttemptId == attId)
                .ToDictionaryAsync(s => s.SkillId, s => s.ScaledScore, cancellationToken)
            : new Dictionary<int, int>();

        var modalityPct = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (latestPlacement?.ListeningPct is { } lp) modalityPct["listening"] = lp;
        if (latestPlacement?.ReadingPct is { } rp) modalityPct["reading"] = rp;
        if (latestPlacement?.SpeakingPct is { } sp) modalityPct["speaking"] = sp;
        if (latestPlacement?.WritingPct is { } wp) modalityPct["writing"] = wp;

        var rankedSkills = skills
            .OrderBy(s => skillScoreBySkillId.TryGetValue(s.SkillId, out var sc)
                ? sc
                : (modalityPct.TryGetValue(s.Modality, out var mpct) ? (int)Math.Round(mpct) : -1))
            .ToList();

        var modalities = rankedSkills.Select(s => s.Modality).Distinct().ToList();
        var allTopics = await db.Topics
            .AsNoTracking()
            .Where(t => modalities.Contains(t.Modality))
            .OrderBy(t => t.TopicId)
            .ToListAsync(cancellationToken);

        var allItems = await db.Items
            .AsNoTracking()
            .Where(i => modalities.Contains(i.Modality) && i.LifecycleStatus == ActiveItemLifecycle)
            .Select(i => new { i.ItemId, i.Modality, i.TopicId, i.CefrLevel, i.TargetBands, i.PlacementEligible })
            .ToListAsync(cancellationToken);

        var allLessons = await db.Lessons
            .AsNoTracking()
            .Where(l => l.CertId == certId)
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.LessonId)
            .Select(l => new { l.LessonId, l.SkillId, l.Modality, l.TopicId, l.CefrLevel, l.TargetBands, l.EstimatedMinutes })
            .ToListAsync(cancellationToken);

        var mockExamId = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId && e.Purpose == ExamPurpose.Mock.ToCode() && e.Status == ExamStatus.Published.ToCode())
            .OrderBy(e => e.ExamId)
            .Select(e => (int?)e.ExamId)
            .FirstOrDefaultAsync(cancellationToken);

        var targetBand = scoreBands.FirstOrDefault(b => b.Code == toBand);
        var targetAccuracy = LearningPathPlanner.TargetAccuracyFor(
            isTopBand: targetBand is not null && scoreBands.Count > 0 && targetBand.DisplayOrder >= scoreBands.Max(b => b.DisplayOrder));

        var steps = new List<PlannedStepRow>();

        for (var week = 1; week <= WeekCount; week++)
        {
            var isFoundationWeek = week <= 2;
            var preferredBands = isFoundationWeek ? foundationBands : targetRangeBands;
            var bandLabelForWeek = isFoundationWeek ? currentBand : toBand;
            var seq = 1;

            // Rotate/prioritize weakest skills in weeks 1-2, all skills in weeks 3-4
            var weekSkills = isFoundationWeek
                ? rankedSkills
                : rankedSkills.AsEnumerable().Reverse().ToList();

            foreach (var skill in weekSkills)
            {
                var skillItems = allItems
                    .Where(i => i.Modality == skill.Modality && !i.PlacementEligible)
                    .ToList();
                if (skillItems.Count == 0)
                {
                    skillItems = allItems.Where(i => i.Modality == skill.Modality).ToList();
                }

                // Nearest-band fallback for items: preferredBands+ALL -> fullRangeBands+ALL -> any active items for modality
                var matchedItems = skillItems.Where(i => MatchesTargetBands(i.TargetBands, preferredBands)).ToList();
                if (matchedItems.Count == 0)
                {
                    matchedItems = skillItems.Where(i => MatchesTargetBands(i.TargetBands, fullRangeBands)).ToList();
                }
                if (matchedItems.Count == 0)
                {
                    matchedItems = skillItems;
                }

                // Nearest-band fallback for lessons
                var skillLessons = allLessons.Where(l => l.SkillId == skill.SkillId).ToList();
                if (skillLessons.Count == 0)
                {
                    skillLessons = allLessons.Where(l => l.Modality == skill.Modality).ToList();
                }

                var lesson = skillLessons.FirstOrDefault(l => MatchesTargetBands(l.TargetBands, preferredBands))
                    ?? skillLessons.FirstOrDefault(l => MatchesTargetBands(l.TargetBands, fullRangeBands))
                    ?? skillLessons.FirstOrDefault();

                var topicId = matchedItems.FirstOrDefault()?.TopicId
                    ?? lesson?.TopicId
                    ?? allTopics.FirstOrDefault(t => t.Modality == skill.Modality)?.TopicId
                    ?? 0;

                if (topicId == 0)
                {
                    continue;
                }

                var stepCefr = ResolveStepCefr(
                    bandLabelForWeek,
                    matchedItems.FirstOrDefault()?.CefrLevel ?? lesson?.CefrLevel).Code;

                // 1. ConceptLesson (if lesson exists)
                if (lesson is not null)
                {
                    steps.Add(new PlannedStepRow(
                        skill.SkillId,
                        skill.Modality,
                        lesson.TopicId,
                        stepCefr,
                        PathStepType.ConceptLesson.ToCode(),
                        SizingTier.Developing.ToCode(),
                        lesson.LessonId,
                        null,
                        week,
                        seq++,
                        null,
                        Math.Max(5, lesson.EstimatedMinutes),
                        targetAccuracy));
                }

                // 2. FocusPractice (if drillable items exist)
                if (matchedItems.Count > 0)
                {
                    var itemCount = Math.Min(10, Math.Max(1, matchedItems.Count));
                    steps.Add(new PlannedStepRow(
                        skill.SkillId,
                        skill.Modality,
                        topicId,
                        stepCefr,
                        PathStepType.FocusPractice.ToCode(),
                        SizingTier.Developing.ToCode(),
                        null,
                        null,
                        week,
                        seq++,
                        itemCount,
                        Math.Max(5, itemCount * 2),
                        targetAccuracy));
                }

                // 3. ReviewSpaced on alternating weeks when items exist
                if (matchedItems.Count > 0 && week is 1 or 3)
                {
                    var reviewCount = Math.Min(6, Math.Max(1, matchedItems.Count));
                    steps.Add(new PlannedStepRow(
                        skill.SkillId,
                        skill.Modality,
                        topicId,
                        stepCefr,
                        PathStepType.ReviewSpaced.ToCode(),
                        SizingTier.Mastered.ToCode(),
                        null,
                        null,
                        week,
                        seq++,
                        reviewCount,
                        Math.Max(5, reviewCount * 2),
                        targetAccuracy));
                }
            }

            // Milestone MockSection at Weeks 2 and 4
            if (mockExamId.HasValue && week is 2 or 4)
            {
                var anchorSkill = rankedSkills[0];
                var anchorTopicId = allTopics.FirstOrDefault(t => t.Modality == anchorSkill.Modality)?.TopicId ?? 0;
                if (anchorTopicId > 0)
                {
                    var anchorCefr = ResolveStepCefr(bandLabelForWeek, null).Code;
                    steps.Add(new PlannedStepRow(
                        anchorSkill.SkillId,
                        anchorSkill.Modality,
                        anchorTopicId,
                        anchorCefr,
                        PathStepType.MockSection.ToCode(),
                        SizingTier.Developing.ToCode(),
                        null,
                        mockExamId.Value,
                        week,
                        seq++,
                        null,
                        20,
                        targetAccuracy));
                }
            }
        }

        return steps;
    }

    /// <summary>
    /// #233: the pacing and the blocks a week is planned from, in one place.
    /// <para>
    /// Generation calls this for its first four weeks and the window's slide calls it for the week it
    /// appends, so a synthesised week is sized and skewed by the same arithmetic as the plan it
    /// extends. Re-reading skill levels on each call is deliberate: the far end must plan from what the
    /// learner can do <b>now</b>, which is the whole reason a slide synthesises instead of copying.
    /// </para>
    /// </summary>
    private async Task<(LearningPathPlanner.WeeklyPacing Pacing, IReadOnlyList<SkillPlanBlock> Blocks)>
        ResolvePacingAndBlocksAsync(
            int userId,
            int certId,
            TargetBand? savedTarget,
            string currentBand,
            string toBand,
            IReadOnlyList<ScoreBand> scoreBands,
            IReadOnlyDictionary<int, CefrLevel>? skillLevels,
            CancellationToken cancellationToken)
    {
        var levels = skillLevels ?? await LoadSkillLevelsAsync(userId, certId, cancellationToken);
        var blocks = await BuildSkillPlanBlocksAsync(userId, certId, levels, toBand, cancellationToken);

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

        return (new LearningPathPlanner.WeeklyPacing(weeklyHours, remainingHours, daysRemaining), blocks);
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
            .Where(p => p.UserId == userId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.CertId == certId ? 1 : 0)
            .ThenByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        string? startingBand = null;
        if (latestPlacement is not null)
        {
            if (latestPlacement.CertId == certId && !string.IsNullOrEmpty(latestPlacement.ResultBandCode))
            {
                startingBand = latestPlacement.ResultBandCode;
            }
            else if (latestPlacement.OverallPct.HasValue)
            {
                var certBands = await db.ScoreBands
                    .AsNoTracking()
                    .Where(b => b.CertId == certId)
                    .OrderBy(b => b.MinTotal)
                    .ToListAsync(cancellationToken);

                if (certBands.Count > 0)
                {
                    var minScale = certBands.Min(b => b.MinTotal);
                    var maxScale = certBands.Max(b => b.MaxTotal);
                    var clampedPct = Math.Clamp(latestPlacement.OverallPct.Value, 0m, 100m);
                    var mappedScore = (int)Math.Round(minScale + (clampedPct / 100m) * (maxScale - minScale));
                    mappedScore = Math.Clamp(mappedScore, minScale, maxScale);
                    var matched = certBands.FirstOrDefault(b => mappedScore >= b.MinTotal && mappedScore <= b.MaxTotal)
                        ?? certBands.OrderBy(b => Math.Abs(((b.MinTotal + b.MaxTotal) / 2.0) - mappedScore)).First();
                    startingBand = matched.Code;
                }
            }
        }

        if (string.IsNullOrEmpty(startingBand))
        {
            var latestMock = await db.ExamAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.CertId == certId && a.Status == AttemptStatus.Graded.ToCode() && a.Exam.Purpose == ExamPurpose.Mock.ToCode() && a.BandCode != null)
                .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            startingBand = latestMock?.BandCode;
        }

        // #153: the band the goal recorded is the fallback when the certificate has no measured band yet.
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
    /// block at all, so an unrealistic goal is refused with reasons wherever generation is asked for.
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

            var mastery = await LoadCellMasteryAsync(userId, topics[0], level, cancellationToken);
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
    /// #153: an Unrealistic goal blocks path generation at the service layer, not just in the screen.
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
                "Move the exam date back, lower the target band, or raise your weekly pace so the deadline is feasible, then your path can be built.");
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
            "Move the exam date back, lower the target band, or raise your weekly pace so the deadline is feasible, then your path can be built.");
    }
}
