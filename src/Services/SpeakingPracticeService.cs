using System;
using System.Collections.Generic;
using System.IO;
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

public sealed class SpeakingPracticeService(
    CompanionDbContext db,
    ILearnerService learners,
    IFileStore fileStore,
    ICreditLedger? creditLedger = null) : ISpeakingPracticeService
{
    public const int SpeakingPracticeCreditCost = 1;
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db);

    /// <summary>
    /// #262: the paper's productive positions, from the one loader the sitting engine uses. This
    /// service enumerated an exam's tasks with a legacy query, which returns nothing for a paper
    /// authored on the pool - so the screen said "no questions found" for a paper that can be sat.
    /// </summary>
    private async Task<List<SittingQuestion>> LoadPaperPositionsAsync(int examId, CancellationToken cancellationToken) =>
        await new AttemptEngine(db, fileStore).LoadSittingQuestionsAsync(examId, cancellationToken);

    public async Task<SpeakingPracticeInstructionsView> GetInstructionsAsync(int userId, int examId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var exam = await db.Exams
            .AsNoTracking()
            .Where(e => e.ExamId == examId && e.CertId == certId && e.Purpose == ExamPurpose.SpeakingPractice.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .Include(e => e.Cert)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.ExamNotFound, $"Speaking practice exam {examId} not found.");

        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);

        var activeAttempt = await db.ExamAttempts
            .Where(a => a.UserId == userId && a.ExamId == examId && a.Status == AttemptStatus.InProgress.ToCode())
            .FirstOrDefaultAsync(cancellationToken);

        if (activeAttempt is not null && DateTimeOffset.UtcNow >= activeAttempt.ExpiresAt)
        {
            activeAttempt = null;
        }

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == certId && s.Modality == Modality.Speaking.ToCode())
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new SpeakingSectionInfo(
                s.SectionId,
                s.Code,
                s.Name,
                s.ExpectedQuestionCount,
                null,
                null,
                s.DisplayOrder))
            .ToListAsync(cancellationToken);

        // #239: a prior take is listed and re-openable, newest first, the active one included.
        var attempts = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.ExamId == examId)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .ThenByDescending(a => a.AttemptId)
            .Select(a => new PriorAttempt(
                a.AttemptId,
                a.Status,
                a.StartedAt,
                a.SubmittedAt,
                a.Status == AttemptStatus.Graded.ToCode()))
            .ToListAsync(cancellationToken);

        return new SpeakingPracticeInstructionsView(
            exam.ExamId,
            exam.CertId,
            exam.Cert.Code,
            exam.Name,
            exam.Description,
            exam.DurationMinutes,
            exam.QuestionCount ?? 0,
            SpeakingPracticeCreditCost,
            balance,
            activeAttempt?.AttemptId,
            sections)
        {
            Attempts = attempts
        };
    }

    public async Task<SpeakingAttemptInfo> StartAttemptAsync(int userId, int examId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var exam = await db.Exams
            .AsNoTracking()
            .Where(e => e.ExamId == examId && e.CertId == certId && e.Purpose == ExamPurpose.SpeakingPractice.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.ExamNotFound, $"Speaking practice exam {examId} not found.");

        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);

        if (balance < SpeakingPracticeCreditCost)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.InsufficientCredits, "You do not have enough credits to start speaking practice.");
        }

        var existingAttempt = await db.ExamAttempts
            .Where(a => a.UserId == userId && a.ExamId == examId && a.Status == AttemptStatus.InProgress.ToCode())
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (existingAttempt is not null)
        {
            if (now < existingAttempt.ExpiresAt)
            {
                var remaining = Math.Max(0, (int)(existingAttempt.ExpiresAt - now).TotalSeconds);
                return new SpeakingAttemptInfo(
                    existingAttempt.AttemptId,
                    existingAttempt.ExamId,
                    EnumCodes.Parse<AttemptStatus>(existingAttempt.Status),
                    existingAttempt.StartedAt,
                    existingAttempt.ExpiresAt,
                    remaining,
                    existingAttempt.IsAutoSubmitted);
            }

            existingAttempt.Status = AttemptStatus.Abandoned.ToCode();
            await db.SaveChangesAsync(cancellationToken);
        }

        var expiresAt = now.AddMinutes(exam.DurationMinutes);
        var attempt = new ExamAttempt
        {
            UserId = userId,
            ExamId = examId,
            CertId = certId,
            Status = AttemptStatus.InProgress.ToCode(),
            StartedAt = now,
            ExpiresAt = expiresAt,
            IsAutoSubmitted = false,
            TotalScore = null, // UI-45 / trg_Attempts_NoTotalScoreForSkillPractice
            BandCode = null    // E1.3 (#148): the estimate is per grading, on AiGradings.BandCode
        };

        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);

        // Pre-populate empty draft free responses for the paper's productive tasks.
        // #262: the paper is the source, not the legacy question table - a pool-authored paper has no
        // question rows, and the old query wrote no drafts for it.
        var paperPositions = await LoadPaperPositionsAsync(examId, cancellationToken);
        // #269: only this service's own modality. Both writing and speaking render as `free_response`,
        // so the format alone does not say which route may serve the paper.
        var questions = paperPositions
            .Where(p => p.RenderFormat == QuestionType.FreeResponse.ToCode()
                && string.Equals(p.Section.Modality, Modality.Speaking.ToCode(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var q in questions)
        {
            var fr = new FreeResponse
            {
                AttemptId = attempt.AttemptId,
                QuestionId = q.ExamItemId is null ? q.Id : null,
                ExamItemId = q.ExamItemId,
                ExamId = examId,
                SkillId = q.Section.SkillId,
                QuestionType = QuestionType.FreeResponse.ToCode(),
                ResponseText = string.Empty,
                AudioPath = null,
                DurationMs = null,
                Status = FreeResponseStatus.Draft.ToCode(),
                DraftSavedAt = now,
                SubmittedAt = null
            };
            db.FreeResponses.Add(fr);
        }

        await db.SaveChangesAsync(cancellationToken);

        var remainingSeconds = Math.Max(0, (int)(expiresAt - now).TotalSeconds);
        return new SpeakingAttemptInfo(
            attempt.AttemptId,
            attempt.ExamId,
            AttemptStatus.InProgress,
            attempt.StartedAt,
            attempt.ExpiresAt,
            remainingSeconds,
            attempt.IsAutoSubmitted);
    }

    public async Task<SpeakingTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotActive, $"Attempt {attemptId} is not in progress.");
        }

        var now = DateTimeOffset.UtcNow;
        if (now >= attempt.ExpiresAt)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.AttemptExpired, $"Attempt {attemptId} has expired.");
        }

        // #262: the paper is the source. A pool-authored paper has no question rows, and the old query
        // returned nothing for it, so the screen said "no questions found" for a paper that can be sat.
        var paperPositions = await LoadPaperPositionsAsync(attempt.ExamId, cancellationToken);
        var questions = paperPositions
            .Where(p => p.RenderFormat == QuestionType.FreeResponse.ToCode()
                && string.Equals(p.Section.Modality, Modality.Speaking.ToCode(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (questions.Count == 0)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.QuestionNotFound, "No questions found for this exam.");
        }

        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Where(fr => fr.AttemptId == attemptId)
            .ToListAsync(cancellationToken);

        // #262: a response names its position by whichever identity its paper uses.
        FreeResponse? ResponseFor(SittingQuestion q) => q.ExamItemId is int examItemId
            ? freeResponses.FirstOrDefault(fr => fr.ExamItemId == examItemId)
            : freeResponses.FirstOrDefault(fr => fr.QuestionId == q.Id);

        var targetQuestion = questionId.HasValue
            ? questions.FirstOrDefault(q => q.Id == questionId.Value) ?? questions[0]
            : questions[0];

        var navList = questions.Select((q, idx) =>
        {
            var fr = ResponseFor(q);
            var isAnswered = fr != null && (!string.IsNullOrEmpty(fr.AudioPath) || !string.IsNullOrEmpty(fr.ResponseText));
            return new SpeakingQuestionNav(
                q.Id,
                idx + 1,
                q.Section.Code,
                q.Section.Name,
                isAnswered,
                q.Id == targetQuestion.Id);
        }).ToList();

        var currentFr = ResponseFor(targetQuestion);
        var savedCount = freeResponses.Count(r => !string.IsNullOrEmpty(r.AudioPath));

        // #152: a Read Aloud question shows the words it wants read. There is no reference for a
        // free-speech or picture question, so those carry none and are graded without one.
        // #262: the reference is carried by the position, so it resolves for a pool task too.
        var (referenceText, describesImage) = SpeakingTask.ResolveReference(
            targetQuestion.ReferenceText, targetQuestion.ReferenceKind);
        var readAloudText = SpeakingTask.IsScripted(referenceText, describesImage)
            ? SpeakingTask.PronunciationReference(referenceText, describesImage)
            : null;

        var targetIndex = questions.FindIndex(q => q.Id == targetQuestion.Id) + 1;

        var qView = new SpeakingQuestionView(
            targetQuestion.Id,
            targetIndex,
            targetQuestion.Stem,
            targetQuestion.PrepSeconds ?? 45,
            targetQuestion.ResponseSeconds ?? 30,
            targetQuestion.Hint,
            targetQuestion.Section.Code,
            targetQuestion.Section.Name,
            targetQuestion.Group?.ImagePath,
            targetQuestion.Group?.ImageAlt,
            targetQuestion.Group?.AudioPath,
            currentFr?.AudioPath,
            currentFr?.DurationMs,
            currentFr?.ResponseText,
            currentFr?.DraftSavedAt,
            currentFr != null ? EnumCodes.Parse<FreeResponseStatus>(currentFr.Status) : FreeResponseStatus.Draft,
            readAloudText);

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - now).TotalSeconds);

        return new SpeakingTakeView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam.Name,
            attempt.CertId,
            attempt.Cert.Code,
            AttemptStatus.InProgress,
            attempt.StartedAt,
            attempt.ExpiresAt,
            remainingSeconds,
            targetQuestion.Id,
            targetIndex,
            questions.Count,
            savedCount,
            qView,
            navList);
    }

    public async Task<SaveSpeakingAudioResult> SaveAudioAsync(
        int userId,
        int attemptId,
        int questionId,
        Stream stream,
        string contentType,
        string fileName,
        int? durationMs,
        CancellationToken cancellationToken = default)
    {
        var (isValid, error) = AudioValidation.Validate(stream.Length, contentType, fileName);
        if (!isValid)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.InvalidAudio, error ?? "Invalid audio file.");
        }

        var attempt = await db.ExamAttempts
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotActive, $"Attempt {attemptId} is not in progress.");
        }

        var now = DateTimeOffset.UtcNow;
        if (now >= attempt.ExpiresAt)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.AttemptExpired, $"Attempt {attemptId} has expired.");
        }

        // #269: the position comes from the paper. A pool-authored speaking paper has no Questions rows,
        // so this query found nothing and every audio upload was refused with "question 36 not found in
        // this exam" - the whole speaking flow was unusable on the pool. The same roster read the take
        // page uses answers it.
        var positions = await LoadPaperPositionsAsync(attempt.ExamId, cancellationToken);
        var position = positions
            .FirstOrDefault(p => p.Id == questionId
                && p.RenderFormat == QuestionType.FreeResponse.ToCode()
                && string.Equals(p.Section.Modality, Modality.Speaking.ToCode(), StringComparison.OrdinalIgnoreCase))
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.QuestionNotFound, $"Speaking question {questionId} not found in this exam.");

        var question = position.LegacyQuestion;
        var responseSeconds = position.ResponseSeconds;

        // #239: the recording window is the TASK'S OWN declaration, and the server enforces it. The
        // composer stops the recorder at this second, but a client that ignores the countdown would
        // otherwise store a longer take and the window would exist only in JavaScript. A question that
        // declares no window is not refused: the column is nullable, and refusing an undeclared window
        // would invent a limit no author wrote. The boundary is inclusive, because a recorder that
        // stops ON the last second saves exactly the window.
        //
        // shortcut: the duration is the recorder's own report, so a modified client can understate it.
        // Closing that needs the audio decoded server-side (ffprobe or an audio library), which is a
        // dependency this ticket does not add; upgrade when a stored take's length becomes a scored fact.
        var windowMs = responseSeconds is int declared ? declared * 1000 : (int?)null;
        if (windowMs is int limit && durationMs is int recorded && recorded > limit)
        {
            throw new SpeakingPracticeException(
                SpeakingPracticeException.AudioTooLong,
                $"This task records for {responseSeconds}s; the take is {recorded / 1000.0:0.#}s. It was not saved.");
        }

        var ext = Path.GetExtension(fileName) ?? ".webm";
        if (fileName?.Contains(FakeAiGrader.SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true)
        {
            ext = $"{FakeAiGrader.SimulateFailMarker}{ext}";
        }
        var relativePath = await fileStore.SaveAudioAsync(attemptId, questionId, stream, contentType, ext, cancellationToken);

        var fr = await db.FreeResponses
            .Where(r => r.AttemptId == attemptId
                && (position.ExamItemId.HasValue
                    ? r.ExamItemId == position.ExamItemId
                    : r.QuestionId == questionId))
            .SingleOrDefaultAsync(cancellationToken);

        if (fr is null)
        {
            fr = new FreeResponse
            {
                AttemptId = attemptId,
                // A pool response names the roster position and carries no QuestionId at all.
                QuestionId = position.ExamItemId.HasValue ? null : questionId,
                ExamItemId = position.ExamItemId,
                ExamId = attempt.ExamId,
                SkillId = position.Section.SkillId,
                QuestionType = QuestionType.FreeResponse.ToCode(),
                AudioPath = relativePath,
                DurationMs = durationMs,
                Status = FreeResponseStatus.Draft.ToCode(),
                DraftSavedAt = now,
                SubmittedAt = null
            };
            db.FreeResponses.Add(fr);
        }
        else
        {
            var oldAudioPath = fr.AudioPath;
            fr.AudioPath = relativePath;
            fr.DurationMs = durationMs;
            fr.Status = FreeResponseStatus.Draft.ToCode();
            fr.DraftSavedAt = now;
            fr.SubmittedAt = null;

            if (!string.IsNullOrWhiteSpace(oldAudioPath) && !string.Equals(oldAudioPath, relativePath, StringComparison.OrdinalIgnoreCase))
            {
                await fileStore.DeleteAudioAsync(oldAudioPath, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new SaveSpeakingAudioResult(
            fr.FreeResponseId,
            fr.QuestionId ?? fr.ExamItemId ?? 0,
            relativePath,
            durationMs,
            now);
    }

    public async Task<SubmitSpeakingResult> SubmitAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotActive, $"Attempt {attemptId} is not in progress.");
        }

        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);
        if (balance < SpeakingPracticeCreditCost)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.InsufficientCredits, "You do not have enough credits to submit speaking practice.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var freeResponses = await db.FreeResponses
            .Where(fr => fr.AttemptId == attemptId)
            .ToListAsync(cancellationToken);

        // Remove unrecorded draft questions (prototype: "Questions you have not recorded are not graded")
        var unrecordedDrafts = freeResponses.Where(r => string.IsNullOrEmpty(r.AudioPath)).ToList();
        if (unrecordedDrafts.Count > 0)
        {
            db.FreeResponses.RemoveRange(unrecordedDrafts);
            await db.SaveChangesAsync(cancellationToken);
            freeResponses = freeResponses.Except(unrecordedDrafts).ToList();
        }

        if (freeResponses.Count == 0)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.Validation, "At least one recording must be saved before submitting.");
        }

        var now = DateTimeOffset.UtcNow;

        // Transition free responses from draft to pending_ai
        foreach (var fr in freeResponses)
        {
            if (fr.Status == FreeResponseStatus.Draft.ToCode())
            {
                fr.Status = FreeResponseStatus.PendingAi.ToCode();
                fr.SubmittedAt = now;
            }
        }

        // Save free response status first so trg_Attempts_NoDraftsAfterSubmit and trg_AiGradings_NotOnDraft pass
        await db.SaveChangesAsync(cancellationToken);

        attempt.Status = AttemptStatus.Submitted.ToCode();
        attempt.SubmittedAt = now;
        attempt.TotalScore = null; // trg_Attempts_NoTotalScoreForSkillPractice (50041)
        attempt.BandCode = null;   // E1.3 (#148): the estimate is per grading, on AiGradings.BandCode

        await db.SaveChangesAsync(cancellationToken);

        // Charge 1 credit via CreditLedger: Reason='ai_grading', Delta=-1
        try
        {
            await _creditLedger.ChargeAiGradingAsync(userId, attemptId, SpeakingPracticeCreditCost, cancellationToken);
        }
        catch (CreditLedgerException ex) when (ex.Code == CreditLedgerException.InsufficientCredits)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.InsufficientCredits, "You do not have enough credits to submit speaking practice.");
        }

        // Queue one AiGradings row per free response
        foreach (var fr in freeResponses)
        {
            var existingGrading = await db.AiGradings
                .Where(g => g.FreeResponseId == fr.FreeResponseId)
                .SingleOrDefaultAsync(cancellationToken);

            if (existingGrading is null)
            {
                db.AiGradings.Add(new AiGrading
                {
                    FreeResponseId = fr.FreeResponseId,
                    SkillId = fr.SkillId,
                    Status = AiGradingStatus.Pending.ToCode(),
                    AttemptCount = 1,
                    RequestedAt = now
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new SubmitSpeakingResult(
            attempt.AttemptId,
            AttemptStatus.Submitted,
            now);
    }

    public async Task<SpeakingGradingPendingView> GetGradingPendingAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        var gradings = await db.AiGradings
            .AsNoTracking()
            .Include(g => g.FreeResponse)
                .ThenInclude(fr => fr.Question)
            .Where(g => g.FreeResponse.AttemptId == attemptId)
            .ToListAsync(cancellationToken);

        // #269: the same reader as writing's - a pool response has no QuestionId, so `QuestionId!.Value`
        // threw and the grading-status page could not load after a successful grading.
        var positions = await LoadPaperPositionsAsync(attempt.ExamId, cancellationToken);
        var positionByExamItemId = positions
            .Where(p => p.ExamItemId.HasValue)
            .ToDictionary(p => p.ExamItemId!.Value);

        int AddressOf(FreeResponse fr) => fr.QuestionId ?? fr.ExamItemId ?? 0;

        int OrderOf(FreeResponse fr) => fr.ExamItemId is int examItemId
            && positionByExamItemId.TryGetValue(examItemId, out var position)
                ? positions.IndexOf(position)
                : fr.Question?.DisplayOrder ?? 0;

        gradings = gradings.OrderBy(g => OrderOf(g.FreeResponse)).ToList();

        var total = gradings.Count;
        var graded = gradings.Count(g => g.Status == AiGradingStatus.Done.ToCode());
        var failed = gradings.Count(g => g.Status == AiGradingStatus.Failed.ToCode());
        var pending = gradings.Count(g => g.Status == AiGradingStatus.Pending.ToCode());
        var canRetry = failed > 0;
        var failureReason = gradings.FirstOrDefault(g => g.Status == AiGradingStatus.Failed.ToCode())?.FailureReason;

        var items = gradings.Select(g => new FreeResponseGradingStatus(
            g.FreeResponseId,
            AddressOf(g.FreeResponse),
            OrderOf(g.FreeResponse),
            EnumCodes.Parse<AiGradingStatus>(g.Status),
            g.AttemptCount,
            g.FailureReason)).ToList();

        return new SpeakingGradingPendingView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam.Name,
            attempt.Cert.Code,
            EnumCodes.Parse<AttemptStatus>(attempt.Status),
            total,
            graded,
            failed,
            pending,
            canRetry,
            failureReason,
            items);
    }

    public async Task RetryGradingAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        var failedGradings = await db.AiGradings
            .Where(g => g.FreeResponse.AttemptId == attemptId && g.Status == AiGradingStatus.Failed.ToCode())
            .ToListAsync(cancellationToken);

        if (failedGradings.Count == 0)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.RetryNotAllowed, "No failed gradings found to retry.");
        }

        foreach (var g in failedGradings)
        {
            g.Status = AiGradingStatus.Pending.ToCode();
            g.AttemptCount += 1;
            g.FailureReason = null;
            g.CompletedAt = null;
        }

        if (attempt.Status != AttemptStatus.Submitted.ToCode())
        {
            attempt.Status = AttemptStatus.Submitted.ToCode();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SpeakingReviewView> GetAttemptReviewAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.Graded.ToCode())
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotFinished, $"Attempt {attemptId} is not graded yet.");
        }

        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Where(fr => fr.AttemptId == attemptId)
            .Include(fr => fr.Question)
                .ThenInclude(q => q.Section)
            .Include(fr => fr.AiGradingFreeResponse)
                .ThenInclude(g => g!.AiGradingScores)
                    .ThenInclude(s => s.GradingCriterion)
            .ToListAsync(cancellationToken);

        // #269: the review page reads the same address, order, section and stem for a pool task as for a
        // legacy one. Reading fr.Question for a pool response threw on QuestionId!.Value.
        var reviewPositions = await LoadPaperPositionsAsync(attempt.ExamId, cancellationToken);
        var reviewPositionByExamItemId = reviewPositions
            .Where(p => p.ExamItemId.HasValue)
            .ToDictionary(p => p.ExamItemId!.Value);

        int ReviewAddressOf(FreeResponse fr) => fr.QuestionId ?? fr.ExamItemId ?? 0;

        // A sitting order is 1-based, the way the navigator and every other screen numbers it. Using
        // IndexOf here reported the first task as displayOrder 0, which the review page rendered as
        // "task 0 of 3".
        int ReviewOrderOf(FreeResponse fr) => fr.ExamItemId is int examItemId
            && reviewPositionByExamItemId.TryGetValue(examItemId, out var pos)
                ? reviewPositions.IndexOf(pos) + 1
                : fr.Question?.DisplayOrder ?? 0;

        int ReviewSectionOf(FreeResponse fr) => fr.ExamItemId is int examItemId
            && reviewPositionByExamItemId.TryGetValue(examItemId, out var pos)
                ? pos.SectionId
                : fr.Question?.SectionId ?? 0;

        freeResponses = freeResponses.OrderBy(ReviewOrderOf).ToList();

        var totalExamQuestions = reviewPositions
            .Count(p => p.RenderFormat == QuestionType.FreeResponse.ToCode()
                && string.Equals(p.Section.Modality, Modality.Speaking.ToCode(), StringComparison.OrdinalIgnoreCase));

        var questionItems = new List<SpeakingQuestionReviewItem>();
        decimal overallTotalScore = 0m;
        decimal overallMaxScore = 0m;
        int gradedCount = 0;

        foreach (var fr in freeResponses)
        {
            var grading = fr.AiGradingFreeResponse;
            var scores = grading?.AiGradingScores ?? (IEnumerable<AiGradingScore>)Array.Empty<AiGradingScore>();

            // Load applicable SectionCriteria to know the maximums
            var reviewSectionId = ReviewSectionOf(fr);
            var sectionCriteria = reviewSectionId == 0
                ? []
                : await db.SectionCriteria
                    .AsNoTracking()
                    .Where(sc => sc.SectionId == reviewSectionId)
                    .ToListAsync(cancellationToken);

            var criteriaItems = new List<SpeakingCriterionScoreItem>();
            decimal qTotal = 0m;
            decimal qMax = 0m;

            if (grading?.AiGradingScores is { Count: > 0 } && !string.IsNullOrEmpty(fr.AudioPath)) gradedCount++;

            foreach (var s in scores.OrderBy(s => s.GradingCriterion.DisplayOrder))
            {
                var sc = sectionCriteria.FirstOrDefault(x => x.CriterionId == s.CriterionId);
                var max = sc?.MaxScore ?? s.GradingCriterion.MaxScore;

                criteriaItems.Add(new SpeakingCriterionScoreItem(
                    s.CriterionId,
                    s.GradingCriterion.Code,
                    s.GradingCriterion.Name,
                    s.Score,
                    max,
                    s.Comment));

                qTotal += s.Score;
                qMax += max;
            }

            questionItems.Add(new SpeakingQuestionReviewItem(
                ReviewAddressOf(fr),
                ReviewOrderOf(fr),
                fr.ExamItemId is int reviewItemId && reviewPositionByExamItemId.TryGetValue(reviewItemId, out var reviewPos)
                    ? reviewPos.Section.Name
                    : fr.Question?.Section?.Name ?? string.Empty,
                fr.ExamItemId is int reviewStemId && reviewPositionByExamItemId.TryGetValue(reviewStemId, out var stemPos)
                    ? stemPos.Stem
                    : fr.Question?.Stem ?? string.Empty,
                fr.AudioPath,
                fr.DurationMs,
                fr.ResponseText,
                qTotal,
                qMax,
                criteriaItems));

            overallTotalScore += qTotal;
            overallMaxScore += qMax;
        }

        // Lowest scored question
        SpeakingQuestionReviewItem? lowest = null;
        if (questionItems.Count > 0)
        {
            lowest = questionItems
                .OrderBy(q => q.MaxScore > 0 ? q.TotalScore / q.MaxScore : 1)
                .ThenBy(q => q.DisplayOrder)
                .FirstOrDefault();
        }

        // #152: the rubric fraction is placed on this certificate's own axis, through the same
        // AttemptEngine call that decides the band. One placement, so the hero score and the band
        // cannot come from two different spans. A certificate with no bands reports no score.
        int? scaledScore = null;
        int? scaledScoreMax = null;
        var skillIds = freeResponses.Select(fr => fr.SkillId).Distinct().ToList();
        var skillId = skillIds.Count == 1 ? skillIds[0] : (int?)null;
        var displayFactor = await DisplayFactorAsync(attempt.CertId, cancellationToken);
        if (overallMaxScore > 0)
        {
            var placement = await AttemptEngine.PlaceRubricAsync(
                db, attempt.CertId, skillId, overallTotalScore, overallMaxScore, cancellationToken);
            if (placement is (int score, int top, _, _))
            {
                scaledScore = score;
                scaledScoreMax = top;
            }        }
        // E1.3 (#148): display the snapshot the grading worker stored, not a live recomputation — the
        // column and the page must be the same number. The worker stamps every grading of the attempt
        // with the attempt-level estimate, so any one of them carries it. A row graded before this
        // existed has no snapshot and nothing else is authoritative, so only then compute it.
        var estimatedCode = freeResponses
            .Select(fr => fr.AiGradingFreeResponse?.BandCode)
            .FirstOrDefault(code => !string.IsNullOrEmpty(code));
        if (estimatedCode is null)
        {
            estimatedCode = (await AttemptEngine.EstimateBandForAttemptAsync(
                db, attempt.AttemptId, attempt.CertId, cancellationToken))?.Code;
        }

        var estimatedName = estimatedCode is null
            ? null
            : await db.ScoreBands
                .AsNoTracking()
                .Where(b => b.CertId == attempt.CertId && b.Code == estimatedCode)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(cancellationToken);

        return new SpeakingReviewView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam.Name,
            attempt.Cert.Code,
            attempt.SubmittedAt,
            totalExamQuestions,
            freeResponses.Count,
            overallTotalScore,
            overallMaxScore,
            scaledScore,
            questionItems,
            lowest,
            estimatedCode,
            estimatedName,
            scaledScoreMax,
            displayFactor)
        {
            // #239: "one task is not a level" counts GRADED tasks, not recorded ones.
            GradedQuestions = gradedCount
        };
    }

    /// <summary>
    /// #152: what one stored unit is worth to the learner. The speaking axis is stored in tenths on
    /// an <c>avg_half_step</c> certificate, so 75 is 7.5 and a page that prints the stored integer is
    /// wrong by a factor of ten. <see cref="AttemptEngine.DisplayFactor"/> is the one rule; this only
    /// tells it which certificate's rule to read.
    /// </summary>
    private async Task<int> DisplayFactorAsync(int certId, CancellationToken cancellationToken)
    {
        var rule = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => c.ScoreAggregationRule)
            .SingleAsync(cancellationToken);
        return AttemptEngine.DisplayFactor(rule);
    }
    public async Task<FileStreamResultInfo?> GetAudioFileAsync(int userId, int attemptId, int questionId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.UserId != userId)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.Forbidden, "You do not have permission to access audio for this attempt.");
        }

        // #269: the address is a POSITION - a question id on a legacy paper, an ExamItemId on a pool one.
        // Matching on QuestionId alone left a pool recording unanswered, so a learner could not hear back
        // the take they had just made: "Audio recording not found for this question".
        var fr = await db.FreeResponses
            .AsNoTracking()
            .Where(r => r.AttemptId == attemptId
                && (r.QuestionId == questionId || r.ExamItemId == questionId))
            .SingleOrDefaultAsync(cancellationToken);

        if (fr is null || string.IsNullOrWhiteSpace(fr.AudioPath))
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.AudioNotFound, "Audio recording not found for this question.");
        }

        var result = await fileStore.GetAudioAsync(fr.AudioPath, cancellationToken);
        if (result is null)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.AudioNotFound, "Audio file not found in storage.");
        }

        return result;
    }
}
