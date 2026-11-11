using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #216 / PLC-13, PLC-14, PLC-15: the placement-only half of the sitting engine - resume, the
/// inactivity clock, the recording retry and its idempotence. A separate partial file on purpose: a
/// placement is an adapter over the one engine, and none of this belongs on the mock exam's path.
/// </summary>
public sealed partial class AttemptEngine
{
    /// <summary>
    /// <b>The one place the inactivity clock is stamped.</b> It is called from a write that carried an
    /// answer, a draft, a recording or a position, never from a read. It also re-derives the deadline,
    /// because PLC-13's rule is <c>min(last_activity + 48 h, started_at + 72 h)</c> and a single stamp
    /// at create time would freeze the first arm at whatever it was then.
    /// <para>
    /// The purpose is read from the exam row when the caller's attempt did not load the navigation:
    /// <c>SaveAnswerAsync</c> loads only <c>ExamAttempts</c>, so a check that trusted
    /// <c>attempt.Exam</c> would read null, answer "a mock", and silently never stamp a placement.
    /// </para>
    /// <para>
    /// A mock keeps its own absolute <c>ExpiresAt</c>: its clock is shorter than 48 hours, so the rule
    /// would be a no-op there - but writing it anyway would let a mock's deadline drift on a long
    /// paper, which is not a change this ticket is allowed to make.
    /// </para>
    /// </summary>
    private async Task MarkPlacementActivityAsync(
        ExamAttempt attempt,
        CancellationToken cancellationToken,
        DateTimeOffset? now = null)
    {
        var purpose = attempt.Exam?.Purpose
            ?? await db.Exams.AsNoTracking()
                .Where(exam => exam.ExamId == attempt.ExamId)
                .Select(exam => exam.Purpose)
                .SingleAsync(cancellationToken);

        if (PlacementSitting.HasCountdown(purpose))
        {
            return;
        }

        var stamped = now ?? DateTimeOffset.UtcNow;
        attempt.LastActivityAt = stamped;
        attempt.ExpiresAt = PlacementSitting.ExpiresAt(attempt.StartedAt, stamped);
    }

    /// <summary>
    /// #216 / PLC-14: the resume screen's read. It answers the five facts the ticket names and it
    /// <b>writes nothing</b> - no position, no clock, no score. A resume that wrote would be the
    /// "reload advances you" defect in a different coat.
    /// </summary>
    public async Task<PlacementResumeView> GetPlacementResumeAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status == AttemptStatus.Abandoned.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "This exam attempt has been cancelled.");
        }

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "This exam attempt is no longer in progress.");
        }

        var questions = await LoadExamQuestionsAsync(attempt.ExamId, cancellationToken);
        var answersByQuestion = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.AttemptId == attempt.AttemptId)
            .ToDictionaryAsync(a => a.QuestionId, cancellationToken);
        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Where(r => r.AttemptId == attempt.AttemptId)
            .ToDictionaryAsync(r => r.QuestionId, cancellationToken);

        // Synced answers are SERVER rows that carry a choice, never the browser's queued payloads:
        // the server cannot see the client's queue, so a number counted from it would be a number the
        // database does not hold.
        bool Answered(Question question) =>
            question.QuestionType == QuestionType.FreeResponse.ToCode()
                ? freeResponses.TryGetValue(question.QuestionId, out var response)
                  && (!string.IsNullOrWhiteSpace(response.ResponseText) || !string.IsNullOrWhiteSpace(response.AudioPath))
                : answersByQuestion.TryGetValue(question.QuestionId, out var answer) && IsAnswered(answer);

        var phaseSkills = questions
            .GroupBy(q => q.Section.Skill)
            .OrderBy(g => g.Key.DisplayOrder)
            .ToList();

        var activeIndex = attempt.ActiveSkillId is int activeSkillId
            ? phaseSkills.FindIndex(g => g.Key.SkillId == activeSkillId)
            : -1;

        var skills = new List<PlacementResumeSkill>(phaseSkills.Count);
        for (var index = 0; index < phaseSkills.Count; index++)
        {
            var group = phaseSkills[index];
            skills.Add(new PlacementResumeSkill(
                group.Key.SkillId,
                group.Key.Code,
                group.Key.Name,
                // A skill is done when the sitting has moved PAST it: that is what ActiveSkillId says,
                // and it is why a partly answered closed skill still reads "done" rather than "in
                // progress", which would offer a resume into a locked part.
                IsDone: activeIndex >= 0 && index < activeIndex,
                IsActive: index == activeIndex,
                AnsweredCount: group.Count(Answered),
                TotalQuestions: group.Count()));
        }

        var placementId = await db.PlacementTests
            .Where(p => p.AttemptId == attempt.AttemptId)
            .Select(p => p.PlacementId)
            .FirstOrDefaultAsync(cancellationToken);

        return new PlacementResumeView(
            placementId,
            attempt.AttemptId,
            attempt.Exam?.Name ?? string.Empty,
            attempt.Cert?.Code ?? string.Empty,
            attempt.StartedAt,
            attempt.ExpiresAt,
            skills,
            attempt.ActiveSkillId,
            skills.FirstOrDefault(s => s.IsActive)?.Name,
            ActiveStage: activeIndex < 0 ? null : activeIndex + 1,
            TotalStages: skills.Count,
            SyncedAnswerCount: questions.Count(Answered),
            TotalQuestions: questions.Count,
            LastQuestionId: attempt.CurrentExamItemId);
    }

    /// <summary>
    /// #216 / PLC-15: record one interrupted placement recording and answer what happens next.
    /// <para>
    /// <b>Idempotent per interruption, not per page load.</b> The caller mints a
    /// <paramref name="sessionToken"/> when the recording starts; a reload that re-asks with the same
    /// token is the SAME interruption and changes nothing. That is the only difference between
    /// "exactly one retry" and "one more retry on every refresh".
    /// </para>
    /// <para>
    /// <b>It touches no score and no status.</b> PLC-16 makes a reload during grading a pure read, and
    /// a retry count is not a derived fact - so nothing here reaches
    /// <c>WriteAttemptScoreAsync</c> or the placement snapshot writer.
    /// </para>
    /// </summary>
    public async Task<PlacementRecordingDecision> RecordPlacementRecordingInterruptionAsync(
        int attemptId,
        int questionId,
        int userId,
        string sessionToken,
        int capturedMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        var attempt = await LoadSittingAttemptAsync(db.ExamAttempts, attemptId, userId, cancellationToken);

        var response = await db.FreeResponses
            .SingleOrDefaultAsync(r => r.AttemptId == attemptId && r.QuestionId == questionId, cancellationToken)
            ?? throw new AttemptEngineException(
                AttemptEngineException.QuestionNotFound,
                $"Free-response question {questionId} not found in attempt.");

        // The same interruption, re-asked: report the stored state and write nothing. Re-deriving with
        // the same token cannot grant a second retry, because the count already includes it.
        if (string.Equals(response.RecordingSessionToken, sessionToken, StringComparison.Ordinal))
        {
            return new PlacementRecordingDecision(
                response.RecordingInterruptions,
                response.RecordingInterruptions <= 1
                    ? PlacementRecordingOutcome.Retry
                    : PlacementRecordingOutcome.Finalise,
                null);
        }

        var siblingId = await FindUnseenSiblingQuestionAsync(attempt, questionId, cancellationToken);
        var decision = PlacementRecordingRule.Decide(response.RecordingInterruptions, capturedMs, siblingId);

        response.RecordingInterruptions = decision.Interruptions;
        response.RecordingSessionToken = sessionToken;

        if (decision.Outcome != PlacementRecordingOutcome.Retry)
        {
            // Retries are over. The capture is kept exactly as it is, so "finalise" needs no write of
            // its own: the audio and its duration are already the durable facts.
            response.DraftSavedAt = DateTimeOffset.UtcNow;
        }

        await MarkPlacementActivityAsync(attempt, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return decision;
    }

    /// <summary>
    /// PLC-15's "unseen sibling from the same cell". The id of a same-skill, same-level sibling task
    /// with no answer yet.
    /// <para>
    /// <b>This is a legacy-paper rule, by design.</b> It matches on <c>Questions.CefrLevel</c>, so a
    /// pool-authored paper has no row here and the method returns null - which is the correct answer,
    /// not a failure: the caller's contract is "null means the paper carries no such sibling, so
    /// finalise rather than inventing a task". Adding a level lookup for the pool would mean a second
    /// way to pick a sibling, and #250's whole point is that a paper has one roster.
    /// </para>
    /// </summary>
    private async Task<int?> FindUnseenSiblingQuestionAsync(
        ExamAttempt attempt,
        int questionId,
        CancellationToken cancellationToken)
    {
        var current = await db.Questions
            .AsNoTracking()
            .Where(q => q.QuestionId == questionId && q.ExamId == attempt.ExamId)
            .Select(q => new { q.SkillId, q.CefrLevel })
            .SingleOrDefaultAsync(cancellationToken);

        if (current is null)
        {
            return null;
        }

        var drafted = await db.FreeResponses
            .Where(r => r.AttemptId == attempt.AttemptId
                && r.QuestionId != null
                && (!string.IsNullOrEmpty(r.ResponseText) || r.AudioPath != null))
            // #250: QuestionId is nullable now. This branch is about LEGACY questions - the fallback it
            // feeds is a query over Questions - so a pool response contributes nothing here.
            .Select(r => r.QuestionId!.Value)
            .ToListAsync(cancellationToken);
        var answered = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.AttemptId
                && a.SelectedOptionId != null
                && a.QuestionId != null)
            // #250: QuestionId is nullable now, so the projection yields int? and a pool answer would
            // contribute a NULL here. This branch is about LEGACY questions, so the nulls are dropped
            // rather than carried into a set of question ids.
            .Select(a => a.QuestionId!.Value)
            .ToListAsync(cancellationToken);

        var seen = drafted.Union(answered).ToHashSet();

        return await db.Questions
            .AsNoTracking()
            .Where(q => q.ExamId == attempt.ExamId
                && q.QuestionId != questionId
                && q.SkillId == current.SkillId
                && q.CefrLevel == current.CefrLevel
                && !seen.Contains(q.QuestionId))
            .OrderBy(q => q.DisplayOrder)
            .Select(q => (int?)q.QuestionId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
