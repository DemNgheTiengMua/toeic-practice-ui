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
    public async Task<LearningPathSummaryView> GetActivePathAsync(int userId, CancellationToken cancellationToken = default)
    {
        // #233 / D10: closing a week is observed on the screen that shows the window. Reading the path
        // is the one moment the learner asks "what is my week", and a window that only advanced from a
        // background worker would leave a learner who returned after a fortnight looking at week one.
        // It is idempotent, so the many readers of this method cost nothing once the window is current.
        await AdvanceWindowAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .Select(c => new CertificateOption(c.Code, c.Name, c.SectionNoun))
            .SingleAsync(cancellationToken);

        var scoreBands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == activeCertId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        var activePath = await db.LearnerPaths
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active")
            .FirstOrDefaultAsync(cancellationToken);

        if (activePath is null)
        {
            return new LearningPathSummaryView(
                HasActivePath: false,
                ActiveCert: cert,
                LearnerPathId: null,
                FromBandCode: null,
                ToBandCode: null,
                FromBandName: null,
                ToBandName: null,
                CurrentBandCode: null,
                CurrentScore: null,
                TargetBandCode: null,
                TargetScore: null,
                Status: null,
                StartedAt: null,
                TotalSteps: 0,
                CompletedSteps: 0,
                TotalEstimatedMinutes: 0,
                RemainingMinutes: 0,
                CefrBands: scoreBands.Select(b => new CefrBandInfo(b.Code, b.Name, b.MinTotal, b.MaxTotal, false, false, false)).ToList(),
                OpenSteps: [],
                NextStepId: null);
        }

        var target = await db.TargetBands
            .AsNoTracking()
            .Where(tb => tb.UserId == userId && tb.CertId == activeCertId)
            .Select(tb => tb.TargetBandCode)
            .FirstOrDefaultAsync(cancellationToken);

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.PlacementId == activePath.PlacementId)
            .FirstOrDefaultAsync(cancellationToken);

        int? currentScore = latestPlacement?.TotalScore;
        var toBand = scoreBands.FirstOrDefault(b => b.Code == target);
        int? targetScore = toBand?.MinTotal;

        var cefrBands = scoreBands.Select(b => new CefrBandInfo(
            b.Code,
            b.Name,
            b.MinTotal,
            b.MaxTotal,
            currentScore.HasValue && currentScore.Value >= b.MinTotal,
            b.Code == activePath.CurrentBandCode,
            b.Code == target)).ToList();

        var steps = await LoadPathStepsAsync(activePath.LearnerPathId, cancellationToken);
        await ApplyLessonProgressAsync(userId, steps, cancellationToken);
        var (guidesBySkill, rubricsBySkill) = await LoadProductiveMetadataAsync(activePath.CertId, steps, cancellationToken);

        var weeks = steps
            .Where(s => s.WeekNumber.HasValue)
            .GroupBy(s => s.WeekNumber!.Value)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var weekSteps = g
                    .OrderBy(s => s.WeekSequence)
                    .ThenBy(s => s.StepId)
                    .ToList();

                var scored = weekSteps.Where(s => s.ActualAccuracyPct.HasValue).ToList();
                decimal? accuracy = scored.Count > 0 ? scored.Average(s => s.ActualAccuracyPct!.Value) : null;
                var weekTarget = weekSteps.Count > 0 ? weekSteps.Max(s => s.TargetAccuracyPct) : 70;
                var completed = weekSteps.Count(s => s.Status == LearnerPathStepStatus.Done.ToCode());

                return new LearningPathWeekSummary(
                    g.Key,
                    weekSteps.Count,
                    completed,
                    weekSteps.Sum(s => s.EstimatedMinutes ?? 0),
                    weekSteps.Where(s => s.Status == LearnerPathStepStatus.Done.ToCode()).Sum(s => s.EstimatedMinutes ?? 0),
                    LearningPathPlanner.IsWeekOnTrack(weekSteps.Count, completed, accuracy, weekTarget),
                    weekTarget,
                    accuracy,
                    weekSteps.Select(s => ToStepInfo(s, guidesBySkill, rubricsBySkill)).ToList());
            })
            .ToList();

        var open = steps
            .Where(s => s.Status != LearnerPathStepStatus.Done.ToCode())
            .OrderBy(s => s.WeekNumber ?? int.MaxValue)
            .ThenBy(s => s.WeekSequence)
            .ThenBy(s => s.StepId)
            .ToList();

        // #232 / D10: a step's level is its skill's own level. Reading it back off the steps the plan
        // already carries keeps this a read of one fact, not a second computation that could disagree.
        var skillLevels = steps
            .GroupBy(s => s.Skill.Code)
            .ToDictionary(g => g.Key, g => CefrLevel.TryParseAuthored(g.First().TargetCefr, out var lvl) ? lvl : CefrLevel.PreA1);

        return new LearningPathSummaryView(
            HasActivePath: true,
            ActiveCert: cert,
            LearnerPathId: activePath.LearnerPathId,
            FromBandCode: activePath.CurrentBandCode,
            ToBandCode: target,
            FromBandName: scoreBands.FirstOrDefault(b => b.Code == activePath.CurrentBandCode)?.Name ?? activePath.CurrentBandCode,
            ToBandName: toBand?.Name ?? target,
            CurrentBandCode: activePath.CurrentBandCode,
            CurrentScore: currentScore,
            TargetBandCode: target,
            TargetScore: targetScore,
            Status: EnumCodes.Parse<LearnerPathStatus>(activePath.Status),
            StartedAt: activePath.StartedAt,
            TotalSteps: steps.Count,
            CompletedSteps: steps.Count(s => s.Status == LearnerPathStepStatus.Done.ToCode()),
            TotalEstimatedMinutes: steps.Sum(s => s.EstimatedMinutes ?? 0),
            RemainingMinutes: open.Sum(s => s.EstimatedMinutes ?? 0),
            CefrBands: cefrBands,
            OpenSteps: open.Select(s => ToStepInfo(s, guidesBySkill, rubricsBySkill)).ToList(),
            NextStepId: open.FirstOrDefault()?.StepId)
        {
            Weeks = weeks,
            SkillLevels = skillLevels
        };
    }

    /// <summary>
    /// #232: one step, by its own id. There is no module to address — a step is a coordinate the
    /// timeline renders inline — and this returns null when the step is not on this learner's path.
    /// </summary>
    public async Task<PathStepInfo?> GetStepAsync(int userId, int stepId, CancellationToken cancellationToken = default)
    {
        var step = await db.LearnerPathSteps
            .AsNoTracking()
            .Include(s => s.Skill)
            .Include(s => s.Topic)
            .Include(s => s.Lesson)
            .Include(s => s.Exam)
            .Include(s => s.LearnerPath)
            .FirstOrDefaultAsync(s => s.StepId == stepId
                && s.LearnerPath.UserId == userId
                && s.LearnerPath.Status == "active", cancellationToken);

        if (step is null)
        {
            return null;
        }

        var (guidesBySkill, rubricsBySkill) = await LoadProductiveMetadataAsync(step.CertId, [step], cancellationToken);
        return ToStepInfo(step, guidesBySkill, rubricsBySkill);
    }

    /// <summary>The path's steps with everything a coordinate needs to render, in plan order.</summary>
    private async Task<List<LearnerPathStep>> LoadPathStepsAsync(int learnerPathId, CancellationToken cancellationToken) =>
        await db.LearnerPathSteps
            .AsNoTracking()
            .Include(s => s.Skill)
            .Include(s => s.Topic)
            .Include(s => s.Lesson)
            .Include(s => s.Exam)
            .Where(s => s.LearnerPathId == learnerPathId)
            .OrderBy(s => s.WeekNumber ?? int.MaxValue)
            .ThenBy(s => s.WeekSequence)
            .ThenBy(s => s.StepId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// #154: a lesson the learner has opened but not finished is "in progress". `done` is persisted by
    /// <c>LessonService</c>; only the in-progress hint is derived here, because showing a not-yet-closed
    /// step as done would hide the bug the persistence fixed.
    /// </summary>
    private async Task ApplyLessonProgressAsync(
        int userId, List<LearnerPathStep> steps, CancellationToken cancellationToken)
    {
        var lessonSteps = steps.Where(s => s.RefLessonId.HasValue && s.Status != LearnerPathStepStatus.Locked.ToCode()).ToList();
        if (lessonSteps.Count == 0)
        {
            return;
        }

        var lessonIds = lessonSteps.Select(s => s.RefLessonId!.Value).Distinct().ToList();
        var progresses = await db.LearnerLessonProgresses
            .AsNoTracking()
            .Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId))
            .ToDictionaryAsync(p => p.LessonId, cancellationToken);

        foreach (var step in lessonSteps)
        {
            if (progresses.TryGetValue(step.RefLessonId!.Value, out var progress)
                && !progress.IsCompleted && progress.CardsCompleted > 0)
            {
                step.Status = LearnerPathStepStatus.InProgress.ToCode();
                step.StartedAt ??= progress.FirstOpenedAt;
            }
        }
    }

    private async Task<(Dictionary<int, string> GuidesBySkill, Dictionary<int, IReadOnlyList<string>> RubricsBySkill)>
        LoadProductiveMetadataAsync(int certId, List<LearnerPathStep> steps, CancellationToken cancellationToken)
    {
        var productiveSkillIds = steps
            .Where(s => string.Equals(s.Modality, Modality.Writing.ToCode(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.Modality, Modality.Speaking.ToCode(), StringComparison.OrdinalIgnoreCase))
            .Select(s => s.SkillId)
            .Distinct()
            .ToList();

        if (productiveSkillIds.Count == 0)
        {
            return ([], []);
        }

        var guides = await db.Lessons
            .AsNoTracking()
            .Where(l => l.CertId == certId && productiveSkillIds.Contains(l.SkillId) && l.Summary != null)
            .OrderBy(l => l.DisplayOrder)
            .Select(l => new { l.SkillId, l.Summary })
            .ToListAsync(cancellationToken);

        var guidesBySkill = guides
            .GroupBy(g => g.SkillId)
            .ToDictionary(g => g.Key, g => g.First().Summary!);

        var criteria = await db.GradingCriteria
            .AsNoTracking()
            .Where(c => productiveSkillIds.Contains(c.SkillId))
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new { c.SkillId, c.Name, c.RubricDescription })
            .ToListAsync(cancellationToken);

        var rubricsBySkill = criteria
            .GroupBy(c => c.SkillId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g
                    .Select(c => string.IsNullOrWhiteSpace(c.RubricDescription) ? c.Name : $"{c.Name}: {c.RubricDescription}")
                    .ToList());

        return (guidesBySkill, rubricsBySkill);
    }

    /// <summary>#232/D21a: the four metadata columns a step shows before it starts — sequence and
    /// skill, topic code and name, level badge, exact item count with estimated minutes.</summary>
    private static PathStepInfo ToStepInfo(
        LearnerPathStep step,
        IReadOnlyDictionary<int, string> guidesBySkill,
        IReadOnlyDictionary<int, IReadOnlyList<string>> rubricsBySkill)
    {
        var stepType = EnumCodes.Parse<PathStepType>(step.StepType);
        var status = EnumCodes.Parse<LearnerPathStepStatus>(step.Status);
        var modality = EnumCodes.Parse<Modality>(step.Modality);
        var isProductive = modality is Modality.Writing or Modality.Speaking;

        string? guideText = null;
        IReadOnlyList<string>? rubricCriteria = null;
        if (isProductive)
        {
            guideText = step.Lesson?.Summary
                ?? (guidesBySkill.TryGetValue(step.SkillId, out var g) ? g : null)
                ?? (modality == Modality.Writing
                    ? "Plan your response around task achievement, coherence, lexical resource, and grammatical accuracy."
                    : "Structure your spoken response clearly with relevant examples, natural fluency, and accurate pronunciation.");
            rubricCriteria = rubricsBySkill.TryGetValue(step.SkillId, out var r) && r.Count > 0
                ? r
                : ["Task Achievement & Coherence", "Lexical Resource & Grammar"];
        }

        return new PathStepInfo(
            step.StepId,
            step.WeekNumber ?? 0,
            step.WeekSequence,
            stepType,
            StepTitle(step, stepType),
            status,
            step.Skill.Code,
            step.Skill.Name,
            modality,
            step.Topic.Code,
            step.Topic.Name,
            CefrLevel.TryParseAuthored(step.TargetCefr, out var level) ? level : CefrLevel.PreA1,
            EnumCodes.Parse<SizingTier>(step.SizingTier),
            step.PlannedItemCount,
            step.EstimatedMinutes,
            step.Score,
            step.ActualAccuracyPct,
            step.TargetAccuracyPct,
            CanStart: status != LearnerPathStepStatus.Locked,
            step.StartedAt,
            step.CompletedAt,
            step.RefLessonId,
            step.RefExamId,
            GuideText: guideText,
            RubricCriteria: rubricCriteria,
            IsZeroCreditAiGrading: isProductive);
    }

    /// <summary>The one-line name a step is shown under, read off what the step actually opens so a
    /// step's title cannot drift from its activity.</summary>
    private static string StepTitle(LearnerPathStep step, PathStepType stepType) => stepType switch
    {
        PathStepType.ConceptLesson or PathStepType.ExpandedRemedial => step.Lesson?.Title ?? step.Topic.Name,
        PathStepType.MockSection => step.Exam?.Name ?? step.Topic.Name,
        _ => step.Topic.Name,
    };
}
