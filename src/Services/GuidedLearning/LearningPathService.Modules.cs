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

    public async Task<LearningPathModuleDetailView> GetModuleDetailAsync(int userId, int moduleId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var activePath = await db.LearnerPaths
            .AsNoTracking()
            .Include(p => p.PathTemplate)
            .Where(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active")
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new LearningPathException(LearningPathException.PathNotFound, "No active learning path found.");

        var module = await db.PathModules
            .AsNoTracking()
            .Include(m => m.Skill)
            .Include(m => m.PathModuleTopics)
            .Include(m => m.PathModuleItemPathModules)
            .Where(m => m.ModuleId == moduleId && m.TemplateId == activePath.TemplateId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new LearningPathException(LearningPathException.ModuleNotFound, $"Module {moduleId} was not found.");

        var allTemplateModules = await db.PathModules
            .AsNoTracking()
            .Where(m => m.TemplateId == activePath.TemplateId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);

        int moduleIndex = allTemplateModules.FindIndex(m => m.ModuleId == moduleId) + 1;
        int totalModules = allTemplateModules.Count;

        var itemIds = module.PathModuleItemPathModules.Select(i => i.ItemId).ToHashSet();
        var steps = await db.LearnerPathSteps
            .AsNoTracking()
            .Include(s => s.PathModuleItem)
                .ThenInclude(pmi => pmi.Lesson)
            .Include(s => s.PracticeSessions)
            .Where(s => s.LearnerPathId == activePath.LearnerPathId && itemIds.Contains(s.PathModuleItemId))
            .OrderBy(s => s.PathModuleItem.DisplayOrder)
            .ToListAsync(cancellationToken);

        var lessonIds = steps
            .Where(s => s.PathModuleItem.RefLessonId.HasValue)
            .Select(s => s.PathModuleItem.RefLessonId!.Value)
            .Distinct()
            .ToList();

        if (lessonIds.Count > 0)
        {
            var lessonProgresses = await db.LearnerLessonProgresses
                .AsNoTracking()
                .Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId))
                .ToDictionaryAsync(p => p.LessonId, cancellationToken);

            foreach (var s in steps)
            {
                if (s.PathModuleItem.RefLessonId is { } lid && lessonProgresses.TryGetValue(lid, out var prog))
                {
                    if (prog.IsCompleted)
                    {
                        s.Status = "done";
                        s.CompletedAt ??= prog.CompletedAt;
                        s.Score = null;
                    }
                    else if (prog.CardsCompleted > 0 && s.Status != "locked")
                    {
                        s.Status = "in_progress";
                        s.StartedAt ??= prog.FirstOpenedAt;
                    }
                }
            }
        }

        bool isPremium = await db.CreditTransactions
            .AnyAsync(t => t.LearnerPathId == activePath.LearnerPathId && t.Reason == CreditReason.PremiumPath.ToCode(), cancellationToken);

        string moduleStatus = "not_started";
        if (steps.Count > 0 && steps.All(s => s.Status == "done"))
        {
            moduleStatus = "done";
        }
        else if (steps.Any(s => s.Status is "in_progress" or "done"))
        {
            moduleStatus = "in_progress";
        }
        else if (steps.Any(s => s.Status == "available"))
        {
            moduleStatus = "not_started";
        }
        else if (steps.Count > 0 && steps.All(s => s.Status == "locked"))
        {
            moduleStatus = "locked";
        }

        bool isLocked = moduleStatus == "locked";

        int totalQuestions = module.PathModuleItemPathModules.Sum(i => i.QuestionCount ?? 20);
        var sessionIds = steps.SelectMany(s => s.PracticeSessions).Select(ps => ps.SessionId).ToList();
        var practiceAnswers = sessionIds.Count > 0
            ? await db.PracticeAnswers.AsNoTracking().Where(pa => sessionIds.Contains(pa.SessionId)).ToListAsync(cancellationToken)
            : new List<PracticeAnswer>();
        int answeredCount = practiceAnswers.Count(a => a.SelectedOptionId != null);
        int correctCount = practiceAnswers.Count(a => a.IsCorrect == true);
        int? accuracyPct = answeredCount > 0 ? (int)Math.Round((double)correctCount / answeredCount * 100) : null;

        int estMinutes = module.EstimatedMinutes ?? 20;
        var allSessions = steps.SelectMany(s => s.PracticeSessions).ToList();
        int timeSpentMinutes = (int)allSessions.Sum(s =>
        {
            if (s.FinishedAt.HasValue)
            {
                return Math.Max(1, (s.FinishedAt.Value - s.StartedAt).TotalMinutes);
            }
            var elapsed = (DateTimeOffset.UtcNow - s.StartedAt).TotalMinutes;
            return Math.Clamp(elapsed, 0, estMinutes * 2);
        });
        int estRemaining = Math.Max(0, estMinutes - timeSpentMinutes);

        int cumulativeAnswered = answeredCount;
        var topics = new List<ModuleTopicInfo>();
        foreach (var t in module.PathModuleTopics.OrderBy(t => t.DisplayOrder))
        {
            string topicStatus = "not_started";
            if (moduleStatus == "done")
            {
                topicStatus = "done";
            }
            else if (moduleStatus is "not_started" or "locked")
            {
                topicStatus = "not_started";
            }
            else // in_progress
            {
                int qCount = t.QuestionCount ?? 10;
                if (cumulativeAnswered >= qCount)
                {
                    topicStatus = "done";
                    cumulativeAnswered -= qCount;
                }
                else if (cumulativeAnswered > 0)
                {
                    topicStatus = "in_progress";
                    cumulativeAnswered = 0;
                }
                else
                {
                    topicStatus = "not_started";
                }
            }
            topics.Add(new ModuleTopicInfo(t.TopicId, t.Title, t.QuestionCount, topicStatus, isPremium ? t.VideoPath : null, isPremium && !string.IsNullOrEmpty(t.VideoPath)));
        }

        var stepInfos = steps.Select(s =>
        {
            var itemType = EnumCodes.Parse<PathModuleItemType>(s.PathModuleItem.ItemType);
            var stepStatus = EnumCodes.Parse<LearnerPathStepStatus>(s.Status);
            string title = itemType switch
            {
                PathModuleItemType.PracticeSection => $"Practice: {module.Title}",
                PathModuleItemType.MockExam => "Mock Exam Assessment",
                PathModuleItemType.AiTask => "AI-Graded Task",
                PathModuleItemType.Mentor => "Mentor Session",
                PathModuleItemType.Lesson => s.PathModuleItem.Lesson?.Title ?? $"Lesson: {module.Title}",
                _ => module.Title
            };
            return new ModuleStepInfo(
                s.StepId,
                s.PathModuleItemId,
                itemType,
                title,
                stepStatus,
                s.Score,
                s.PathModuleItem.QuestionCount,
                s.PathModuleItem.IsPremiumOnly,
                CanStart: stepStatus != LearnerPathStepStatus.Locked && (!s.PathModuleItem.IsPremiumOnly || isPremium),
                s.StartedAt,
                s.CompletedAt,
                s.PathModuleItem.RefSectionId,
                s.PathModuleItem.RefExamId,
                s.PathModuleItem.RefLessonId);
        }).ToList();

        var activeStep = steps.FirstOrDefault(s => s.Status == "in_progress" && (!s.PathModuleItem.IsPremiumOnly || isPremium))
            ?? steps.FirstOrDefault(s => s.Status == "available" && (!s.PathModuleItem.IsPremiumOnly || isPremium))
            ?? steps.FirstOrDefault(s => s.Status == "in_progress")
            ?? steps.FirstOrDefault(s => s.Status == "available")
            ?? steps.FirstOrDefault();

        return new LearningPathModuleDetailView(
            activePath.LearnerPathId,
            module.ModuleId,
            module.Title,
            module.GroupName,
            module.Description,
            estMinutes,
            module.DisplayOrder,
            module.Skill.Code,
            module.Skill.Name,
            EnumCodes.Parse<Modality>(module.Skill.Modality),
            moduleStatus,
            isLocked,
            moduleIndex,
            totalModules,
            activePath.PathTemplate.FromBandCode,
            activePath.PathTemplate.ToBandCode,
            answeredCount,
            totalQuestions,
            accuracyPct,
            correctCount,
            answeredCount,
            timeSpentMinutes,
            estRemaining,
            isPremium,
            topics,
            stepInfos,
            activeStep?.StepId);
    }
}
