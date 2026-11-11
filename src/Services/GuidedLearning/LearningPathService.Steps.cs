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
    /// A drill runs through the one selector (D11) — the same draw a free drill makes, so the plan and
    /// the practice cannot contradict each other (PLC-57). It draws from
    /// <c>(cert, skill, topic, level)</c>, excluding placement-eligible items, ordered least-seen first.
    /// <para>
    /// <b>Resume is at the exact unsubmitted item.</b> A step already holding a session hands back the
    /// same session; the practice screen's own "first unanswered" rule then lands on the exact item the
    /// learner left.
    /// </para>
    /// <para>
    /// <b>A step is never blocked by a cooldown</b> (PLC-60). The selector routes around cooling items
    /// within the topic and substitutes a sibling topic, and the session's own scope rows record what it
    /// actually drew so the screen can say so.
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

        var draw = await DrawDrillItemsAsync(step, level, cancellationToken);
        var selected = draw.Items;

        if (selected.Count == 0)
        {
            throw new LearningPathException(
                LearningPathException.NoQuestionsAvailable,
                $"No items are available for this step's band at {step.TargetCefr}. Its cell and every sibling topic in the band are empty.");
        }

        // A substituted draw is written on the step itself: the session's TopicId stays the step's own
        // coordinate, and this column is what says a sibling topic supplied part of it (PLC-60's "say
        // so"). It is a placement on the step's own fact, not a second copy of the plan.
        var substituted = draw.Outcome == StepDrawOutcome.SubstitutedSiblingTopic;

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

        return new StartStepResult(
            step.StepId, stepType, StepRoute.ItemSelector, $"/practice/{session.SessionId}", session.SessionId, null)
        {
            SubstitutedFromTopicId = substituted ? step.TopicId : null,
        };
    }

    /// <summary>
    /// D11 / PLC-60, through the one selector: what this step's own cell serves, and — when the cell is
    /// cooling — the sibling-topic fill that keeps the step executable. The selector owns the rule; this
    /// only hands it the step's coordinate.
    /// </summary>
    private async Task<StepDraw> DrawDrillItemsAsync(
        LearnerPathStep step, CefrLevel level, CancellationToken cancellationToken)
    {
        var wanted = step.PlannedItemCount
            ?? LearningPathPlanner.ItemCountFor(EnumCodes.Parse<SizingTier>(step.SizingTier), availableInCell: null);

        var selector = new ItemSelector(db);
        return await selector.DrawForStepAsync(
            step.LearnerPath.UserId, step.CertId, [step.SkillId], step.Modality, step.TopicId, level, wanted,
            cancellationToken);
    }

    /// <summary>A lesson step opens the reader at its own lesson and module. The reader keeps its own
    /// progress row per module, so a partially read lesson resumes where it was rather than at card
    /// one.</summary>
    private Task<StartStepResult> StartLessonStepAsync(
        LearnerPathStep step, PathStepType stepType, CancellationToken cancellationToken)
    {
        var lessonId = step.RefLessonId
            ?? throw new InvalidOperationException($"Lesson step {step.StepId} names no lesson.");

        // #240: the step type says which module to read. An expanded_remedial step reads the remedial
        // module; every other lesson step reads the primary one. D10's table states this pairing, and
        // LessonService.ModuleForStepType is the one copy of it.
        var module = LessonService.ModuleForStepType(stepType);
        return Task.FromResult(new StartStepResult(
            step.StepId, stepType, StepRoute.LessonReader, $"/lessons/{lessonId}?module={module}", null, null));
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
