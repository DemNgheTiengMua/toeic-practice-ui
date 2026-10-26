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

        var question = await db.Questions
            .Where(q => q.QuestionId == questionId && q.ExamId == attempt.ExamId)
            .Select(q => new
            {
                q.SkillId,
                q.GroupId,
                q.SectionId,
                UnitNumber = q.Group.UnitNumber,
                NavigationMode = q.Section.NavigationMode
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question {questionId} not found in attempt.");

        if (attempt.ActiveSkillId.HasValue && question.SkillId != attempt.ActiveSkillId.Value)
        {
            throw new AttemptEngineException(
                AttemptEngineException.WrongPhase,
                "This question is not in the active exam skill.");
        }

        if (question.UnitNumber != attempt.ActiveUnitNumber)
        {
            throw new AttemptEngineException(
                AttemptEngineException.WrongPhase,
                "This question belongs to a locked unit.");
        }

        if (string.Equals(question.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase))
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
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && (userId == null || a.UserId == userId), cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        var examSkills = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId)
            .Select(q => q.Section.Skill)
            .Distinct()
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

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

        var firstSectionAndGroup = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId && q.Section.SkillId == nextSkill.SkillId)
            .OrderBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .Select(q => new
            {
                q.Section,
                q.Group,
                q.QuestionId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (firstSectionAndGroup != null && string.Equals(firstSectionAndGroup.Section.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            attempt.ActiveGroupId = firstSectionAndGroup.Group.GroupId;
        }
        else
        {
            attempt.ActiveGroupId = null;
        }

        var now = DateTimeOffset.UtcNow;
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

        if (firstSectionAndGroup != null)
        {
            attempt.LastQuestionId = firstSectionAndGroup.QuestionId;
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
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && (userId == null || a.UserId == userId), cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        if (targetUnitNumber <= attempt.ActiveUnitNumber)
        {
            throw new AttemptEngineException(AttemptEngineException.WrongPhase, "Cannot navigate back to a locked unit.");
        }

        attempt.ActiveUnitNumber = targetUnitNumber;

        var firstUnitQuestionId = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId && q.Section.SkillId == attempt.ActiveSkillId && q.Group.UnitNumber == targetUnitNumber)
            .OrderBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .Select(q => (int?)q.QuestionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstUnitQuestionId.HasValue)
        {
            attempt.LastQuestionId = firstUnitQuestionId.Value;
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
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && (userId == null || a.UserId == userId), cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        var targetGroup = await db.QuestionGroups
            .Include(g => g.Section)
            .SingleOrDefaultAsync(g => g.GroupId == targetGroupId && g.ExamId == attempt.ExamId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question group {targetGroupId} not found.");

        if (attempt.ActiveSkillId.HasValue && targetGroup.Section.SkillId != attempt.ActiveSkillId.Value)
        {
            throw new AttemptEngineException(AttemptEngineException.WrongPhase, "Target group belongs to a different skill.");
        }

        attempt.ActiveGroupId = targetGroupId;

        int subTimerSeconds = targetGroup.ResponseSeconds
            ?? await db.Questions.Where(q => q.GroupId == targetGroupId && q.ResponseSeconds.HasValue).Select(q => q.ResponseSeconds!.Value).FirstOrDefaultAsync(cancellationToken);
        if (subTimerSeconds <= 0)
        {
            subTimerSeconds = 600;
        }

        attempt.PhaseDeadlineAt = DateTimeOffset.UtcNow.UtcDateTime.AddSeconds(subTimerSeconds);

        var firstGroupQuestionId = await db.Questions
            .Where(q => q.GroupId == targetGroupId)
            .OrderBy(q => q.DisplayOrder)
            .Select(q => (int?)q.QuestionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstGroupQuestionId.HasValue)
        {
            attempt.LastQuestionId = firstGroupQuestionId.Value;
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

    public async Task<AttemptTransitionResult> CompleteAudioAndStartReviewAsync(
        int attemptId,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && (userId == null || a.UserId == userId), cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        var section = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId && (attempt.ActiveSkillId == null || q.Section.SkillId == attempt.ActiveSkillId.Value))
            .Select(q => q.Section)
            .FirstOrDefaultAsync(cancellationToken);

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
