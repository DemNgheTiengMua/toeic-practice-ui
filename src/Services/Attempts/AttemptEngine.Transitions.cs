using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    private async Task ValidateQuestionSittingStateAsync(
        ExamAttempt attempt,
        int questionId,
        CancellationToken cancellationToken)
    {
        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);
        if (phaseDeadline.HasValue && DateTimeOffset.UtcNow >= phaseDeadline.Value)
        {
            throw new AttemptEngineException(
                AttemptEngineException.AttemptExpired,
                "Exam phase time has expired.");
        }

        // #250: the sitting state of a position is a property of the PAPER, not of a question row. A
        // pool answer names an ExamItemId, which has no Questions row to look up - so this resolves
        // through the same seam the projection uses. Measured: before this, saving an answer on a
        // pool-only paper failed with "Question 1 not found in attempt" even though the paper had three
        // positions, because the lookup was a query on db.Questions.
        var questions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);
        var question = questions.FirstOrDefault(q => q.Id == questionId)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question {questionId} not found in attempt.");

        if (attempt.ActiveSkillId.HasValue && question.Section.SkillId != attempt.ActiveSkillId.Value)
        {
            throw new AttemptEngineException(
                AttemptEngineException.WrongPhase,
                "This question is not in the active exam skill.");
        }

        if ((question.Group.UnitNumber ?? 1) != attempt.ActiveUnitNumber)
        {
            throw new AttemptEngineException(
                AttemptEngineException.WrongPhase,
                "This question belongs to a locked unit.");
        }

        if (string.Equals(question.Section.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            if (attempt.ActiveGroupId.HasValue && question.GroupId != attempt.ActiveGroupId.Value)
            {
                throw new AttemptEngineException(
                    AttemptEngineException.WrongPhase,
                    "This question belongs to a locked question group.");
            }
        }
    }

    private Task EnsureQuestionInActivePhaseAsync(
        ExamAttempt attempt,
        int questionId,
        CancellationToken cancellationToken) =>
        ValidateQuestionSittingStateAsync(attempt, questionId, cancellationToken);

    public async Task<AttemptTransitionResult> TransitionToNextSkillAsync(
        int attemptId,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await LoadSittingAttemptAsync(db.ExamAttempts.Include(a => a.Exam), attemptId, userId, cancellationToken);

        // #250: the paper's skills and their order come from the seam. For a pool-only paper the old
        // query returned an empty list, so the transition immediately submitted the attempt instead of
        // moving to the next skill.
        var paperQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);
        var examSkills = paperQuestions
            .Select(q => q.Section.Skill)
            .DistinctBy(s => s.SkillId)
            .OrderBy(s => s.DisplayOrder)
            .ToList();

        int currentIndex = -1;
        if (attempt.ActiveSkillId.HasValue)
        {
            currentIndex = examSkills.FindIndex(s => s.SkillId == attempt.ActiveSkillId.Value);
        }

        int nextIndex = currentIndex + 1;
        if (nextIndex >= examSkills.Count)
        {
            var grading = await SubmitAttemptAsync(attempt.AttemptId, attempt.UserId, isAutoSubmit: false, cancellationToken);
            return new AttemptTransitionResult(
                attempt.AttemptId,
                attempt.ActiveSkillId,
                attempt.ActiveUnitNumber,
                attempt.ActiveGroupId,
                ToUtcOffset(attempt.PhaseDeadlineAt),
                0,
                EnumCodes.Parse<AttemptStatus>(grading.Attempt.Status));
        }

        var nextSkill = examSkills[nextIndex];
        attempt.ActiveSkillId = nextSkill.SkillId;
        attempt.ActiveUnitNumber = 1;

        // #250: the first position of the next skill comes from the paper the loader already returned,
        // so this does not re-query. A pool-only paper would have found nothing here and left
        // ActiveGroupId null on a HYBRID_GROUP_LOCKED paper, unlocking a group that should be locked.
        var firstSectionAndGroup = paperQuestions
            .FirstOrDefault(q => q.Section.SkillId == nextSkill.SkillId);

        if (firstSectionAndGroup != null && string.Equals(firstSectionAndGroup.Section.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            attempt.ActiveGroupId = firstSectionAndGroup.GroupId;
        }
        else
        {
            attempt.ActiveGroupId = null;
        }

        // #214 / PLC-12: a placement does not advance a clock when it advances a skill, so it gets no
        // phase deadline here. A mock does, exactly as before.
        var runsAClock = await RunsAClockAsync(attempt, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (runsAClock)
        {
            if (firstSectionAndGroup != null && string.Equals(firstSectionAndGroup.Section.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase) && firstSectionAndGroup.Group.ResponseSeconds.HasValue)
            {
                attempt.PhaseDeadlineAt = now.UtcDateTime.AddSeconds(firstSectionAndGroup.Group.ResponseSeconds.Value);
            }
            else if (nextSkill.DurationMinutes is int dur && dur > 0)
            {
                attempt.PhaseDeadlineAt = now.UtcDateTime.AddMinutes(dur);
            }
            else
            {
                attempt.PhaseDeadlineAt = attempt.ExpiresAt.UtcDateTime;
            }
        }

        if (firstSectionAndGroup != null)
        {
            RecordLastPosition(attempt, firstSectionAndGroup);
        }

        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - now).TotalSeconds);

        return new AttemptTransitionResult(
            attempt.AttemptId,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            ToUtcOffset(attempt.PhaseDeadlineAt),
            remainingSeconds,
            EnumCodes.Parse<AttemptStatus>(attempt.Status));
    }

    public async Task<AttemptTransitionResult> TransitionUnitAsync(
        int attemptId,
        int targetUnitNumber,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await LoadSittingAttemptAsync(db.ExamAttempts, attemptId, userId, cancellationToken);

        if (targetUnitNumber <= attempt.ActiveUnitNumber)
        {
            throw new AttemptEngineException(AttemptEngineException.WrongPhase, "Cannot navigate back to a locked unit.");
        }

        attempt.ActiveUnitNumber = targetUnitNumber;

        // #250: the paper's positions come from the seam, not from a Questions query. A pool-only
        // paper had no row here, so the unit transition silently left LastQuestionId pointing at the
        // previous unit's question.
        //
        // No re-sort: LoadSittingQuestionsAsync already returns sitting order (skill, section, then the
        // binding's own position), so the first match is the unit's first question.
        var paperQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);
        var firstUnitQuestion = paperQuestions
            .FirstOrDefault(q => q.Section.SkillId == attempt.ActiveSkillId
                && q.Group.UnitNumber == targetUnitNumber);

        if (firstUnitQuestion is not null)
        {
            RecordLastPosition(attempt, firstUnitQuestion);
        }

        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);

        return new AttemptTransitionResult(
            attempt.AttemptId,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            ToUtcOffset(attempt.PhaseDeadlineAt),
            remainingSeconds,
            EnumCodes.Parse<AttemptStatus>(attempt.Status));
    }

    public async Task<AttemptTransitionResult> TransitionGroupAsync(
        int attemptId,
        int targetGroupId,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await LoadSittingAttemptAsync(db.ExamAttempts, attemptId, userId, cancellationToken);

        // #269: a "group" is the block of positions a paper shares, and on the pool that block IS the
        // stimulus. A solo position has no block id, so a caller addresses it by its own negated position
        // id - the same convention the projection uses for GroupId.
        var paperQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);
        var groupQuestions = paperQuestions.Where(q => q.GroupId == targetGroupId).ToList();

        var firstGroupQuestion = groupQuestions.FirstOrDefault()
            ?? throw new AttemptEngineException(
                AttemptEngineException.QuestionNotFound, $"Question group {targetGroupId} not found.");

        if (attempt.ActiveSkillId.HasValue
            && firstGroupQuestion.Section.SkillId != attempt.ActiveSkillId.Value)
        {
            throw new AttemptEngineException(AttemptEngineException.WrongPhase, "Target group belongs to a different skill.");
        }

        attempt.ActiveGroupId = targetGroupId;
        // #269: on the pool a group belongs to a unit, so entering it enters that unit too. The legacy
        // shape carried UnitNumber on the group row but never moved the attempt with it, and a
        // UNIT_LOCKED paper then refused the very unit it had just transitioned into.
        attempt.ActiveUnitNumber = firstGroupQuestion.Group.UnitNumber ?? attempt.ActiveUnitNumber;

        // #214 / PLC-12: a placement has no clock, so entering a locked group starts no sub-timer.
        // A mock keeps its per-group window exactly as before.
        if (await RunsAClockAsync(attempt, cancellationToken))
        {
            int subTimerSeconds = firstGroupQuestion.Group.ResponseSeconds
                ?? groupQuestions.Where(q => q.ResponseSeconds.HasValue).Select(q => q.ResponseSeconds!.Value).FirstOrDefault();
            if (subTimerSeconds <= 0)
            {
                subTimerSeconds = 600;
            }

            attempt.PhaseDeadlineAt = DateTimeOffset.UtcNow.UtcDateTime.AddSeconds(subTimerSeconds);
        }

        // The group's first position in sitting order - the loader already sorted the paper.
        RecordLastPosition(attempt, firstGroupQuestion);

        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);

        return new AttemptTransitionResult(
            attempt.AttemptId,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            ToUtcOffset(attempt.PhaseDeadlineAt),
            remainingSeconds,
            EnumCodes.Parse<AttemptStatus>(attempt.Status));
    }

    public async Task<AttemptTransitionResult> CompleteAudioAndStartReviewAsync(
        int attemptId,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await LoadSittingAttemptAsync(db.ExamAttempts, attemptId, userId, cancellationToken);

        // #214 / PLC-12: the review window is a clock too, and a placement has none. This transition is
        // the listening half of a mock; a placement never asks for it, and refusing to stamp a deadline
        // here is what keeps that true if one ever did.
        if (!await RunsAClockAsync(attempt, cancellationToken))
        {
            return new AttemptTransitionResult(
                attempt.AttemptId,
                attempt.ActiveSkillId,
                attempt.ActiveUnitNumber,
                attempt.ActiveGroupId,
                ToUtcOffset(attempt.PhaseDeadlineAt),
                Math.Max(0, (int)(ResolveEffectiveDeadline(attempt) - DateTimeOffset.UtcNow).TotalSeconds),
                EnumCodes.Parse<AttemptStatus>(attempt.Status));
        }

        // #250: the section's timer policy is a property of the paper, so it comes from the seam.
        var paperQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);
        var section = paperQuestions
            .FirstOrDefault(q => attempt.ActiveSkillId == null || q.Section.SkillId == attempt.ActiveSkillId.Value)
            ?.Section;

        int checkTimeSec = 120;
        if (section?.TimerPolicyJson != null)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(section.TimerPolicyJson);
                if (doc.RootElement.TryGetProperty("reviewSeconds", out var revProp) && revProp.TryGetInt32(out var rev))
                {
                    checkTimeSec = rev;
                }
                else if (doc.RootElement.TryGetProperty("checkTimeSec", out var chkProp) && chkProp.TryGetInt32(out var chk))
                {
                    checkTimeSec = chk;
                }
            }
            catch
            {
                // fallback to default
            }
        }

        attempt.PhaseDeadlineAt = DateTimeOffset.UtcNow.UtcDateTime.AddSeconds(checkTimeSec);
        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);

        return new AttemptTransitionResult(
            attempt.AttemptId,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            ToUtcOffset(attempt.PhaseDeadlineAt),
            remainingSeconds,
            EnumCodes.Parse<AttemptStatus>(attempt.Status));
    }
}
