using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public class WritingPracticeException(string code, string message) : Exception(message)
{
    public const string InsufficientCredits = "insufficient_credits";
    public const string ExamNotFound = "exam_not_found";
    public const string AttemptNotFound = "attempt_not_found";
    public const string AttemptNotActive = "attempt_not_active";
    public const string AttemptExpired = "attempt_expired";
    public const string QuestionNotFound = "question_not_found";
    public const string AttemptNotFinished = "attempt_not_finished";
    public const string RetryNotAllowed = "retry_not_allowed";

    public string Code { get; } = code;
}

public interface IWritingPracticeService
{
    Task<WritingPracticeInstructionsView> GetInstructionsAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<WritingAttemptInfo> StartAttemptAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<WritingTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionId, CancellationToken cancellationToken = default);

    Task<SaveDraftResult> SaveDraftAsync(int userId, int attemptId, int questionId, string? responseText, CancellationToken cancellationToken = default);

    Task<SubmitWritingResult> SubmitAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<WritingGradingPendingView> GetGradingPendingAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task RetryGradingAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<WritingReviewView> GetAttemptReviewAsync(int userId, int attemptId, CancellationToken cancellationToken = default);
}

public sealed class WritingPracticeService(
    CompanionDbContext db,
    ILearnerService learners,
    ICreditLedger? creditLedger = null) : IWritingPracticeService
{
    public const int WritingPracticeCreditCost = 1;
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db);

    public async Task<WritingPracticeInstructionsView> GetInstructionsAsync(int userId, int examId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var exam = await db.Exams
            .AsNoTracking()
            .Where(e => e.ExamId == examId && e.CertId == certId && e.Purpose == ExamPurpose.WritingPractice.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .Include(e => e.Cert)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WritingPracticeException(WritingPracticeException.ExamNotFound, $"Writing practice exam {examId} not found.");

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
            .Where(s => s.CertId == certId && s.Modality == Modality.Writing.ToCode())
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new WritingSectionInfo(
                s.SectionId,
                s.Code,
                s.Name,
                s.ExpectedQuestionCount ?? 0,
                null,
                s.DisplayOrder))
            .ToListAsync(cancellationToken);

        return new WritingPracticeInstructionsView(
            exam.ExamId,
            exam.CertId,
            exam.Cert.Code,
            exam.Name,
            exam.Description,
            exam.DurationMinutes,
            exam.QuestionCount ?? 0,
            WritingPracticeCreditCost,
            balance,
            activeAttempt?.AttemptId,
            sections);
    }

    public async Task<WritingAttemptInfo> StartAttemptAsync(int userId, int examId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var exam = await db.Exams
            .Where(e => e.ExamId == examId && e.CertId == certId && e.Purpose == ExamPurpose.WritingPractice.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WritingPracticeException(WritingPracticeException.ExamNotFound, $"Writing practice exam {examId} not found.");

        // Check if there is already an in-progress attempt for this exam
        var existingAttempt = await db.ExamAttempts
            .Where(a => a.UserId == userId && a.ExamId == examId && a.Status == AttemptStatus.InProgress.ToCode())
            .FirstOrDefaultAsync(cancellationToken);

        if (existingAttempt is not null)
        {
            if (DateTimeOffset.UtcNow >= existingAttempt.ExpiresAt)
            {
                existingAttempt.Status = AttemptStatus.Abandoned.ToCode();
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var remaining = Math.Max(0, (int)(existingAttempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
                return new WritingAttemptInfo(
                    existingAttempt.AttemptId,
                    existingAttempt.ExamId,
                    EnumCodes.Parse<AttemptStatus>(existingAttempt.Status),
                    existingAttempt.StartedAt,
                    existingAttempt.ExpiresAt,
                    remaining,
                    existingAttempt.IsAutoSubmitted);
            }
        }

        // Check credits before starting ("block before you charge" invariant)
        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);

        if (balance < WritingPracticeCreditCost)
        {
            throw new WritingPracticeException(WritingPracticeException.InsufficientCredits, "You do not have enough credits to start writing practice.");
        }

        var now = DateTimeOffset.UtcNow;
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
            TotalScore = null,
            BandCode = null
        };

        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);

        // Pre-populate free response draft rows for questions in this exam
        var questions = await db.Questions
            .Where(q => q.ExamId == examId)
            .OrderBy(q => q.DisplayOrder)
            .ToListAsync(cancellationToken);

        if (questions.Count > 0)
        {
            attempt.LastQuestionId = questions[0].QuestionId;
            foreach (var q in questions)
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
            await db.SaveChangesAsync(cancellationToken);
        }

        var remainingSeconds = Math.Max(0, (int)(expiresAt - now).TotalSeconds);
        return new WritingAttemptInfo(
            attempt.AttemptId,
            attempt.ExamId,
            AttemptStatus.InProgress,
            attempt.StartedAt,
            attempt.ExpiresAt,
            remainingSeconds,
            false);
    }

    public async Task<WritingTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WritingPracticeException(WritingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new WritingPracticeException(WritingPracticeException.AttemptNotActive, "Attempt is not active.");
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            throw new WritingPracticeException(WritingPracticeException.AttemptExpired, "Attempt has expired.");
        }

        var questions = await db.Questions
            .AsNoTracking()
            .Where(q => q.ExamId == attempt.ExamId)
            .Include(q => q.Group)
            .Include(q => q.Section)
            .OrderBy(q => q.DisplayOrder)
            .ToListAsync(cancellationToken);

        if (questions.Count == 0)
        {
            throw new WritingPracticeException(WritingPracticeException.QuestionNotFound, "No questions found for this exam.");
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

        var targetFr = freeResponses.FirstOrDefault(fr => fr.QuestionId == targetQuestion.QuestionId);

        var questionNavs = questions.Select((q, idx) =>
        {
            var fr = freeResponses.FirstOrDefault(r => r.QuestionId == q.QuestionId);
            var isAnswered = fr != null && !string.IsNullOrWhiteSpace(fr.ResponseText);
            return new WritingQuestionNav(
                q.QuestionId,
                idx + 1,
                q.Section.Code,
                q.Section.Name,
                isAnswered,
                q.QuestionId == targetQuestion.QuestionId);
        }).ToList();

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        var currentQuestionIndex = questions.IndexOf(targetQuestion) + 1;

        var questionView = new WritingQuestionView(
            targetQuestion.QuestionId,
            currentQuestionIndex,
            targetQuestion.Stem,
            targetQuestion.MinWords,
            targetQuestion.MaxWords,
            targetQuestion.ResponseSeconds ?? targetQuestion.Group?.ResponseSeconds,
            targetQuestion.Hint,
            targetQuestion.Section.Code,
            targetQuestion.Section.Name,
            targetQuestion.Group?.ImagePath,
            targetQuestion.Group?.ImageAlt,
            targetFr?.ResponseText,
            targetFr?.DraftSavedAt,
            targetFr != null ? EnumCodes.Parse<FreeResponseStatus>(targetFr.Status) : FreeResponseStatus.Draft);

        return new WritingTakeView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam.Name,
            attempt.CertId,
            attempt.Cert.Code,
            EnumCodes.Parse<AttemptStatus>(attempt.Status),
            attempt.StartedAt,
            attempt.ExpiresAt,
            remainingSeconds,
            targetQuestion.QuestionId,
            currentQuestionIndex,
            questions.Count,
            questionView,
            questionNavs);
    }

    public async Task<SaveDraftResult> SaveDraftAsync(int userId, int attemptId, int questionId, string? responseText, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WritingPracticeException(WritingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new WritingPracticeException(WritingPracticeException.AttemptNotActive, "Attempt is not in progress.");
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            throw new WritingPracticeException(WritingPracticeException.AttemptExpired, "Attempt has expired.");
        }

        var freeResponse = await db.FreeResponses
            .Where(fr => fr.AttemptId == attemptId && fr.QuestionId == questionId)
            .SingleOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var text = responseText ?? string.Empty;

        if (freeResponse is null)
        {
            var question = await db.Questions.FindAsync(new object[] { questionId }, cancellationToken)
                ?? throw new WritingPracticeException(WritingPracticeException.QuestionNotFound, $"Question {questionId} not found.");

            freeResponse = new FreeResponse
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                ExamId = attempt.ExamId,
                SkillId = question.SkillId,
                QuestionType = QuestionType.FreeResponse.ToCode(),
                ResponseText = text,
                Status = FreeResponseStatus.Draft.ToCode(),
                SubmittedAt = null,
                DraftSavedAt = now
            };
            db.FreeResponses.Add(freeResponse);
        }
        else
        {
            freeResponse.ResponseText = text;
            freeResponse.DraftSavedAt = now;
            freeResponse.SubmittedAt = null;
            freeResponse.Status = FreeResponseStatus.Draft.ToCode();
        }

        attempt.LastQuestionId = questionId;
        await db.SaveChangesAsync(cancellationToken);

        var wordCount = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return new SaveDraftResult(freeResponse.FreeResponseId, questionId, now, wordCount);
    }

    public async Task<SubmitWritingResult> SubmitAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .Include(a => a.Exam)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WritingPracticeException(WritingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            if (attempt.Status == AttemptStatus.Submitted.ToCode() || attempt.Status == AttemptStatus.Graded.ToCode())
            {
                return new SubmitWritingResult(attempt.AttemptId, EnumCodes.Parse<AttemptStatus>(attempt.Status), attempt.SubmittedAt ?? DateTimeOffset.UtcNow);
            }
            throw new WritingPracticeException(WritingPracticeException.AttemptNotActive, "Attempt is not in progress.");
        }

        // Verify credit balance before charge (block before you charge)
        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);
        if (balance < WritingPracticeCreditCost)
        {
            throw new WritingPracticeException(WritingPracticeException.InsufficientCredits, "You do not have enough credits to submit and grade this writing attempt.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var freeResponses = await db.FreeResponses
            .Where(fr => fr.AttemptId == attemptId)
            .ToListAsync(cancellationToken);

        // Transition all drafts to pending_ai (UI-40 / trg_Attempts_NoDraftsAfterSubmit)
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
        attempt.TotalScore = null;
        attempt.BandCode = null;

        await db.SaveChangesAsync(cancellationToken);

        // Charge 1 credit via CreditLedger: Reason='ai_grading', Delta=-1
        try
        {
            await _creditLedger.ChargeAiGradingAsync(userId, attemptId, WritingPracticeCreditCost, cancellationToken);
        }
        catch (CreditLedgerException ex) when (ex.Code == CreditLedgerException.InsufficientCredits)
        {
            throw new WritingPracticeException(WritingPracticeException.InsufficientCredits, "You do not have enough credits to submit and grade this writing attempt.");
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

        return new SubmitWritingResult(attempt.AttemptId, AttemptStatus.Submitted, now);
    }

    public async Task<WritingGradingPendingView> GetGradingPendingAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WritingPracticeException(WritingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Where(fr => fr.AttemptId == attemptId)
            .Include(fr => fr.Question)
            .Include(fr => fr.AiGradingFreeResponse)
            .OrderBy(fr => fr.Question.DisplayOrder)
            .ToListAsync(cancellationToken);

        var items = new List<FreeResponseGradingStatus>();
        int gradedCount = 0;
        int failedCount = 0;
        int pendingCount = 0;
        string? failureReason = null;

        foreach (var fr in freeResponses)
        {
            var grading = fr.AiGradingFreeResponse;
            var status = grading != null
                ? EnumCodes.Parse<AiGradingStatus>(grading.Status)
                : AiGradingStatus.Pending;

            if (status == AiGradingStatus.Done) gradedCount++;
            else if (status == AiGradingStatus.Failed)
            {
                failedCount++;
                failureReason ??= grading?.FailureReason;
            }
            else pendingCount++;

            items.Add(new FreeResponseGradingStatus(
                fr.FreeResponseId,
                fr.QuestionId,
                fr.Question.DisplayOrder,
                status,
                grading?.AttemptCount ?? 0,
                grading?.FailureReason));
        }

        var canRetry = failedCount > 0;

        return new WritingGradingPendingView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam.Name,
            attempt.Cert.Code,
            EnumCodes.Parse<AttemptStatus>(attempt.Status),
            freeResponses.Count,
            gradedCount,
            failedCount,
            pendingCount,
            canRetry,
            failureReason,
            items);
    }

    public async Task RetryGradingAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WritingPracticeException(WritingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        var failedGradings = await db.AiGradings
            .Include(g => g.FreeResponse)
            .Where(g => g.FreeResponse.AttemptId == attemptId && g.Status == AiGradingStatus.Failed.ToCode())
            .ToListAsync(cancellationToken);

        if (failedGradings.Count == 0)
        {
            throw new WritingPracticeException(WritingPracticeException.RetryNotAllowed, "No failed gradings found to retry.");
        }

        // Retry reuses existing AiGradings rows and NEVER charges credit
        foreach (var grading in failedGradings)
        {
            grading.Status = AiGradingStatus.Pending.ToCode();
            grading.AttemptCount += 1;
            grading.FailureReason = null;
            grading.CompletedAt = null;
        }

        if (attempt.Status != AttemptStatus.Submitted.ToCode())
        {
            attempt.Status = AttemptStatus.Submitted.ToCode();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WritingReviewView> GetAttemptReviewAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId && a.UserId == userId)
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WritingPracticeException(WritingPracticeException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.Graded.ToCode())
        {
            throw new WritingPracticeException(WritingPracticeException.AttemptNotFinished, "This attempt has not completed grading.");
        }

        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Where(fr => fr.AttemptId == attemptId)
            .Include(fr => fr.Question).ThenInclude(q => q.Section)
            .Include(fr => fr.AiGradingFreeResponse).ThenInclude(g => g!.AiGradingScores).ThenInclude(s => s.GradingCriterion)
            .OrderBy(fr => fr.Question.DisplayOrder)
            .ToListAsync(cancellationToken);

        var sectionCriteria = await db.SectionCriteria
            .AsNoTracking()
            .Where(sc => sc.Section.CertId == attempt.CertId)
            .ToListAsync(cancellationToken);

        var questionItems = new List<WritingQuestionReviewItem>();
        decimal overallTotalScore = 0m;
        decimal overallMaxScore = 0m;
        int answeredCount = 0;

        foreach (var fr in freeResponses)
        {
            var text = fr.ResponseText ?? string.Empty;
            var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (words > 0) answeredCount++;

            var criteriaScores = new List<WritingCriterionScoreItem>();
            decimal qScore = 0m;
            decimal qMax = 0m;

            if (fr.AiGradingFreeResponse?.AiGradingScores is { Count: > 0 } scores)
            {
                foreach (var critScore in scores.OrderBy(s => s.GradingCriterion.DisplayOrder))
                {
                    var overrideMax = sectionCriteria.FirstOrDefault(c => c.SectionId == fr.Question.SectionId && c.CriterionId == critScore.CriterionId)?.MaxScore;
                    var criterionMax = overrideMax ?? critScore.GradingCriterion.MaxScore;

                    criteriaScores.Add(new WritingCriterionScoreItem(
                        critScore.CriterionId,
                        critScore.GradingCriterion.Code,
                        critScore.GradingCriterion.Name,
                        critScore.Score,
                        criterionMax,
                        critScore.Comment));

                    qScore += critScore.Score;
                    qMax += criterionMax;
                }
            }

            questionItems.Add(new WritingQuestionReviewItem(
                fr.QuestionId,
                fr.Question.DisplayOrder,
                fr.Question.Section.Name,
                fr.Question.Stem,
                fr.ResponseText,
                words,
                qScore,
                qMax,
                criteriaScores));

            overallTotalScore += qScore;
            overallMaxScore += qMax;
        }

        return new WritingReviewView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam.Name,
            attempt.Cert.Code,
            attempt.SubmittedAt,
            freeResponses.Count,
            answeredCount,
            overallTotalScore,
            overallMaxScore,
            questionItems);
    }
}

