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
                // #216 / PLC-13: min(last_activity + 48 h, started_at + 72 h). Nothing has been
                // written yet, so the inactivity arm is measured from the start - which is why a
                // placement opens with 48 hours, not 72. The 72-hour arm is the cap every later
                // stamp is clamped against, never a deadline of its own.
                : PlacementSitting.ExpiresAt(now);
            var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(examId, cancellationToken);
            var requiresPreflight = requiresAudio || requiresMic;

            // #250: ONE source for the paper. LoadSittingQuestionsAsync resolves the roster when the
            // paper was authored on the pool and the legacy rows otherwise, and it has already applied
            // the pinned-version overlay and resolved the audio chain. Creation and the sitting
            // projection therefore cannot disagree about what the paper is - before this, creation read
            // Questions directly, so a pool-only paper was refused here with "that position holds no
            // question". PoolOnlyPaperTests pins that.
            var questions = await LoadSittingQuestionsAsync(examId, cancellationToken);

            var firstQuestion = questions.FirstOrDefault();
            var firstSkill = questions
                .OrderBy(q => q.Section.Skill.DisplayOrder)
                .FirstOrDefault();

            int? activeSkillId = firstSkill?.Section.SkillId;
            int activeUnitNumber = 1;
            int? activeGroupId = null;
            if (firstQuestion != null
                && string.Equals(firstQuestion.Section.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase))
            {
                activeGroupId = firstQuestion.GroupId;
            }

            DateTime? phaseDeadlineAt = null;
            if (!runsAClock)
            {
                // #214 / PLC-12: no overall countdown, so no phase clock either. The outer bound is the
                // only deadline, and it is enforced where every engine deadline is.
            }
            else if (firstQuestion != null
                && string.Equals(firstQuestion.Section.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase)
                && firstQuestion.Group.ResponseSeconds.HasValue)
            {
                phaseDeadlineAt = now.UtcDateTime.AddSeconds(firstQuestion.Group.ResponseSeconds.Value);
            }
            else if (firstSkill?.Section.Skill.DurationMinutes is int skillDur && skillDur > 0)
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
                RecordLastPosition(attempt, questions[0]);
                foreach (var q in questions)
                {
                    if (q.RenderFormat != QuestionType.FreeResponse.ToCode())
                    {
                        db.AttemptAnswers.Add(new AttemptAnswer
                        {
                            AttemptId = attempt.AttemptId,
                            // #250: a legacy position names its question; a pool position names the
                            // roster row it sits at. CK_AttemptAnswers_HasShape requires at least one,
                            // and only one of the two is ever set for a given paper.
                            QuestionId = q.LegacyQuestion is null ? null : q.Id,
                            ExamId = examId,
                            // #186: the row records the version, so a re-versioned or retired item
                            // cannot change what this attempt renders later.
                            ItemId = q.ItemId == 0 ? null : q.ItemId,
                            ItemVersionId = q.ItemVersionId,
                            ExamItemId = q.ExamItemId,
                            SelectedOptionId = null,
                            SelectedLabel = null,
                            IsCorrect = null,
                            IsFlagged = false
                        });
                    }
                    else
                    {
                        // #250 / #261: a productive task on a pool paper names its roster position, so
                        // the row is written with ExamItemId and no QuestionId. CK_FreeResp_HasShape
                        // requires at least one identity and the two filtered unique indexes keep one
                        // response per position, so this is the same shape the legacy write produced -
                        // addressed by the paper's own position instead of a question row.
                        db.FreeResponses.Add(new FreeResponse
                        {
                            AttemptId = attempt.AttemptId,
                            QuestionId = q.LegacyQuestion is null ? null : q.Id,
                            ExamItemId = q.ExamItemId,
                            ExamId = examId,
                            SkillId = q.Section.SkillId,
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
        var writesPosition = false;
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
            writesPosition = true;
        }

        if (request.CandidateNotesJson != null)
        {
            attempt.CandidateNotesJson = request.CandidateNotesJson;
            writesPosition = true;
        }

        if (request.LastQuestionId.HasValue)
        {
            // #250: the same seam the sitting projection reads, so a position means the same thing in
            // both places. Before this, sync resolved positions against Questions directly and a pool
            // paper's position could not be found at all.
            var orderedQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);

            var targetIdx = orderedQuestions.FindIndex(q => q.Id == request.LastQuestionId.Value);
            if (targetIdx >= 0 && !IsPhaseOrUnitOrGroupLocked(attempt, orderedQuestions[targetIdx]))
            {
                var currentIdx = attempt.LastQuestionId.HasValue
                    ? orderedQuestions.FindIndex(q => q.Id == attempt.LastQuestionId.Value)
                    : -1;
                var targetQuestion = orderedQuestions[targetIdx];
                var isBackwardInAudio = string.Equals(targetQuestion.Section.NavigationMode, "AUDIO_DRIVEN", StringComparison.OrdinalIgnoreCase)
                    && currentIdx >= 0
                    && targetIdx < GetFirstClusterIndex(orderedQuestions, currentIdx);

                if (!isBackwardInAudio)
                {
                    RecordLastPosition(attempt, targetQuestion);
                    writesPosition = true;
                }
            }
        }
        // #216 / PLC-13: only a write that actually carried something moves the inactivity clock. An
        // empty sync - what a poll or a reload sends - moves nothing, because a read must never
        // extend a sitting; refreshing would then be a way to keep a dead one alive.
        if (savedCount > 0 || writesPosition)
        {
            await MarkPlacementActivityAsync(attempt, cancellationToken);
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

        // #250: answers and free responses are keyed by whichever identity their paper uses - a legacy
        // position names QuestionId, a pool position names ExamItemId - so the sheet must look up both
        // rather than assume one.
        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.AttemptId)
            .ToListAsync(cancellationToken);
        var answerByQuestionId = answers
            .Where(a => a.QuestionId.HasValue)
            .ToDictionary(a => a.QuestionId!.Value);
        var answerByExamItemId = answers
            .Where(a => a.ExamItemId.HasValue)
            .ToDictionary(a => a.ExamItemId!.Value);

        var freeResponses = await db.FreeResponses
            .Where(fr => fr.AttemptId == attempt.AttemptId)
            .ToListAsync(cancellationToken);
        // #261: keyed twice, because a response names its position by whichever identity its paper
        // uses - keying by the nullable legacy column throws on the second pool row.
        var freeResponseByQuestionId = freeResponses
            .Where(fr => fr.QuestionId.HasValue)
            .ToDictionary(fr => fr.QuestionId!.Value);
        var freeResponseByExamItemId = freeResponses
            .Where(fr => fr.ExamItemId.HasValue)
            .ToDictionary(fr => fr.ExamItemId!.Value);

        // The paper's positions in order, from the seam - a pool-only paper has no Questions rows to
        // enumerate, and the old query returned an empty sheet for it.
        var paperQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);

        var answerSheet = paperQuestions.Select(q =>
        {
            var ans = q.ExamItemId is int examItemId
                ? answerByExamItemId.GetValueOrDefault(examItemId)
                : answerByQuestionId.GetValueOrDefault(q.Id);
            var fr = q.ExamItemId is int itemId
                ? freeResponseByExamItemId.GetValueOrDefault(itemId)
                : freeResponseByQuestionId.GetValueOrDefault(q.Id);
            return new AttemptAnswerSheetItem(
                q.Id,
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
