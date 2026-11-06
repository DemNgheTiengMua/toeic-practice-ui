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
    /// <summary>
    /// #232 / D10: opens a step on its own route. The three step types bind to three different
    /// surfaces — the item selector, the lesson reader and <c>IAttemptEngine</c> — and this switch is
    /// where that binding is real. A single route serving all three would be the label the ticket says
    /// is not three types.
    /// </summary>
    public async Task<StartStepResult> StartStepAsync(int userId, int stepId, CancellationToken cancellationToken = default)
    {
        var step = await db.LearnerPathSteps
            .Include(s => s.LearnerPath)
            .FirstOrDefaultAsync(s => s.StepId == stepId, cancellationToken)
            ?? throw new LearningPathException(LearningPathException.StepNotFound, $"Step {stepId} not found.");

        if (step.LearnerPath.UserId != userId || step.LearnerPath.Status != "active")
        {
            throw new LearningPathException(LearningPathException.StepNotFound, $"Step {stepId} not found.");
        }

        if (step.Status == LearnerPathStepStatus.Locked.ToCode())
        {
            throw new LearningPathException(LearningPathException.StepLocked, "This step is locked.");
        }

        if (step.Status == LearnerPathStepStatus.Available.ToCode())
        {
            step.Status = LearnerPathStepStatus.InProgress.ToCode();
            step.StartedAt ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        var stepType = EnumCodes.Parse<PathStepType>(step.StepType);
        var route = LearningPathPlanner.ExecutionRouteOf(stepType);

        return route switch
        {
            StepRoute.ItemSelector => await StartDrillStepAsync(step, stepType, cancellationToken),
            StepRoute.LessonReader => await StartLessonStepAsync(step, stepType, cancellationToken),
            StepRoute.AttemptEngine => await StartMockSectionStepAsync(step, stepType, cancellationToken),
            _ => throw new InvalidOperationException($"Step {stepId} names a route with no execution: {route}."),
        };
    }

    /// <summary>
    /// A drill runs through the item selector (D11). It draws from <c>(cert, skill, topic, level)</c>,
    /// excluding placement-eligible items, ordered least-seen first.
    /// <para>
    /// <b>Resume is at the exact unsubmitted item.</b> A step already holding a session hands back the
    /// same session; the practice screen's own "first unanswered" rule then lands on the exact item the
    /// learner left.
    /// </para>
    /// <para>
    /// shortcut: this is the path's own draw, not yet the one selector #235 collapses into
    /// SmartPracticePlanner. Upgrade when #235 lands — this file then calls that one selector and the
    /// two origins cannot disagree.
    /// </para>
    /// </summary>
    private async Task<StartStepResult> StartDrillStepAsync(
        LearnerPathStep step, PathStepType stepType, CancellationToken cancellationToken)
    {
        var existingSession = await db.PracticeSessions
            .Where(ps => ps.LearnerPathStepId == step.StepId)
            .OrderByDescending(ps => ps.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingSession is not null)
        {
            var target = existingSession.Status == PracticeStatus.Finished.ToCode()
                ? $"/practice/{existingSession.SessionId}/summary"
                : $"/practice/{existingSession.SessionId}";
            return new StartStepResult(step.StepId, stepType, StepRoute.ItemSelector, target, existingSession.SessionId, null);
        }

        var level = CefrLevel.TryParseAuthored(step.TargetCefr, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Step {step.StepId} carries an unauthorable level '{step.TargetCefr}'.");

        var selected = await DrawDrillItemsAsync(step, level, cancellationToken);

        if (selected.Count == 0)
        {
            throw new LearningPathException(
                LearningPathException.NoQuestionsAvailable,
                $"No items are available for this step's topic at {step.TargetCefr}. Its cell is empty or every item is cooling down.");
        }

        var session = new PracticeSession
        {
            UserId = step.LearnerPath.UserId,
            CertId = step.CertId,
            SectionId = null,
            TopicId = step.TopicId,
            CefrLevel = level,
            Modality = step.Modality,
            QuestionCount = selected.Count,
            Status = PracticeStatus.InProgress.ToCode(),
            CorrectCount = 0,
            QuestionSource = QuestionSource.All.ToCode(),
            LearnerPathStepId = step.StepId,
            StartedAt = DateTimeOffset.UtcNow,
        };

        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < selected.Count; i++)
        {
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                QuestionId = null,
                SectionId = null,
                ItemId = selected[i].ItemId,
                ItemVersionId = selected[i].ItemVersionId,
                SelectedLabel = null,
                SelectedOptionId = null,
                IsCorrect = null,
                HintUsed = false,
                DisplayOrder = i + 1,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        // The honest count the step actually got, which may be short of the tier's range: the step says
        // so rather than claiming a size the cell cannot fill. The minutes stay the plan's own, because
        // the week's budget is what the week is judged on and a per-item formula here would make the
        // week's minutes disagree with the pace the learner set.
        step.PlannedItemCount = selected.Count;
        await db.SaveChangesAsync(cancellationToken);

        return new StartStepResult(step.StepId, stepType, StepRoute.ItemSelector, $"/practice/{session.SessionId}", session.SessionId, null);
    }

    /// <summary>
    /// D11's selection order, applied to the step's own cell: least seen, then least recently seen,
    /// then item id — so a repeated draw is a reproducible choice rather than whatever the storage
    /// engine returns. Items still cooling down are excluded, and the step is never blocked: what is
    /// left is what the step gets.
    /// </summary>
    private async Task<List<(int ItemId, int ItemVersionId)>> DrawDrillItemsAsync(
        LearnerPathStep step, CefrLevel level, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var wanted = step.PlannedItemCount
            ?? LearningPathPlanner.ItemCountFor(EnumCodes.Parse<SizingTier>(step.SizingTier), availableInCell: null);

        var items = await db.Items
            .AsNoTracking()
            .Where(i => i.Modality == step.Modality
                && i.TopicId == step.TopicId
                && i.CefrLevel == level
                && i.LifecycleStatus == ActiveItemLifecycle
                && i.PlacementEligible == DrillPlacementEligible)
            .Select(i => new
            {
                i.ItemId,
                TimesSeen = db.PracticeAnswers.Count(pa => pa.ItemId == i.ItemId && pa.Session.UserId == step.LearnerPath.UserId),
                LastSeenAt = db.PracticeAnswers
                    .Where(pa => pa.ItemId == i.ItemId && pa.Session.UserId == step.LearnerPath.UserId)
                    .Max(pa => (DateTimeOffset?)pa.AnsweredAt),
                // A pool item cools from its own answers, not from `UserQuestionHistories`: that table
                // is keyed by the legacy `QuestionId`, so comparing one to an `ItemId` matched the
                // wrong rows (item 12 inherited question 12's cooldown) and no pool answer ever wrote
                // one, making the rule a no-op for every drill. Same 24-hour wrong-answer rule as
                // `PracticeService.RecordQuestionHistoryAsync`, read from the pool's own ledger.
                LastWrongAt = db.PracticeAnswers
                    .Where(pa => pa.ItemId == i.ItemId
                        && pa.Session.UserId == step.LearnerPath.UserId
                        && pa.IsCorrect == false)
                    .Max(pa => (DateTimeOffset?)pa.AnsweredAt),
            })
            .ToListAsync(cancellationToken);

        var eligible = items
            .Where(i => i.LastWrongAt is null || i.LastWrongAt + MasterySchedule.WrongAnswerCooldown <= now)
            .Where(i => i.LastSeenAt is null || i.LastSeenAt <= now)
            .ToList();

        if (eligible.Count == 0)
        {
            return [];
        }

        var itemIds = eligible.Select(i => i.ItemId).ToList();
        var latestVersions = await db.ItemVersions
            .AsNoTracking()
            .Where(v => itemIds.Contains(v.ItemId))
            .GroupBy(v => v.ItemId)
            .Select(g => new { ItemId = g.Key, ItemVersionId = g.Max(v => v.ItemVersionId) })
            .ToDictionaryAsync(v => v.ItemId, v => v.ItemVersionId, cancellationToken);

        return eligible
            .Where(i => latestVersions.ContainsKey(i.ItemId))
            .OrderBy(i => i.TimesSeen)
            .ThenBy(i => i.LastSeenAt ?? DateTimeOffset.MinValue)
            .ThenBy(i => i.ItemId)
            .Take(wanted)
            .Select(i => (i.ItemId, latestVersions[i.ItemId]))
            .ToList();
    }

    /// <summary>A lesson step opens the reader at its own lesson. The reader keeps its own progress
    /// row, so a partially read lesson resumes where it was.</summary>
    private Task<StartStepResult> StartLessonStepAsync(
        LearnerPathStep step, PathStepType stepType, CancellationToken cancellationToken)
    {
        var lessonId = step.RefLessonId
            ?? throw new InvalidOperationException($"Lesson step {step.StepId} names no lesson.");

        return Task.FromResult(new StartStepResult(
            step.StepId, stepType, StepRoute.LessonReader, $"/lessons/{lessonId}", null, null));
    }

    /// <summary>
    /// A mock section is a real sitting, so it resolves through <c>IAttemptEngine</c>'s own exam path
    /// rather than a second paper-opening scheme. The attempt is created by the sitting screen, which
    /// owns the countdown and the credit movement; this returns where to go.
    /// </summary>
    private async Task<StartStepResult> StartMockSectionStepAsync(
        LearnerPathStep step, PathStepType stepType, CancellationToken cancellationToken)
    {
        var examId = step.RefExamId
            ?? throw new InvalidOperationException($"Mock section step {step.StepId} names no exam.");

        var examExists = await db.Exams
            .AsNoTracking()
            .AnyAsync(e => e.ExamId == examId && e.CertId == step.CertId, cancellationToken);

        if (!examExists)
        {
            throw new LearningPathException(
                LearningPathException.NoQuestionsAvailable,
                $"The mock exam this step opens ({examId}) is no longer available for this certificate.");
        }

        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == step.LearnerPath.UserId
                && a.ExamId == examId
                && a.Status == AttemptStatus.InProgress.ToCode())
            .Select(a => (int?)a.AttemptId)
            .FirstOrDefaultAsync(cancellationToken);

        return new StartStepResult(
            step.StepId, stepType, StepRoute.AttemptEngine, $"/exams/{examId}/instructions", null, attempt);
    }
}
