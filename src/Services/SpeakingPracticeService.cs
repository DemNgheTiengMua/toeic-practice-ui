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

public class SpeakingPracticeException(string code, string message) : Exception(message)
{
    public const string InsufficientCredits = "insufficient_credits";
    public const string ExamNotFound = "exam_not_found";
    public const string AttemptNotFound = "attempt_not_found";
    public const string AttemptNotActive = "attempt_not_active";
    public const string AttemptExpired = "attempt_expired";
    public const string QuestionNotFound = "question_not_found";
    public const string AttemptNotFinished = "attempt_not_finished";
    public const string RetryNotAllowed = "retry_not_allowed";
    public const string InvalidAudio = "invalid_audio";
    public const string Forbidden = "forbidden";
    public const string AudioNotFound = "audio_not_found";
    public const string Validation = "validation";

    public string Code { get; } = code;
}

public interface ISpeakingPracticeService
{
    Task<SpeakingPracticeInstructionsView> GetInstructionsAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<SpeakingAttemptInfo> StartAttemptAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<SpeakingTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionId, CancellationToken cancellationToken = default);

    Task<SaveSpeakingAudioResult> SaveAudioAsync(int userId, int attemptId, int questionId, Stream stream, string contentType, string fileName, int? durationMs, CancellationToken cancellationToken = default);

    Task<SubmitSpeakingResult> SubmitAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<SpeakingGradingPendingView> GetGradingPendingAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task RetryGradingAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<SpeakingReviewView> GetAttemptReviewAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<FileStreamResultInfo?> GetAudioFileAsync(int userId, int attemptId, int questionId, CancellationToken cancellationToken = default);
}

public sealed class SpeakingPracticeService(
    CompanionDbContext db,
    ILearnerService learners,
    IFileStore fileStore,
    ICreditLedger? creditLedger = null) : ISpeakingPracticeService
{
    public const int SpeakingPracticeCreditCost = 1;
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db);

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
                s.ExpectedQuestionCount ?? 0,
                null,
                null,
                s.DisplayOrder))
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
            sections);
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
            TotalScore = null, // UI-45 / trg_Attempts_NoBandForSpeakingPractice
            BandCode = null    // UI-38 / trg_Attempts_NoBandForSpeakingPractice
        };

        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);

        // Pre-populate empty draft free responses for questions so they are linked
        var questions = await db.Questions
            .Where(q => q.ExamId == examId && q.QuestionType == QuestionType.FreeResponse.ToCode())
            .OrderBy(q => q.DisplayOrder)
            .ToListAsync(cancellationToken);

        foreach (var q in questions)
        {
            var fr = new FreeResponse
            {
                AttemptId = attempt.AttemptId,
                QuestionId = q.QuestionId,
                ExamId = examId,
                SkillId = q.SkillId,
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

        var questions = await db.Questions
            .AsNoTracking()
            .Where(q => q.ExamId == attempt.ExamId && q.QuestionType == QuestionType.FreeResponse.ToCode())
            .Include(q => q.Section)
            .Include(q => q.Group)
            .OrderBy(q => q.DisplayOrder)
            .ToListAsync(cancellationToken);

        if (questions.Count == 0)
        {
            throw new SpeakingPracticeException(SpeakingPracticeException.QuestionNotFound, "No questions found for this exam.");
        }

        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Where(fr => fr.AttemptId == attemptId)
            .ToListAsync(cancellationToken);

        var targetQuestion = questionId.HasValue
            ? questions.FirstOrDefault(q => q.QuestionId == questionId.Value) ?? questions[0]
            : (attempt.LastQuestionId.HasValue
                ? questions.FirstOrDefault(q => q.QuestionId == attempt.LastQuestionId.Value) ?? questions[0]
                : questions[0]);

        if (attempt.LastQuestionId != targetQuestion.QuestionId)
        {
            attempt.LastQuestionId = targetQuestion.QuestionId;
            await db.SaveChangesAsync(cancellationToken);
        }

        var navList = questions.Select(q =>
        {
            var fr = freeResponses.FirstOrDefault(r => r.QuestionId == q.QuestionId);
            var isAnswered = fr != null && (!string.IsNullOrEmpty(fr.AudioPath) || !string.IsNullOrEmpty(fr.ResponseText));
            return new SpeakingQuestionNav(
                q.QuestionId,
                q.DisplayOrder,
                q.Section.Code,
                q.Section.Name,
                isAnswered,
                q.QuestionId == targetQuestion.QuestionId);
        }).ToList();

        var currentFr = freeResponses.FirstOrDefault(r => r.QuestionId == targetQuestion.QuestionId);
        var savedCount = freeResponses.Count(r => !string.IsNullOrEmpty(r.AudioPath));

        var qView = new SpeakingQuestionView(
            targetQuestion.QuestionId,
            targetQuestion.DisplayOrder,
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
            currentFr != null ? EnumCodes.Parse<FreeResponseStatus>(currentFr.Status) : FreeResponseStatus.Draft);

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - now).TotalSeconds);
        var targetIndex = questions.FindIndex(q => q.QuestionId == targetQuestion.QuestionId) + 1;

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
            targetQuestion.QuestionId,
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

        var question = await db.Questions
            .Where(q => q.QuestionId == questionId
                && q.ExamId == attempt.ExamId
                && q.QuestionType == QuestionType.FreeResponse.ToCode()
                && q.Section.Modality == Modality.Speaking.ToCode())
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpeakingPracticeException(SpeakingPracticeException.QuestionNotFound, $"Speaking question {questionId} not found in this exam.");

        var ext = Path.GetExtension(fileName) ?? ".webm";
        if (fileName?.Contains(FakeAiGrader.SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true)
        {
            ext = $"{FakeAiGrader.SimulateFailMarker}{ext}";
        }
        var relativePath = await fileStore.SaveAudioAsync(attemptId, questionId, stream, contentType, ext, cancellationToken);

        var fr = await db.FreeResponses
            .Where(r => r.AttemptId == attemptId && r.QuestionId == questionId)
            .SingleOrDefaultAsync(cancellationToken);

        if (fr is null)
        {
            fr = new FreeResponse
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                ExamId = attempt.ExamId,
                SkillId = question.SkillId,
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
            fr.QuestionId,
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
        attempt.TotalScore = null; // trg_Attempts_NoBandForSpeakingPractice (50041)
        attempt.BandCode = null;   // trg_Attempts_NoBandForSpeakingPractice (50032)

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
            .OrderBy(g => g.FreeResponse.Question.DisplayOrder)
            .ToListAsync(cancellationToken);

        var total = gradings.Count;
        var graded = gradings.Count(g => g.Status == AiGradingStatus.Done.ToCode());
        var failed = gradings.Count(g => g.Status == AiGradingStatus.Failed.ToCode());
        var pending = gradings.Count(g => g.Status == AiGradingStatus.Pending.ToCode());
        var canRetry = failed > 0;
        var failureReason = gradings.FirstOrDefault(g => g.Status == AiGradingStatus.Failed.ToCode())?.FailureReason;

        var items = gradings.Select(g => new FreeResponseGradingStatus(
            g.FreeResponseId,
            g.FreeResponse.QuestionId,
            g.FreeResponse.Question.DisplayOrder,
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
            .OrderBy(fr => fr.Question.DisplayOrder)
            .ToListAsync(cancellationToken);

        var totalExamQuestions = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId && q.QuestionType == QuestionType.FreeResponse.ToCode())
            .CountAsync(cancellationToken);

        var questionItems = new List<SpeakingQuestionReviewItem>();
        decimal overallTotalScore = 0m;
        decimal overallMaxScore = 0m;

        foreach (var fr in freeResponses)
        {
            var grading = fr.AiGradingFreeResponse;
            var scores = grading?.AiGradingScores ?? (IEnumerable<AiGradingScore>)Array.Empty<AiGradingScore>();

            // Load applicable SectionCriteria to know the maximums
            var sectionCriteria = await db.SectionCriteria
                .AsNoTracking()
                .Where(sc => sc.SectionId == fr.Question.SectionId)
                .ToListAsync(cancellationToken);

            var criteriaItems = new List<SpeakingCriterionScoreItem>();
            decimal qTotal = 0m;
            decimal qMax = 0m;

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
                fr.QuestionId,
                fr.Question.DisplayOrder,
                fr.Question.Section.Name,
                fr.Question.Stem,
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

        // TOEIC Speaking scale (0-200) or raw score
        int? scaledScore = null;
        if (overallMaxScore > 0)
        {
            scaledScore = (int)Math.Round((overallTotalScore / overallMaxScore) * 200m, MidpointRounding.AwayFromZero);
        }

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
            lowest);
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

        var fr = await db.FreeResponses
            .AsNoTracking()
            .Where(r => r.AttemptId == attemptId && r.QuestionId == questionId)
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
