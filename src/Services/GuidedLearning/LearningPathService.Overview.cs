using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed partial class LearningPathService
{
    public async Task<LearningPathSummaryView> GetActivePathAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .Select(c => new CertificateOption(c.Code, c.Name, c.SectionNoun))
            .SingleAsync(cancellationToken);

        var activePath = await db.LearnerPaths
            .AsNoTracking()
            .Include(p => p.PathTemplate)
                .ThenInclude(t => t.ScoreBand)
            .Include(p => p.PathTemplate)
                .ThenInclude(t => t.ScoreBandNavigation)
            .Include(p => p.LearnerPathSteps)
                .ThenInclude(s => s.PathModuleItem)
                    .ThenInclude(pmi => pmi.PathModule)
                        .ThenInclude(m => m.Skill)
            .Include(p => p.LearnerPathSteps)
                .ThenInclude(s => s.PathModuleItem)
                    .ThenInclude(pmi => pmi.Lesson)
            .Include(p => p.LearnerPathSteps)
                .ThenInclude(s => s.PathModuleItem)
                    .ThenInclude(pmi => pmi.Exam)
            .Include(p => p.LearnerPathSteps)
                .ThenInclude(s => s.PathModuleItem)
                    .ThenInclude(pmi => pmi.Section)
            .Where(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active")
            .FirstOrDefaultAsync(cancellationToken);

        var scoreBands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == activeCertId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        if (activePath == null)
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
                TotalModules: 0,
                CompletedModules: 0,
                TotalEstimatedMinutes: 0,
                RemainingMinutes: 0,
                CefrBands: scoreBands.Select(b => new CefrBandInfo(b.Code, b.Name, b.MinTotal, b.MaxTotal, false, false, false)).ToList(),
                Modules: [],
                NextModuleId: null);
        }

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.PlacementId == activePath.PlacementId)
            .FirstOrDefaultAsync(cancellationToken);

        int? currentScore = latestPlacement?.TotalScore;
        var toBand = scoreBands.FirstOrDefault(b => b.Code == activePath.PathTemplate.ToBandCode);
        int? targetScore = toBand?.MinTotal;

        var cefrBands = scoreBands.Select(b => new CefrBandInfo(
            b.Code,
            b.Name,
            b.MinTotal,
            b.MaxTotal,
            currentScore.HasValue && currentScore.Value >= b.MinTotal,
            b.Code == activePath.CurrentBandCode,
            b.Code == activePath.PathTemplate.ToBandCode)).ToList();

        var templateModules = await db.PathModules
            .AsNoTracking()
            .Include(m => m.Skill)
            .Include(m => m.PathModuleItemPathModules)
            .Where(m => m.TemplateId == activePath.TemplateId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);

        // #154: the path's steps are what the learner works through. A module the plan put no step in
        // is not part of their path, and listing it made the screen show empty modules beside the real
        // ones. Filter first, then order by the order the plan actually visits the modules.
        var steppedItemIds = activePath.LearnerPathSteps
            .Select(s => s.PathModuleItemId)
            .ToHashSet();

        var itemToModuleMap = templateModules
            .SelectMany(m => m.PathModuleItemPathModules.Select(i => new { i.ItemId, m.ModuleId }))
            .ToDictionary(x => x.ItemId, x => x.ModuleId);

        var modulesWithSteps = templateModules
            .Where(m => m.PathModuleItemPathModules.Any(i => steppedItemIds.Contains(i.ItemId)))
            .ToList();

        var moduleOrder = new Dictionary<int, int>();
        var orderIndex = 0;
        foreach (var itemId in activePath.LearnerPathSteps.OrderBy(s => s.StepId).Select(s => s.PathModuleItemId))
        {
            if (itemToModuleMap.TryGetValue(itemId, out var modId) && !moduleOrder.ContainsKey(modId))
            {
                moduleOrder[modId] = orderIndex++;
            }
        }

        templateModules = modulesWithSteps
            .OrderBy(m => moduleOrder.TryGetValue(m.ModuleId, out var ord) ? ord : m.DisplayOrder)
            .ThenBy(m => m.DisplayOrder)
            .ToList();

        var activePathLessonSteps = activePath.LearnerPathSteps
            .Where(s => s.PathModuleItem?.ItemType == "lesson" && s.PathModuleItem.RefLessonId.HasValue)
            .ToList();
        if (activePathLessonSteps.Count > 0)
        {
            var lessonIds = activePathLessonSteps.Select(s => s.PathModuleItem.RefLessonId!.Value).Distinct().ToList();
            var progresses = await db.LearnerLessonProgresses
                .AsNoTracking()
                .Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId))
                .ToDictionaryAsync(p => p.LessonId, cancellationToken);

            foreach (var step in activePathLessonSteps)
            {
                // #154: `done` is persisted by LessonService now. Only the in-progress hint is
                // derived here; see the note in GetModuleDetailAsync.
                if (progresses.TryGetValue(step.PathModuleItem.RefLessonId!.Value, out var prog)
                    && !prog.IsCompleted && prog.CardsCompleted > 0 && step.Status != "locked")
                {
                    step.Status = "in_progress";
                    step.StartedAt ??= prog.FirstOpenedAt;
                }
            }
        }

        var moduleSummaries = new List<LearningPathModuleSummary>();
        int completedModulesCount = 0;
        int totalEstimatedMins = 0;
        int remainingMins = 0;
        int? nextModuleId = null;

        foreach (var module in templateModules)
        {
            var moduleItemIds = module.PathModuleItemPathModules.Select(i => i.ItemId).ToHashSet();
            var moduleSteps = activePath.LearnerPathSteps.Where(s => moduleItemIds.Contains(s.PathModuleItemId)).ToList();

            string status = "not_started";
            if (moduleSteps.Count > 0 && moduleSteps.All(s => s.Status == "done"))
            {
                status = "done";
                completedModulesCount++;
            }
            else if (moduleSteps.Any(s => s.Status is "in_progress" or "done"))
            {
                status = "in_progress";
            }
            else if (moduleSteps.Any(s => s.Status == "available"))
            {
                status = "not_started";
            }
            else if (moduleSteps.Count > 0 && moduleSteps.All(s => s.Status == "locked"))
            {
                status = "locked";
            }

            bool isLocked = status == "locked";
            bool canContinue = !isLocked && status != "done";
            int estMinutes = module.EstimatedMinutes ?? 15;
            totalEstimatedMins += estMinutes;
            if (status != "done")
            {
                remainingMins += estMinutes;
                nextModuleId ??= module.ModuleId;
            }

            int totalQ = module.PathModuleItemPathModules.Sum(i => i.QuestionCount ?? 20);
            int completedQ = moduleSteps.Where(s => s.Status == "done").Sum(s => s.PathModuleItem?.QuestionCount ?? 20);

            var activeStep = moduleSteps.FirstOrDefault(s => s.Status == "in_progress")
                ?? moduleSteps.FirstOrDefault(s => s.Status == "available")
                ?? moduleSteps.FirstOrDefault();

            moduleSummaries.Add(new LearningPathModuleSummary(
                module.ModuleId,
                module.TemplateId,
                module.Title,
                module.GroupName,
                module.Description,
                estMinutes,
                module.DisplayOrder,
                module.Skill.Code,
                module.Skill.Name,
                EnumCodes.Parse<Modality>(module.Skill.Modality),
                status,
                isLocked,
                canContinue,
                totalQ,
                completedQ,
                module.PathModuleItemPathModules.Count,
                moduleSteps.Count(s => s.Status == "done"),
                activeStep?.StepId));
        }

        var statusEnum = EnumCodes.Parse<LearnerPathStatus>(activePath.Status);

        // #154: the path is planned week by week. Grouping and the On Track verdict are computed here
        // from the stored rows — the view only draws what the planner decided.
        var weeks = activePath.LearnerPathSteps
            .Where(s => s.WeekNumber.HasValue)
            .GroupBy(s => s.WeekNumber!.Value)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var weekSteps = g
                    .OrderBy(s => s.StepId)
                    .Select(s => new PathWeekStepInfo(
                        s.StepId,
                        EnumCodes.Parse<PathModuleItemType>(s.PathModuleItem.ItemType),
                        StepTitle(s),
                        EnumCodes.Parse<LearnerPathStepStatus>(s.Status),
                        s.EstimatedMinutes,
                        s.Score,
                        s.ActualAccuracyPct,
                        s.TargetAccuracyPct,
                        CanStart: s.Status is "available" or "in_progress" or "done",
                        s.CompletedAt,
                        s.PathModuleItem.RefSectionId,
                        s.PathModuleItem.RefExamId,
                        s.PathModuleItem.RefLessonId))
                    .ToList();

                var scored = weekSteps.Where(s => s.ActualAccuracyPct.HasValue).ToList();
                decimal? accuracy = scored.Count > 0 ? scored.Average(s => s.ActualAccuracyPct!.Value) : null;
                var target = weekSteps.Count > 0 ? weekSteps.Max(s => s.TargetAccuracyPct) : 70;
                var completed = weekSteps.Count(s => s.Status == LearnerPathStepStatus.Done);

                return new LearningPathWeekSummary(
                    g.Key,
                    weekSteps.Count,
                    completed,
                    weekSteps.Sum(s => s.EstimatedMinutes ?? 0),
                    weekSteps.Where(s => s.Status == LearnerPathStepStatus.Done).Sum(s => s.EstimatedMinutes ?? 0),
                    LearningPathPlanner.IsWeekOnTrack(weekSteps.Count, completed, accuracy, target),
                    target,
                    accuracy,
                    weekSteps);
            })
            .ToList();

        return new LearningPathSummaryView(
            HasActivePath: true,
            ActiveCert: cert,
            LearnerPathId: activePath.LearnerPathId,
            FromBandCode: activePath.PathTemplate.FromBandCode,
            ToBandCode: activePath.PathTemplate.ToBandCode,
            FromBandName: activePath.PathTemplate.ScoreBand?.Name ?? activePath.PathTemplate.FromBandCode,
            ToBandName: activePath.PathTemplate.ScoreBandNavigation?.Name ?? activePath.PathTemplate.ToBandCode,
            CurrentBandCode: activePath.CurrentBandCode,
            CurrentScore: currentScore,
            TargetBandCode: activePath.PathTemplate.ToBandCode,
            TargetScore: targetScore,
            Status: statusEnum,
            StartedAt: activePath.StartedAt,
            TotalModules: templateModules.Count,
            CompletedModules: completedModulesCount,
            TotalEstimatedMinutes: totalEstimatedMins,
            RemainingMinutes: remainingMins,
            CefrBands: cefrBands,
            Modules: moduleSummaries,
            NextModuleId: nextModuleId)
        {
            Weeks = weeks
        };
    }

    /// <summary>The one-line name a step is shown under. Read off what the step actually points at, so
    /// a step's title cannot drift from its activity.</summary>
    private static string StepTitle(LearnerPathStep step) => step.PathModuleItem.ItemType switch
    {
        "lesson" => step.PathModuleItem.Lesson?.Title ?? "Lesson",
        "mock_exam" => step.PathModuleItem.Exam?.Name ?? "Mock exam",
        "practice_section" => step.PathModuleItem.Section?.Name ?? "Practice",
        _ => step.PathModuleItem.PathModule?.Title ?? "Step",
    };
}
