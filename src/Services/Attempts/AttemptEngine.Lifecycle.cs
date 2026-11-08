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
            // #214 / PLC-12: a placement has no clock. Its deadline is the spec's outer bound
            // (started_at + 72 h), not the exam's own duration, and it runs no phase clock - the
            // per-skill deadline is what ended a placement when its first Listening budget ran out.
            //
            // FirstOrDefault, not Single: an unknown exam must still fail on the foreign key below,
            // as it always has. A Single here raised "Sequence contains no elements" first and changed
            // which exception the boundary test - and any caller - sees.
            var purpose = await db.Exams.AsNoTracking()
                .Where(e => e.ExamId == examId)
                .Select(e => e.Purpose)
                .FirstOrDefaultAsync(cancellationToken);
            var runsAClock = purpose is null || PlacementSitting.HasCountdown(purpose);
            var expiresAt = runsAClock
                ? now.AddMinutes(durationMinutes)
                : PlacementSitting.ExpiresAt(now);
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

            // #186: resolve the paper's bindings before anything is written. A binding whose position
            // holds no question is refused here rather than creating an attempt the learner cannot sit
            // to completion � and because this runs before the transaction's first insert, a refusal
            // leaves no attempt, no answer and no free-response draft behind.
            var bindings = await LoadPaperBindingsAsync(examId, cancellationToken);
            EnsureBindingsRender(questions, bindings);
            var boundByQuestion = PlaceBindings(questions, bindings);

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
            if (!runsAClock)
            {
                // #214 / PLC-12: no overall countdown, so no phase clock either. The outer bound is the
                // only deadline, and it is enforced where every engine deadline is.
            }
            else if (firstSection != null && string.Equals(firstSection.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase) && firstGroup?.ResponseSeconds.HasValue == true)
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
                    boundByQuestion.TryGetValue(q.QuestionId, out var bound);
                    if (q.QuestionType != QuestionType.FreeResponse.ToCode())
                    {
                        db.AttemptAnswers.Add(new AttemptAnswer
                        {
                            AttemptId = attempt.AttemptId,
                            QuestionId = q.QuestionId,
                            ExamId = examId,
                            // #186: the row records the version, so a re-versioned or retired item
                            // cannot change what this attempt renders later.
                            ItemId = bound?.ItemId,
                            ItemVersionId = bound?.ItemVersionId,
                            SelectedOptionId = null,
                            SelectedLabel = null,
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
        var attempt = await LoadSittingAttemptAsync(db.ExamAttempts, attemptId, userId, cancellationToken);

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
        var attempt = await LoadSittingAttemptAsync(db.ExamAttempts.Include(a => a.Exam), attemptId, userId, cancellationToken);

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
                ProjectedOptionId(ans),
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
