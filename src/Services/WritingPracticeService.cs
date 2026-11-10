using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed class WritingPracticeService(
    CompanionDbContext db,
    ILearnerService learners,
    ICreditLedger? creditLedger = null) : IWritingPracticeService
{
    public const int WritingPracticeCreditCost = 1;
    public const string FreeDiagnosticSourceName = "Free diagnostic benchmark";
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db);

    private static bool IsFreeDiagnostic(Exam exam) =>
        string.Equals(exam.Purpose, "placement", StringComparison.OrdinalIgnoreCase)
        || string.Equals(exam.SourceName, FreeDiagnosticSourceName, StringComparison.Ordinal);

    /// <summary>
    /// #262: the paper's productive positions, from the one loader the sitting engine uses.
    /// <para>
    /// This service used to enumerate an exam's tasks with a legacy query, which returns nothing for a
    /// paper authored on the pool. Rather than a second interpretation of "what is on this paper", it
    /// reads the same <see cref="SittingQuestion"/> list the sitting does - so the practice screen and
    /// the paper can never disagree about which tasks exist or how many there are.
    /// </para>
    /// </summary>
    private async Task<List<SittingQuestion>> LoadPaperPositionsAsync(int examId, CancellationToken cancellationToken) =>
        await new AttemptEngine(db).LoadSittingQuestionsAsync(examId, cancellationToken);

    /// <summary>The one position a request names, resolved through the same seam.</summary>
    private async Task<SittingQuestion> LoadPaperPositionAsync(
        int examId, int questionId, CancellationToken cancellationToken)
    {
        var positions = await LoadPaperPositionsAsync(examId, cancellationToken);
        return positions.FirstOrDefault(p => p.Id == questionId)
            ?? throw new WritingPracticeException(
                WritingPracticeException.QuestionNotFound, $"Question {questionId} not found.");
    }

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
                s.ExpectedQuestionCount,
                null,
                s.DisplayOrder))
            .ToListAsync(cancellationToken);

        // #239: a prior submission is listed and re-openable. Newest first. This includes the active
        // attempt when there is one, which is why `activeAttempt` is not appended separately.
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

        return new WritingPracticeInstructionsView(
            exam.ExamId,
            exam.CertId,
            exam.Cert.Code,
            exam.Name,
            exam.Description,
            exam.DurationMinutes,
            exam.QuestionCount ?? 0,
            IsFreeDiagnostic(exam) ? 0 : WritingPracticeCreditCost,
            balance,
            activeAttempt?.AttemptId,
            sections)
        {
            Attempts = attempts
        };
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
        if (!IsFreeDiagnostic(exam)
            && await _creditLedger.GetBalanceAsync(userId, cancellationToken) < WritingPracticeCreditCost)
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
            BandCode = null   // E1.3 (#148): the estimate is per grading, on AiGradings.BandCode
        };

        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);

        // Pre-populate free response draft rows for the paper's productive tasks.
        // #262: the paper is the source. A pool-authored paper has no question rows, and the old query
        // wrote no drafts for it - so the screen opened with every task missing a response row.
        var paperPositions = await LoadPaperPositionsAsync(examId, cancellationToken);
        var questions = paperPositions
            .Where(p => p.RenderFormat == QuestionType.FreeResponse.ToCode())
            .ToList();

        if (questions.Count > 0)
        {
            foreach (var q in questions)
            {
                db.FreeResponses.Add(new FreeResponse
                {
                    AttemptId = attempt.AttemptId,
                    QuestionId = q.ExamItemId is null ? q.Id : null,
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

        // #262: the paper is the source, not the legacy question table. A pool-authored paper has no
        // question rows, and the old query returned nothing for it - so the screen reported "no
        // questions found" for a paper the sitting engine can open.
        var positions = await LoadPaperPositionsAsync(attempt.ExamId, cancellationToken);
        var questions = positions
            .Where(p => p.RenderFormat == QuestionType.FreeResponse.ToCode())
            .ToList();

        if (questions.Count == 0)
        {
            throw new WritingPracticeException(WritingPracticeException.QuestionNotFound, "No questions found for this exam.");
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

        var targetFr = ResponseFor(targetQuestion);

        var questionNavs = questions.Select((q, idx) =>
        {
            var fr = ResponseFor(q);
            var isAnswered = fr != null && !string.IsNullOrWhiteSpace(fr.ResponseText);
            return new WritingQuestionNav(
                q.Id,
                idx + 1,
                q.Section.Code,
                q.Section.Name,
                isAnswered,
                q.Id == targetQuestion.Id);
        }).ToList();

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        var currentQuestionIndex = questions.IndexOf(targetQuestion) + 1;

        var questionView = new WritingQuestionView(
            targetQuestion.Id,
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
            targetQuestion.Id,
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

        // #262: the position is resolved from the paper, so a pool task resolves to its roster row and
        // a legacy task to its question. LastQuestionId is a foreign key to Questions and cannot hold a
        // roster position, so it is only stamped for a legacy paper - it retires with the tables.
        var position = await LoadPaperPositionAsync(attempt.ExamId, questionId, cancellationToken);

        var positionExamItemId = position.ExamItemId;
        var freeResponse = await db.FreeResponses
            .Where(fr => fr.AttemptId == attemptId
                && (positionExamItemId.HasValue
                    ? fr.ExamItemId == positionExamItemId
                    : fr.QuestionId == questionId))
            .SingleOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var text = responseText ?? string.Empty;

        if (freeResponse is null)
        {
            freeResponse = new FreeResponse
            {
                AttemptId = attemptId,
                QuestionId = position.ExamItemId is null ? questionId : null,
                ExamItemId = position.ExamItemId,
                ExamId = attempt.ExamId,
                SkillId = position.Section.SkillId,
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

        if (position.ExamItemId is null)
        {
            attempt.LastQuestionId = questionId;
        }
        await db.SaveChangesAsync(cancellationToken);

        var wordCount = WordCount.Count(text);
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

        var isFreeDiagnostic = IsFreeDiagnostic(attempt.Exam);

        // Verify credit balance before charge (block before you charge)
        if (!isFreeDiagnostic
            && await _creditLedger.GetBalanceAsync(userId, cancellationToken) < WritingPracticeCreditCost)
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
        attempt.BandCode = null;   // E1.3 (#148): the estimate is per grading, on AiGradings.BandCode

        await db.SaveChangesAsync(cancellationToken);

        // Diagnostic writing is part of the free placement loop; paid practice is charged once.
        if (!isFreeDiagnostic)
        {
            try
            {
                await _creditLedger.ChargeAiGradingAsync(userId, attemptId, WritingPracticeCreditCost, cancellationToken);
            }
            catch (CreditLedgerException ex) when (ex.Code == CreditLedgerException.InsufficientCredits)
            {
                throw new WritingPracticeException(WritingPracticeException.InsufficientCredits, "You do not have enough credits to submit and grade this writing attempt.");
            }
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
                fr.QuestionId!.Value,
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
        int gradedCount = 0;

        foreach (var fr in freeResponses)
        {
            var text = fr.ResponseText ?? string.Empty;
            // #239: the same rule the composer and the save path use, so the review page's word count
            // and the one the learner watched cannot disagree.
            var words = WordCount.Count(text);
            if (words > 0) answeredCount++;

            var criteriaScores = new List<WritingCriterionScoreItem>();
            var marks = new List<AttemptEngine.RubricMark>();
            decimal qMax = 0m;

            if (fr.AiGradingFreeResponse?.AiGradingScores is { Count: > 0 } scores)
            {
                // #239: a task counts as evidence of a level only when it was BOTH answered and graded.
                // A blank draft is still graded (the grader scores it zero with "no response"), and
                // counting that would let a one-answer sitting look like a two-task measurement.
                if (words > 0) gradedCount++;
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

                    marks.Add(new AttemptEngine.RubricMark(
                        critScore.Score, criterionMax, critScore.GradingCriterion.Weight));
                    qMax += criterionMax;
                }
            }

            // E2.1 (#151): the per-question total is the weighted one the worker stored and the band
            // was decided from, not a plain sum that would disagree with both.
            var (qScore, _) = AttemptEngine.WeightedRubricTotal(marks);

            questionItems.Add(new WritingQuestionReviewItem(
                fr.QuestionId!.Value,
                fr.Question.DisplayOrder,
                fr.Question.Section.Name,
                fr.Question.Stem,
                fr.ResponseText,
                words,
                qScore,
                qMax,
                criteriaScores,
                AiErrorSpan.Deserialize(fr.AiGradingFreeResponse?.ErrorSpansJson),
                fr.AiGradingFreeResponse?.PriorityFix,
                fr.AiGradingFreeResponse?.ModelRewrite));

            overallTotalScore += qScore;
            overallMaxScore += qMax;
        }

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
            questionItems,
            estimatedCode,
            estimatedName)
        {
            // #239: "one task is not a level" is a count of GRADED tasks, not of answered ones - a
            // blank or failed answer is not evidence of a level.
            GradedQuestions = gradedCount
        };
    }
}
