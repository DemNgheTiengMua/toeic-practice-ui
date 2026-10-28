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
            .Select(c => new CertificateOption(c.Code, c.Name))
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
                IsPremium: false,
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

        bool isPremium = await db.CreditTransactions
            .AnyAsync(t => t.LearnerPathId == activePath.LearnerPathId && t.Reason == CreditReason.PremiumPath.ToCode(), cancellationToken);

        var templateModules = await db.PathModules
            .AsNoTracking()
            .Include(m => m.Skill)
            .Include(m => m.PathModuleItemPathModules)
            .Where(m => m.TemplateId == activePath.TemplateId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);

        var stepItemOrder = activePath.LearnerPathSteps
            .OrderBy(s => s.StepId)
            .Select(s => s.PathModuleItemId)
            .ToList();

        if (stepItemOrder.Count > 0)
        {
            var itemToModuleMap = templateModules
                .SelectMany(m => m.PathModuleItemPathModules.Select(i => new { i.ItemId, m.ModuleId }))
                .ToDictionary(x => x.ItemId, x => x.ModuleId);

            var moduleOrder = new Dictionary<int, int>();
            int orderIndex = 0;
            foreach (var itemId in stepItemOrder)
            {
                if (itemToModuleMap.TryGetValue(itemId, out var modId) && !moduleOrder.ContainsKey(modId))
                {
                    moduleOrder[modId] = orderIndex++;
                }
            }

            templateModules = templateModules
                .OrderBy(m => moduleOrder.TryGetValue(m.ModuleId, out var ord) ? ord : m.DisplayOrder)
                .ThenBy(m => m.DisplayOrder)
                .ToList();
        }

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
                if (progresses.TryGetValue(step.PathModuleItem.RefLessonId!.Value, out var prog))
                {
                    if (prog.IsCompleted)
                    {
                        step.Status = "done";
                        step.CompletedAt ??= prog.CompletedAt;
                        step.Score = null;
                    }
                    else if (prog.CardsCompleted > 0 && step.Status != "locked")
                    {
                        step.Status = "in_progress";
                        step.StartedAt ??= prog.FirstOpenedAt;
                    }
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
            IsPremium: isPremium,
            TotalModules: templateModules.Count,
            CompletedModules: completedModulesCount,
            TotalEstimatedMinutes: totalEstimatedMins,
            RemainingMinutes: remainingMins,
            CefrBands: cefrBands,
            Modules: moduleSummaries,
            NextModuleId: nextModuleId);
    }
}
