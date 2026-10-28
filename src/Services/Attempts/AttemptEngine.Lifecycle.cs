using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    public async Task<ExamAttempt> CreateAttemptAsync(
        int userId,
        int certId,
        int examId,
        int durationMinutes,
        CancellationToken cancellationToken = default)
    {
        var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.AddMinutes(durationMinutes);
            var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(examId, cancellationToken);
            var requiresPreflight = requiresAudio || requiresMic;

            var questions = await db.Questions
                .Where(q => q.ExamId == examId)
                .Include(q => q.Group)
                .Include(q => q.Section).ThenInclude(s => s.Skill)
                .OrderBy(q => q.Section.Skill.DisplayOrder)
                .ThenBy(q => q.Section.DisplayOrder)
                .ThenBy(q => q.Group.DisplayOrder)
                .ThenBy(q => q.DisplayOrder)
                .ToListAsync(cancellationToken);

            var firstSkill = questions
                .Select(q => q.Section.Skill)
                .OrderBy(s => s.DisplayOrder)
                .FirstOrDefault();

            var firstSection = questions.FirstOrDefault()?.Section;
            var firstGroup = questions.FirstOrDefault()?.Group;

            int? activeSkillId = firstSkill?.SkillId;
            int activeUnitNumber = 1;
            int? activeGroupId = null;
            if (firstSection != null && string.Equals(firstSection.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase))
            {
                activeGroupId = firstGroup?.GroupId;
            }

            DateTime? phaseDeadlineAt = null;
            if (firstSection != null && string.Equals(firstSection.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase) && firstGroup?.ResponseSeconds.HasValue == true)
            {
                phaseDeadlineAt = now.UtcDateTime.AddSeconds(firstGroup.ResponseSeconds.Value);
            }
            else if (firstSkill?.DurationMinutes is int skillDur && skillDur > 0)
            {
                phaseDeadlineAt = now.UtcDateTime.AddMinutes(skillDur);
            }
            else
            {
                phaseDeadlineAt = expiresAt.UtcDateTime;
            }

            string authorizationCode;
            do
            {
                authorizationCode = GenerateAuthorizationCode();
            } while (await db.ExamAttempts.AnyAsync(a => a.AuthorizationCode == authorizationCode, cancellationToken));

            var attempt = new ExamAttempt
            {
                UserId = userId,
                ExamId = examId,
                CertId = certId,
                AuthorizationCode = authorizationCode,
                Status = AttemptStatus.InProgress.ToCode(),
                StartedAt = now,
                ExpiresAt = expiresAt,
                ActiveSkillId = activeSkillId,
                ActiveUnitNumber = activeUnitNumber,
                ActiveGroupId = activeGroupId,
                PhaseDeadlineAt = phaseDeadlineAt,
                IsAutoSubmitted = false,
                PreflightVerifiedAt = requiresPreflight ? null : now
            };

            db.ExamAttempts.Add(attempt);
            await db.SaveChangesAsync(cancellationToken);

            if (questions.Count > 0)
            {
                attempt.LastQuestionId = questions[0].QuestionId;
                foreach (var q in questions)
                {
                    if (q.QuestionType != QuestionType.FreeResponse.ToCode())
                    {
                        db.AttemptAnswers.Add(new AttemptAnswer
                        {
                            AttemptId = attempt.AttemptId,
                            QuestionId = q.QuestionId,
                            ExamId = examId,
                            SelectedOptionId = null,
                            IsCorrect = null,
                            IsFlagged = false
                        });
                    }
                    else
                    {
                        db.FreeResponses.Add(new FreeResponse
                        {
                            AttemptId = attempt.AttemptId,
                            QuestionId = q.QuestionId,
                            ExamId = examId,
                            SkillId = q.SkillId,
                            QuestionType = QuestionType.FreeResponse.ToCode(),
                            ResponseText = string.Empty,
                            Status = FreeResponseStatus.Draft.ToCode(),
                            SubmittedAt = null,
                            DraftSavedAt = now
                        });
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);

            return attempt;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    public async Task<int> AutoSubmitExpiredAttemptsAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken = default)
    {
        var expiredAttempts = await db.ExamAttempts
            .Where(a => a.UserId == userId && a.CertId == certId && a.Status == AttemptStatus.InProgress.ToCode() && DateTimeOffset.UtcNow >= a.ExpiresAt)
            .Select(a => a.AttemptId)
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (var expiredId in expiredAttempts)
        {
            await SubmitAttemptAsync(expiredId, userId, isAutoSubmit: true, cancellationToken);
            count++;
        }

        return count;
    }

    public async Task CancelAttemptAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        attempt.Status = AttemptStatus.Abandoned.ToCode();
        await db.SaveChangesAsync(cancellationToken);
    }

    public static string GenerateAuthorizationCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return $"EX-{RandomNumberGenerator.GetString(chars, 6)}";
    }

    public async Task<SittingSyncResult> SaveSittingSyncAsync(
        int attemptId,
        SittingSyncRequest request,
        int? userId = null,
        Func<int, int, Task>? onAutoSubmitted = null,
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

        if (attempt.PreflightVerifiedAt == null)
        {
            await EnsurePreflightVerifiedAsync(attempt, cancellationToken);
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, attempt.UserId, isAutoSubmit: true, cancellationToken);
            if (onAutoSubmitted != null)
            {
                await onAutoSubmitted(attempt.AttemptId, attempt.UserId);
            }
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);
        if (phaseDeadline.HasValue && DateTimeOffset.UtcNow >= phaseDeadline.Value)
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam phase time has expired.");
        }

        int savedCount = 0;
        if (request.Answers != null && request.Answers.Count > 0)
        {
            foreach (var ans in request.Answers)
            {
                await SaveAnswerAsync(
                    attempt.AttemptId,
                    attempt.UserId,
                    ans.QuestionId,
                    ans.SelectedOptionId,
                    ans.IsFlagged,
                    onAutoSubmitted,
                    cancellationToken,
                    ans.ResponseText,
                    ans.SelectedOptionIdsJson);
                savedCount++;
            }
        }

        if (request.LastAudioOffsetMs.HasValue && request.LastAudioOffsetMs.Value >= 0)
        {
            attempt.LastAudioOffsetMs = request.LastAudioOffsetMs.Value;
        }

        if (request.CandidateNotesJson != null)
        {
            attempt.CandidateNotesJson = request.CandidateNotesJson;
        }

        if (request.LastQuestionId.HasValue)
        {
            var orderedQuestions = await db.Questions
                .Where(q => q.ExamId == attempt.ExamId)
                .Include(q => q.Group)
                .Include(q => q.Section).ThenInclude(s => s.Skill)
                .OrderBy(q => q.Section.Skill.DisplayOrder)
                .ThenBy(q => q.Section.DisplayOrder)
                .ThenBy(q => q.Group.DisplayOrder)
                .ThenBy(q => q.DisplayOrder)
                .ToListAsync(cancellationToken);

            var targetIdx = orderedQuestions.FindIndex(q => q.QuestionId == request.LastQuestionId.Value);
            if (targetIdx >= 0 && !IsPhaseOrUnitOrGroupLocked(attempt, orderedQuestions[targetIdx]))
            {
                var currentIdx = attempt.LastQuestionId.HasValue
                    ? orderedQuestions.FindIndex(q => q.QuestionId == attempt.LastQuestionId.Value)
                    : -1;
                var targetQuestion = orderedQuestions[targetIdx];
                var isBackwardInAudio = string.Equals(targetQuestion.Section.NavigationMode, "AUDIO_DRIVEN", StringComparison.OrdinalIgnoreCase)
                    && currentIdx >= 0
                    && targetIdx < GetFirstClusterIndex(orderedQuestions, currentIdx);

                if (!isBackwardInAudio)
                {
                    attempt.LastQuestionId = request.LastQuestionId.Value;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);

        return new SittingSyncResult(
            attempt.AttemptId,
            remainingSeconds,
            attempt.IsAutoSubmitted,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            phaseDeadline,
            attempt.LastAudioOffsetMs,
            savedCount);
    }

    public async Task<AttemptResumeView> ResumeAttemptByCodeAsync(
        string authorizationCode,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, "Authorization code is required.");
        }

        var normalizedCode = authorizationCode.Trim().ToUpperInvariant();
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .SingleOrDefaultAsync(a => a.AuthorizationCode == normalizedCode && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"No exam attempt found for authorization code '{authorizationCode}'.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "This exam attempt is no longer in progress.");
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.AttemptId)
            .ToDictionaryAsync(a => a.QuestionId, cancellationToken);

        var freeResponses = await db.FreeResponses
            .Where(fr => fr.AttemptId == attempt.AttemptId)
            .ToDictionaryAsync(fr => fr.QuestionId, cancellationToken);

        var questionIds = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId)
            .OrderBy(q => q.DisplayOrder)
            .Select(q => q.QuestionId)
            .ToListAsync(cancellationToken);

        var answerSheet = questionIds.Select(qId =>
        {
            answers.TryGetValue(qId, out var ans);
            freeResponses.TryGetValue(qId, out var fr);
            return new AttemptAnswerSheetItem(
                qId,
                ans?.SelectedOptionId,
                ans?.SelectedOptionIdsJson,
                fr?.ResponseText ?? ans?.ResponseText,
                ans?.IsFlagged ?? false);
        }).ToList();

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);
        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);

        return new AttemptResumeView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.AuthorizationCode ?? normalizedCode,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            attempt.LastQuestionId,
            attempt.LastAudioOffsetMs,
            attempt.CandidateNotesJson,
            remainingSeconds,
            attempt.ExpiresAt,
            phaseDeadline,
            answerSheet);
    }

    private static DateTimeOffset? ToUtcOffset(DateTime? dt) =>
        dt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc), TimeSpan.Zero) : null;

    private static DateTimeOffset ResolveEffectiveDeadline(ExamAttempt attempt)
    {
        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);
        return phaseDeadline.HasValue && phaseDeadline.Value < attempt.ExpiresAt
            ? phaseDeadline.Value
            : attempt.ExpiresAt;
    }
}
