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

    public async Task<StartStepResult> StartStepAsync(int userId, int stepId, CancellationToken cancellationToken = default)
    {
        var step = await db.LearnerPathSteps
            .Include(s => s.LearnerPath)
            .Include(s => s.PathModuleItem)
                .ThenInclude(pmi => pmi.PathModule)
            .FirstOrDefaultAsync(s => s.StepId == stepId, cancellationToken)
            ?? throw new LearningPathException(LearningPathException.StepNotFound, $"Step {stepId} not found.");

        if (step.LearnerPath.UserId != userId || step.LearnerPath.Status != "active")
        {
            throw new LearningPathException(LearningPathException.StepNotFound, $"Step {stepId} not found.");
        }

        if (step.Status == "locked")
        {
            throw new LearningPathException(LearningPathException.StepLocked, "This step is locked.");
        }

        if (step.PathModuleItem.IsPremiumOnly)
        {
            bool isPremium = await db.CreditTransactions
                .AnyAsync(t => t.LearnerPathId == step.LearnerPathId && t.Reason == CreditReason.PremiumPath.ToCode(), cancellationToken);
            if (!isPremium)
            {
                throw new LearningPathException(LearningPathException.StepLocked, "This step requires a premium learning path.");
            }
        }

        if (step.Status == "available")
        {
            step.Status = "in_progress";
            step.StartedAt ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        var itemType = EnumCodes.Parse<PathModuleItemType>(step.PathModuleItem.ItemType);

        switch (itemType)
        {
            case PathModuleItemType.PracticeSection:
            {
                int sectionId = step.PathModuleItem.RefSectionId
                    ?? throw new InvalidOperationException($"Practice section step {stepId} is missing RefSectionId.");

                var existingSession = await db.PracticeSessions
                    .Where(ps => ps.LearnerPathStepId == stepId && ps.UserId == userId)
                    .OrderByDescending(ps => ps.StartedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingSession != null)
                {
                    string target = existingSession.Status == PracticeStatus.Finished.ToCode()
                        ? $"/practice/{existingSession.SessionId}/summary"
                        : $"/practice/{existingSession.SessionId}";
                    return new StartStepResult(stepId, itemType, target, existingSession.SessionId, null);
                }

                var section = await db.Sections.SingleAsync(s => s.SectionId == sectionId, cancellationToken);
                var questionIds = await db.Questions
                    .Where(q => q.SectionId == sectionId && q.QuestionType == "mcq" && db.QuestionOptions.Any(o => o.QuestionId == q.QuestionId))
                    .OrderBy(q => q.DisplayOrder)
                    .ThenBy(q => q.QuestionId)
                    .Select(q => q.QuestionId)
                    .ToListAsync(cancellationToken);

                if (questionIds.Count == 0)
                {
                    throw new LearningPathException(LearningPathException.NoQuestionsAvailable, $"No practice questions are available for section {sectionId}.");
                }

                int qCount = Math.Min(questionIds.Count, step.PathModuleItem.QuestionCount ?? 20);
                if (qCount == 0 && questionIds.Count > 0) qCount = questionIds.Count;

                var session = new PracticeSession
                {
                    UserId = userId,
                    SectionId = sectionId,
                    Modality = section.Modality,
                    QuestionCount = Math.Max(1, qCount),
                    Status = PracticeStatus.InProgress.ToCode(),
                    CorrectCount = 0,
                    QuestionSource = QuestionSource.All.ToCode(),
                    FeedbackMode = FeedbackMode.Immediate.ToCode(),
                    LearnerPathStepId = step.StepId,
                    StartedAt = DateTimeOffset.UtcNow
                };

                db.PracticeSessions.Add(session);
                await db.SaveChangesAsync(cancellationToken);

                for (int i = 0; i < qCount; i++)
                {
                    db.PracticeAnswers.Add(new PracticeAnswer
                    {
                        SessionId = session.SessionId,
                        QuestionId = questionIds[i],
                        SectionId = sectionId,
                        SelectedOptionId = null,
                        IsCorrect = null,
                        HintUsed = false,
                        DisplayOrder = i + 1
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
                return new StartStepResult(stepId, itemType, $"/practice/{session.SessionId}", session.SessionId, null);
            }

            case PathModuleItemType.MockExam:
            {
                int examId = step.PathModuleItem.RefExamId
                    ?? throw new InvalidOperationException($"Mock exam step {stepId} is missing RefExamId.");
                return new StartStepResult(stepId, itemType, $"/exams/{examId}/instructions", null, null);
            }

            case PathModuleItemType.AiTask:
            {
                if (step.PathModuleItem.RefExamId is { } refExamId)
                {
                    var exam = await db.Exams.FindAsync([refExamId], cancellationToken);
                    if (exam?.Purpose == "speaking_practice")
                    {
                        return new StartStepResult(stepId, itemType, $"/speaking/{refExamId}/instructions", null, null);
                    }
                    if (exam?.Purpose == "writing_practice")
                    {
                        return new StartStepResult(stepId, itemType, $"/writing/{refExamId}/instructions", null, null);
                    }
                    return new StartStepResult(stepId, itemType, $"/exams/{refExamId}/instructions", null, null);
                }
                return new StartStepResult(stepId, itemType, "/practice", null, null);
            }

            case PathModuleItemType.Lesson:
            {
                int lessonId = step.PathModuleItem.RefLessonId
                    ?? throw new InvalidOperationException($"Lesson step {stepId} is missing RefLessonId.");
                return new StartStepResult(stepId, itemType, $"/lessons/{lessonId}", null, null);
            }

            case PathModuleItemType.Mentor:
            default:
                return new StartStepResult(stepId, itemType, "/mentors", null, null);
        }
    }

    public async Task<bool> CompleteStepAsync(int userId, int stepId, decimal? score = null, CancellationToken cancellationToken = default)
    {
        var step = await db.LearnerPathSteps
            .Include(s => s.LearnerPath)
            .Include(s => s.PathModuleItem)
            .FirstOrDefaultAsync(s => s.StepId == stepId && s.LearnerPath.UserId == userId, cancellationToken);

        if (step == null) return false;

        if (step.Status == "done") return true;

        var itemType = EnumCodes.Parse<PathModuleItemType>(step.PathModuleItem.ItemType);
        if (itemType == PathModuleItemType.Lesson)
        {
            var lessonId = step.PathModuleItem.RefLessonId;
            if (lessonId.HasValue)
            {
                var progress = await db.LearnerLessonProgresses
                    .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId.Value, cancellationToken);
                if (progress == null || !progress.IsCompleted)
                {
                    return false;
                }
                step.StartedAt ??= progress.FirstOpenedAt;
                step.CompletedAt = progress.CompletedAt ?? DateTimeOffset.UtcNow;
                step.Score = null;
            }
        }
        else
        {
            step.CompletedAt = DateTimeOffset.UtcNow;
            step.Score = score ?? step.Score ?? 100m;
        }

        step.Status = "done";

        var allSteps = await db.LearnerPathSteps
            .Include(s => s.PathModuleItem)
                .ThenInclude(pmi => pmi.PathModule)
            .Where(s => s.LearnerPathId == step.LearnerPathId)
            .OrderBy(s => s.PathModuleItem.PathModule.DisplayOrder)
            .ThenBy(s => s.PathModuleItem.DisplayOrder)
            .ToListAsync(cancellationToken);

        var nextLocked = allSteps.FirstOrDefault(s => s.Status == "locked");
        if (nextLocked != null)
        {
            nextLocked.Status = "available";
        }

        if (allSteps.All(s => s.Status == "done"))
        {
            step.LearnerPath.Status = "completed";
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
