using System;
using System.Collections.Generic;
using System.Data;
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

public sealed partial class PlacementService(
    CompanionDbContext db,
    ILearnerService learners,
    IAttemptEngine? attemptEngine = null,
    TimeProvider? clock = null,
    IPlacementStageOneDrawService? stageOneDraw = null,
    IPlacementStageTwoDrawService? stageTwoDraw = null,
    ICreditLedger? creditLedger = null,
    PlacementOptions? options = null) : IPlacementService
{
    private readonly IAttemptEngine _attemptEngine = attemptEngine ?? new AttemptEngine(db);

    // #276: the two draws, constructed over the same DbContext when no caller supplies them - so a
    // test that does `new PlacementService(db, learners)` still exercises the real draw rather than a
    // branch nobody runs in production.
    private readonly IPlacementStageOneDrawService _stageOneDraw = stageOneDraw ?? new PlacementStageOneDrawService(db);
    private readonly IPlacementStageTwoDrawService _stageTwoDraw = stageTwoDraw ?? new PlacementStageTwoDrawService(db);

    /// <summary>#213: the cooldown is a wall-clock rule, so the clock is injected rather than assumed.</summary>
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db, clock ?? TimeProvider.System);
    private readonly PlacementOptions _options = options ?? new PlacementOptions();

    private static PlacementException MapPlacementException(AttemptEngineException ex) => ex.Code switch
    {
        AttemptEngineException.AttemptNotFound => new PlacementException(PlacementException.PlacementNotFound, ex.Message),
        AttemptEngineException.AttemptNotActive => new PlacementException(PlacementException.PlacementNotActive, ex.Message),
        AttemptEngineException.AttemptExpired => new PlacementException(PlacementException.PlacementExpired, ex.Message),
        AttemptEngineException.AttemptNotFinished => new PlacementException(PlacementException.PlacementNotFinished, ex.Message),
        _ => new PlacementException(ex.Code, ex.Message)
    };

    public async Task<PlacementCatalogView> GetPlacementCatalogAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new CertificateOption(c.Code, c.Name, c.SectionNoun))
            .SingleAsync(cancellationToken);

        // Auto-submit any expired in-progress placement tests
        var expiredPlacements = await db.PlacementTests
            .Where(p => p.UserId == userId && p.Status == PlacementStatus.InProgress.ToCode() && p.ExamAttempt != null && DateTimeOffset.UtcNow >= p.ExamAttempt.ExpiresAt)
            .Select(p => p.PlacementId)
            .ToListAsync(cancellationToken);

        foreach (var expId in expiredPlacements)
        {
            await SubmitPlacementInternalAsync(expId, userId, isAutoSubmit: true, cancellationToken);
        }

        var activePlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == PlacementStatus.InProgress.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        var latestDone = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        string? currentBandName = null;
        if (!string.IsNullOrEmpty(latestDone?.ResultBandCode))
        {
            var bandCertId = latestDone.CertId ?? certId;
            currentBandName = await db.ScoreBands
                .AsNoTracking()
                .Where(b => b.CertId == bandCertId && b.Code == latestDone.ResultBandCode)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // #311: Single Universal Placement Exam across the platform (preferred for active cert if present, otherwise universal).
        var certExams = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId && e.Purpose == ExamPurpose.Placement.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .OrderBy(e => e.ExamId)
            .ToListAsync(cancellationToken);

        var exams = certExams.Count > 0
            ? certExams
            : await db.Exams
                .AsNoTracking()
                .Where(e => e.Purpose == ExamPurpose.Placement.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
                .OrderByDescending(e => e.Name.Contains("Universal"))
                .ThenByDescending(e => e.ExamId)
                .Take(1)
                .ToListAsync(cancellationToken);

        var items = new List<PlacementCatalogItem>();
        for (int i = 0; i < exams.Count; i++)
        {
            var exam = exams[i];
            bool isRecommended = exam.Name.Contains("Universal", StringComparison.OrdinalIgnoreCase)
                || exam.Name.Contains("Standard", StringComparison.OrdinalIgnoreCase)
                || i == 0;
            int? activePlacementId = (activePlacement?.ExamId == exam.ExamId) ? activePlacement.PlacementId : null;

            items.Add(new PlacementCatalogItem(
                exam.ExamId,
                exam.CertId,
                cert.Code,
                exam.Name,
                exam.Description,
                exam.DurationMinutes,
                exam.QuestionCount ?? 0,
                isRecommended,
                activePlacementId));
        }

        var isRetake = await db.PlacementTests
            .AsNoTracking()
            .AnyAsync(p => p.UserId == userId && p.Status != PlacementStatus.InProgress.ToCode(), cancellationToken);

        int? creditBalance = isRetake
            ? await _creditLedger.GetBalanceAsync(userId, cancellationToken)
            : null;

        return new PlacementCatalogView(
            cert,
            latestDone?.ResultBandCode,
            currentBandName,
            latestDone?.TotalScore,
            items,
            activePlacement?.PlacementId,
            activePlacement?.AttemptId,
            IsRetake: isRetake,
            AllSkillsRetakeCredits: _options.AllSkillsRetakeCredits,
            ListeningAndReadingRetakeCredits: _options.ListeningAndReadingRetakeCredits,
            CreditBalance: creditBalance);
    }

    public Task<PlacementTestInfo> StartPlacementTestAsync(int userId, int examId, CancellationToken cancellationToken = default) =>
        StartPlacementInternalAsync(userId, examId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills, idempotencyKey: null, cancellationToken);

    public Task<PlacementTestInfo> StartPlacementTestAsync(
        int userId, int examId, PlacementReuseChoice reuse, CancellationToken cancellationToken = default) =>
        StartPlacementInternalAsync(userId, examId, reuse, PlacementScope.AllSkills, idempotencyKey: null, cancellationToken);

    public Task<PlacementTestInfo> StartPlacementTestAsync(
        int userId,
        int examId,
        PlacementReuseChoice reuse,
        PlacementScope scope,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        StartPlacementInternalAsync(userId, examId, reuse, scope, idempotencyKey, cancellationToken);

    private static PlacementTestInfo ToPlacementTestInfo(PlacementTest placement, int fallbackExamId, int creditsCharged)
    {
        var remaining = placement.ExamAttempt is not null
            ? Math.Max(0, (int)(placement.ExamAttempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)
            : 0;
        var parsedScope = EnumCodes.Parse<PlacementScope>(placement.Scope);



        return new PlacementTestInfo(
            placement.PlacementId,
            placement.AttemptId ?? 0,
            placement.ExamId ?? fallbackExamId,
            EnumCodes.Parse<PlacementStatus>(placement.Status),
            placement.ExamAttempt?.StartedAt ?? placement.TakenAt,
            placement.ExamAttempt?.ExpiresAt ?? DateTimeOffset.UtcNow,
            remaining,
            placement.ExamAttempt?.IsAutoSubmitted ?? false,
            Scope: parsedScope,
            CreditsCharged: creditsCharged);
    }

    private static bool IsUniqueOrSerializationConflict(Exception ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is Microsoft.Data.SqlClient.SqlException sqlEx
                && sqlEx.Number is 2601 or 2627 or 1205)
            {
                return true;
            }
        }
        return false;
    }

    private async Task<PlacementTestInfo> StartPlacementInternalAsync(
        int userId,
        int examId,
        PlacementReuseChoice reuse,
        PlacementScope scope,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var exam = await db.Exams
            .Where(e => e.ExamId == examId && e.Purpose == ExamPurpose.Placement.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PlacementException(PlacementException.ExamNotFound, $"Placement exam {examId} not found.");

        var normalizedKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

        for (var attemptNo = 0; attemptNo < 3; attemptNo++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                if (normalizedKey is not null)
                {
                    var existingByKey = await db.PlacementTests
                        .Include(p => p.ExamAttempt)
                        .Include(p => p.ExamAttempt)
                        .Include(p => p.CreditTransaction)
                        .FirstOrDefaultAsync(p => p.UserId == userId && p.IdempotencyKey == normalizedKey, cancellationToken);
                    if (existingByKey is not null)
                    {
                        await tx.CommitAsync(cancellationToken);
                        var creditsCharged = existingByKey.CreditTransaction is not null
                            ? Math.Abs(existingByKey.CreditTransaction.Delta)
                            : 0;
                        return ToPlacementTestInfo(existingByKey, examId, creditsCharged);
                    }
                }

                // Check if there is already an in-progress placement test for this exam
                var existingPlacement = await db.PlacementTests
                    .Include(p => p.ExamAttempt)
                    .Where(p => p.UserId == userId && p.ExamId == examId && p.Status == PlacementStatus.InProgress.ToCode())
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingPlacement is not null)
                {
                    if (existingPlacement.ExamAttempt is not null && DateTimeOffset.UtcNow >= existingPlacement.ExamAttempt.ExpiresAt)
                    {
                        await SubmitPlacementInternalAsync(existingPlacement.PlacementId, userId, isAutoSubmit: true, cancellationToken);
                    }
                    else
                    {
                        await tx.CommitAsync(cancellationToken);
                        return ToPlacementTestInfo(existingPlacement, examId, creditsCharged: 0);
                    }
                }

                // #213 / PLC-07: the retake cooldown. It reads the most recent FINISHED sitting and nothing else,
                // so there is no lifetime cap anywhere in this rule. A resume of the sitting already in progress
                // returned above and is never refused by it - a cooldown that blocked a resume would strand a
                // learner halfway through a paper.
                await EnsureCooldownElapsedAsync(userId, exam.CertId, cancellationToken);

                // #289 / #311: the first placement per learner is free and never touches the credit ledger;
                // every subsequent placement charges the configured price for the chosen scope inside this serializable
                // transaction before abandoning any prior sitting or creating the attempt.
                var isRetake = await db.PlacementTests
                    .AnyAsync(p => p.UserId == userId && p.Status != PlacementStatus.InProgress.ToCode(), cancellationToken);

                CreditReceipt? chargeTxn = null;
                if (isRetake)
                {
                    var price = _options.PriceFor(scope);
                    try
                    {
                        chargeTxn = await _creditLedger.ChargePlacementRetakeAsync(userId, scope, price, cancellationToken);
                    }
                    catch (CreditLedgerException ex) when (ex.Code == CreditLedgerException.InsufficientCredits)
                    {
                        throw new PlacementException(PlacementException.InsufficientCredits, ex.Message);
                    }
                }

                // Abandon any other in-progress placement test for this learner and its linked attempt
                var otherPlacements = await db.PlacementTests
                    .Include(p => p.ExamAttempt)
                    .Where(p => p.UserId == userId && p.Status == PlacementStatus.InProgress.ToCode())
                    .ToListAsync(cancellationToken);
                foreach (var other in otherPlacements)
                {
                    other.Status = PlacementStatus.Abandoned.ToCode();
                    if (other.ExamAttempt != null && other.ExamAttempt.Status == AttemptStatus.InProgress.ToCode())
                    {
                        other.ExamAttempt.Status = AttemptStatus.Abandoned.ToCode();
                    }
                }

                var attempt = await _attemptEngine.CreateAttemptAsync(
                    userId, exam.CertId, examId, exam.DurationMinutes,
                    drawnPaper: null,
                    scope,
                    cancellationToken);

                var placement = new PlacementTest
                {
                    UserId = userId,
                    CertId = exam.CertId,
                    ExamId = examId,
                    AttemptId = attempt.AttemptId,
                    Status = PlacementStatus.InProgress.ToCode(),
                    ExamPurpose = ExamPurpose.Placement.ToCode(),
                    Scope = scope.ToCode(),
                    CreditTxnId = chargeTxn?.TxnId,
                    IdempotencyKey = normalizedKey,
                    TakenAt = attempt.StartedAt,
                    TotalScore = null,
                    ResultBandCode = null,
                    // The choice lives on the sitting; the result writer reads it per skill.
                    ReusesPriorResults = reuse == PlacementReuseChoice.ReuseValid,
                };

                db.PlacementTests.Add(placement);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                placement.ExamAttempt = attempt;
                return ToPlacementTestInfo(
                    placement,
                    examId,
                    creditsCharged: chargeTxn is not null ? Math.Abs(chargeTxn.Delta) : 0);
            }
            catch (Exception ex) when (normalizedKey is not null && attemptNo < 2 && IsUniqueOrSerializationConflict(ex))
            {
                try { await tx.RollbackAsync(cancellationToken); } catch { /* already rolled back on deadlock */ }
                db.ChangeTracker.Clear();
                await Task.Delay(25 * (attemptNo + 1), cancellationToken);
            }
        }

        throw new InvalidOperationException("Unreachable: placement start conflict retry exhausted.");
    }

    public async Task<PreflightStatusView> GetPlacementPreflightStatusAsync(int userId, int placementId, CancellationToken cancellationToken = default)
    {
        var attemptId = await db.PlacementTests
            .Where(p => p.PlacementId == placementId && p.UserId == userId)
            .Select(p => p.AttemptId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        try
        {
            return await _attemptEngine.GetPreflightStatusAsync(attemptId, userId, cancellationToken);
        }
        catch (AttemptEngineException ex)
        {
            throw MapPlacementException(ex);
        }
    }

    public async Task<PlacementTakeView> GetPlacementTakeAsync(int userId, int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default)
    {
        var placement = await db.PlacementTests
            .Include(p => p.ExamAttempt).ThenInclude(a => a!.Exam)
            .Include(p => p.Cert)
            .SingleOrDefaultAsync(p => p.PlacementId == placementId && p.UserId == userId, cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        if (placement.Status == PlacementStatus.Abandoned.ToCode())
        {
            throw new PlacementException(PlacementException.PlacementNotActive, "This placement test was abandoned.");
        }

        var attempt = placement.ExamAttempt
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Exam attempt for placement test {placementId} not found.");

        // Auto-submit if expired
        if (placement.Status == PlacementStatus.InProgress.ToCode() && attempt.Status == AttemptStatus.InProgress.ToCode() && DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitPlacementInternalAsync(placementId, userId, isAutoSubmit: true, cancellationToken);
            // Reload after auto-submit
            placement = await db.PlacementTests
                .Include(p => p.ExamAttempt).ThenInclude(a => a!.Exam)
                .Include(p => p.Cert)
                .SingleAsync(p => p.PlacementId == placementId && p.UserId == userId, cancellationToken);
            attempt = placement.ExamAttempt!;
        }

        AttemptTakeView take;
        try
        {
            take = await _attemptEngine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder, cancellationToken);
            if (await AdvanceAtLockBoundaryAsync(take, userId, questionDisplayOrder, cancellationToken))
            {
                take = await _attemptEngine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder, cancellationToken);
            }
        }
        catch (AttemptEngineException ex)
        {
            throw MapPlacementException(ex);
        }

        return new PlacementTakeView(
            placement.PlacementId,
            take.AttemptId,
            take.ExamId,
            take.ExamName,
            placement.Cert?.Code ?? string.Empty,
            EnumCodes.Parse<PlacementStatus>(placement.Status),
            take.StartedAt,
            take.ExpiresAt,
            take.RemainingSeconds,
            take.TotalQuestions,
            take.AnsweredCount,
            take.FlaggedCount,
            take.CurrentQuestion,
            take.Navigator.ToList(),
            take.IsAutoSubmitted,
            take.Questions,
            take.Phases);
    }

    /// <summary>
    /// A placement sitting has no separate "next section" step: its Next control asks for the
    /// following question. On the last question of a locked part (a skill, or a HYBRID_GROUP_LOCKED
    /// question group inside one) that is the first question of the next locked part, which the
    /// engine refuses while the attempt is still on the earlier one. That request, and only that
    /// one, moves the attempt forward (what the mock exam's explicit skill and group transitions
    /// do), so the candidate can leave Listening, or the first writing task. A rewind into a
    /// finished part, or a jump past the next part's first question, stays refused.
    /// </summary>
    private async Task<bool> AdvanceAtLockBoundaryAsync(
        AttemptTakeView take, int userId, int? requestedDisplayOrder, CancellationToken cancellationToken)
    {
        if (requestedDisplayOrder is not int requested
            || requested == take.CurrentQuestion.DisplayOrder
            || take.Status != AttemptStatus.InProgress
            || take.ActiveSkillId is not int activeSkillId
            || take.Questions is not { Count: > 0 } questions
            || take.Phases is not { Count: > 0 } phases)
        {
            return false;
        }

        var ordered = phases.OrderBy(p => p.SkillDisplayOrder).ToList();
        var activeIndex = ordered.FindIndex(p => p.SkillId == activeSkillId);
        if (activeIndex < 0)
        {
            return false;
        }

        // Inside the active skill the next locked group comes first; the next skill opens only from
        // the last one, so a jump to it cannot skip a group.
        if (take.ActiveGroupId is int activeGroupId)
        {
            var skillQuestions = questions.Where(q => q.SkillCode == ordered[activeIndex].SkillCode).ToList();
            var endOfActiveGroup = skillQuestions.Where(q => q.GroupId == activeGroupId)
                .Select(q => q.DisplayOrder).DefaultIfEmpty(0).Max();
            var nextGroup = skillQuestions
                .Where(q => q.DisplayOrder > endOfActiveGroup && q.GroupId is not null
                    && string.Equals(q.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase))
                .OrderBy(q => q.DisplayOrder)
                .FirstOrDefault();
            if (nextGroup is { GroupId: int nextGroupId })
            {
                if (nextGroup.DisplayOrder != requested)
                {
                    return false;
                }

                // The target group is absolute, so a racing duplicate request repeats this move.
                await _attemptEngine.TransitionGroupAsync(take.AttemptId, nextGroupId, userId, cancellationToken);
                return true;
            }
        }

        if (activeIndex + 1 >= ordered.Count)
        {
            return false;
        }

        var nextSkill = ordered[activeIndex + 1].SkillCode;
        var firstOfNextSkill = questions
            .Where(q => q.SkillCode == nextSkill)
            .OrderBy(q => q.DisplayOrder)
            .FirstOrDefault();
        if (firstOfNextSkill?.DisplayOrder != requested)
        {
            return false;
        }

        // #276: stage two is entered HERE, at the stage-one/stage-two boundary - the moment the learner
        // leaves a skill. The stage-one score for the skill about to close only exists once its items are
        // behind the learner, and the routed pair is recorded on the attempt so stage two's paper is a
        // fact about this sitting rather than a number recomputed at every read.
        //
        // Before the transition, because the transition may submit the attempt on the last skill and
        // RecordStageTwoRouteAsync refuses a sitting that is no longer open. A repeated boundary click
        // re-records nothing: the engine returns false when the row already exists.
        await RecordStageTwoRoutingAsync(take.AttemptId, activeSkillId, cancellationToken);

        // Safe under a duplicate request: the engine derives the next skill from the attempt's
        // active one, so a racing second advance repeats this one instead of skipping a skill, and
        // it is never reached with the last skill active, so it never submits the attempt.
        await _attemptEngine.TransitionToNextSkillAsync(take.AttemptId, userId, cancellationToken);
        return true;
    }

    public async Task<SaveAttemptAnswerResult> SaveAnswerAsync(
        int userId,
        int placementId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
        CancellationToken cancellationToken = default,
        string? responseText = null,
        string? selectedOptionIdsJson = null)
    {
        var placement = await db.PlacementTests
            .Include(p => p.ExamAttempt)
            .SingleOrDefaultAsync(p => p.PlacementId == placementId && p.UserId == userId, cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        if (placement.Status != PlacementStatus.InProgress.ToCode())
        {
            throw new PlacementException(PlacementException.PlacementNotActive, "Placement test is not in progress.");
        }

        var attempt = placement.ExamAttempt
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Exam attempt for placement test {placementId} not found.");

        try
        {
            return await _attemptEngine.SaveAnswerAsync(
                attempt.AttemptId,
                userId,
                questionId,
                selectedOptionId,
                isFlagged,
                // #191: no onAutoSubmitted callback. The engine closes the linked placement when it
                // scores the attempt, and this callback used to write that same snapshot a second
                // time. A no-op callback that still exists reads as "there is something to sync".
                onAutoSubmitted: null,
                cancellationToken,
                responseText,
                selectedOptionIdsJson);
        }
        catch (AttemptEngineException ex) when (ex.Code == AttemptEngineException.AttemptExpired)
        {
            throw new PlacementException(PlacementException.PlacementExpired, "Placement test time has expired.");
        }
        catch (AttemptEngineException ex)
        {
            throw MapPlacementException(ex);
        }
    }

    public async Task<SaveAttemptDraftResult> SaveDraftAsync(
        int userId,
        int placementId,
        int questionId,
        string? responseText,
        CancellationToken cancellationToken = default)
    {
        var placement = await db.PlacementTests
            .Include(p => p.ExamAttempt)
            .SingleOrDefaultAsync(p => p.PlacementId == placementId && p.UserId == userId, cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        if (placement.Status != PlacementStatus.InProgress.ToCode())
        {
            throw new PlacementException(PlacementException.PlacementNotActive, "Placement test is not in progress.");
        }

        var attempt = placement.ExamAttempt
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Exam attempt for placement test {placementId} not found.");

        try
        {
            return await _attemptEngine.SaveDraftAsync(attempt.AttemptId, userId, questionId, responseText, cancellationToken);
        }
        catch (AttemptEngineException ex)
        {
            throw MapPlacementException(ex);
        }
    }

    public async Task<PlacementGradingStatusView> GetGradingStatusAsync(int userId, int placementId, CancellationToken cancellationToken = default)
    {
        var placement = await db.PlacementTests
            .Include(p => p.ExamAttempt)
            .Include(p => p.Cert)
            .SingleOrDefaultAsync(p => p.PlacementId == placementId && p.UserId == userId, cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        var attempt = placement.ExamAttempt
            ?? await db.ExamAttempts.SingleOrDefaultAsync(a => a.AttemptId == placement.AttemptId, cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Exam attempt for placement test {placementId} not found.");

        var (freeResponseCount, gradedFreeResponseCount, failedFreeResponseCount, isGraded) =
            await GetAttemptGradingCountsAsync(attempt.AttemptId, attempt.Status, cancellationToken);

        var skillScores = await db.AttemptSkillScores
            .Where(s => s.AttemptId == attempt.AttemptId)
            .Include(s => s.Skill)
            .OrderBy(s => s.Skill.DisplayOrder)
            .ToListAsync(cancellationToken);

        // #269: a pool paper's answers have no question row, so the per-skill total is counted from each
        // answer's own row. It used to read `a.Question.Section.SkillId` and threw
        // NullReferenceException on the first pool answer.
        var skillByAnswer = await RosterSkills.ByAnswerAsync(db, attempt.AttemptId, cancellationToken);
        var answerIds = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.AttemptId == attempt.AttemptId)
            .Select(a => a.AnswerId)
            .ToListAsync(cancellationToken);

        var skillInfos = skillScores.Select(sc =>
        {
            int totalInSkill = answerIds.Count(
                id => skillByAnswer.GetValueOrDefault(id) == sc.SkillId);
            return new PlacementSkillScoreInfo(
                sc.SkillId,
                sc.Skill.Code,
                sc.Skill.Name,
                sc.RawScore,
                totalInSkill,
                sc.ScaledScore,
                attempt.BandCode);
        }).ToList();

        string stageDescriptor;
        if (isGraded)
        {
            stageDescriptor = "Complete";
        }
        else if (gradedFreeResponseCount == 0)
        {
            stageDescriptor = "Evaluating grammar structure...";
        }
        else if (gradedFreeResponseCount < freeResponseCount)
        {
            stageDescriptor = "Checking coherence & task achievement...";
        }
        else
        {
            stageDescriptor = "Compiling mentor report...";
        }

        return new PlacementGradingStatusView(
            placement.PlacementId,
            attempt.AttemptId,
            isGraded,
            freeResponseCount,
            gradedFreeResponseCount,
            failedFreeResponseCount,
            attempt.TotalScore,
            attempt.BandCode,
            null,
            skillInfos,
            stageDescriptor);
    }

    /// <summary>
    /// #217 / PLC-35: a rejected grading is retried by re-queueing the same <c>AiGradings</c> row. It
    /// reuses the row rather than creating one, and it writes no ledger row: placement is free and a
    /// retry is the system's fault, not a second attempt the learner pays for.
    /// <para>
    /// Only a FAILED grading is retried. A pending one is already in the queue (retrying it would
    /// double-dispatch the same job) and a done one must not be re-scored; both are idle no-ops here,
    /// which is what makes a double-click harmless.
    /// </para>
    /// </summary>
    public async Task<PlacementRetryResult> RetryGradingAsync(
        int userId,
        int placementId,
        CancellationToken cancellationToken = default)
    {
        var placement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.PlacementId == placementId && p.UserId == userId)
            .Select(p => new { p.AttemptId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        if (placement.AttemptId is not int attemptId)
        {
            throw new PlacementException(PlacementException.PlacementNotFound, $"Exam attempt for placement test {placementId} not found.");
        }

        var failedGradings = await db.AiGradings
            .Where(g => g.FreeResponse.AttemptId == attemptId && g.Status == AiGradingStatus.Failed.ToCode())
            .ToListAsync(cancellationToken);

        if (failedGradings.Count == 0)
        {
            throw new PlacementException(
                PlacementException.RetryNotAllowed,
                "No failed grading to retry on this placement.");
        }

        var now = DateTimeOffset.UtcNow;
        var responseIds = failedGradings.Select(g => g.FreeResponseId).ToList();
        foreach (var grading in failedGradings)
        {
            grading.Status = AiGradingStatus.Pending.ToCode();
            grading.AttemptCount += 1;
            grading.FailureReason = null;
            grading.CompletedAt = null;
        }

        // The attempt moves back to `submitted` FIRST, and the responses follow. The schema refuses a
        // pending response inside a `graded` attempt (trg_FreeResponses_NotIntoGradedAttempt), so the
        // order is not cosmetic - the other way round is a refused write. It is also the truth: the
        // productive half is being re-measured, so the sitting is open again.
        await db.ExamAttempts
            .Where(a => a.AttemptId == attemptId && a.Status == AttemptStatus.Graded.ToCode())
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AttemptStatus.Submitted.ToCode()));

        await db.FreeResponses
            .Where(fr => responseIds.Contains(fr.FreeResponseId))
            .ExecuteUpdateAsync(s => s.SetProperty(fr => fr.Status, FreeResponseStatus.PendingAi.ToCode()));

        await db.SaveChangesAsync(cancellationToken);

        // The per-skill rows are deliberately NOT rewritten here. Re-queueing a task does not change
        // what was measured, and rewriting them would flip a finished partial result back to
        // `in_progress` while the job is merely queued again. The writer runs on its own when the
        // retried grading settles (AttemptEngine.FinalizeAiGradedAttemptAsync), which is the one path
        // that turns `not_measured` into a measurement.

        return new PlacementRetryResult(placementId, attemptId, failedGradings.Count);
    }

    public async Task<PlacementResultView> SubmitPlacementTestAsync(int userId, int placementId, bool isAutoSubmit, CancellationToken cancellationToken = default)
    {
        return await SubmitPlacementInternalAsync(placementId, userId, isAutoSubmit, cancellationToken);
    }

    public async Task<PlacementResultView> GetPlacementResultAsync(int userId, int placementId, CancellationToken cancellationToken = default)
    {
        var placement = await db.PlacementTests
            .Include(p => p.ExamAttempt).ThenInclude(a => a!.Exam)
            .Include(p => p.Cert)
            .SingleOrDefaultAsync(p => p.PlacementId == placementId && p.UserId == userId, cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        if (placement.Status == PlacementStatus.InProgress.ToCode() && placement.ExamAttempt is not null && DateTimeOffset.UtcNow >= placement.ExamAttempt.ExpiresAt)
        {
            return await SubmitPlacementInternalAsync(placementId, userId, isAutoSubmit: true, cancellationToken);
        }

        if (placement.Status == PlacementStatus.Abandoned.ToCode())
        {
            // #217 / PLC-19: an abandoned sitting has no result to show. This must be refused BEFORE
            // the "still grading" branch below, or a sitting that measured nothing would render a
            // result page whose every skill is not_measured - the fabricated result criterion 3 names.
            throw new PlacementException(
                PlacementException.PlacementAbandoned,
                "Nothing on this placement could be measured, so it was recorded as abandoned and has no result.");
        }

        if (placement.Status != PlacementStatus.Done.ToCode())
        {
            // #217 / PLC-34: a sitting whose attempt was already submitted is GRADING, not unfinished.
            // Its objective skills are measured and visible now; only the productive tasks are still in
            // the queue. Returning the view (a pure read - PLC-16) is what lets the result page show the
            // objective half immediately and fill the productive half in as each task returns.
            //
            // A sitting whose attempt is still `in_progress` has nothing to show: the learner is still
            // answering, and a result page for it would be a screen with no facts.
            var attemptSubmitted = placement.ExamAttempt is { } attempt
                && attempt.Status != AttemptStatus.InProgress.ToCode()
                && attempt.Status != AttemptStatus.Abandoned.ToCode();

            if (!attemptSubmitted)
            {
                throw new PlacementException(PlacementException.PlacementNotFinished, "Placement test is not finished.");
            }
        }

        return await BuildPlacementResultViewAsync(placement, cancellationToken);
    }

    public async Task<TargetBandView> GetTargetBandViewAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new CertificateOption(c.Code, c.Name, c.SectionNoun))
            .SingleAsync(cancellationToken);

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        string? currentBandCode = latestPlacement?.ResultBandCode;
        int? currentTotalScore = latestPlacement?.TotalScore;
        string? currentBandName = null;

        var scoreBands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == certId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrEmpty(currentBandCode))
        {
            currentBandName = scoreBands.FirstOrDefault(b => b.Code == currentBandCode)?.Name;
        }

        var savedTarget = await db.TargetBands
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.CertId == certId)
            .FirstOrDefaultAsync(cancellationToken);

        string? targetBandCode = savedTarget?.TargetBandCode;

        var cefrBands = scoreBands.Select(b => new CefrBandInfo(
            b.Code,
            b.Name,
            b.MinTotal,
            b.MaxTotal,
            currentTotalScore.HasValue && currentTotalScore.Value >= b.MinTotal,
            b.Code == currentBandCode,
            b.Code == targetBandCode)).ToList();

        var targetOptions = new List<TargetBandOption>();
        foreach (var band in scoreBands)
        {
            string description = $"{band.MinTotal} to {band.MaxTotal} points";
            if (currentTotalScore.HasValue)
            {
                if (band.MinTotal > currentTotalScore.Value)
                {
                    int diff = band.MinTotal - currentTotalScore.Value;
                    description = $"{band.MinTotal} to {band.MaxTotal} points · Around {diff} points away.";
                }
                else if (band.Code == currentBandCode)
                {
                    description = $"{band.MinTotal} to {band.MaxTotal} points · Your current level.";
                }
            }

            bool isCurrent = band.Code == currentBandCode;
            bool isTarget = band.Code == targetBandCode;

            targetOptions.Add(new TargetBandOption(
                band.Code,
                band.Name,
                band.MinTotal,
                band.MaxTotal,
                description,
                isCurrent,
                isTarget));
        }

        return new TargetBandView(
            cert,
            currentBandCode,
            currentTotalScore,
            currentBandName,
            targetBandCode,
            cefrBands,
            targetOptions,
            await GetGoalStatusAsync(userId, cancellationToken));
    }

    public async Task<SaveTargetBandResult> SetTargetBandAsync(int userId, string targetBandCode, CancellationToken cancellationToken = default)
    {
        // #153: the goal is now five columns, so the old band-only writer delegates rather than
        // keeping a second, unvalidated way to write the row. A goal set this way has no deadline,
        // and therefore no verdict — which is legal and is not "unrealistic".
        var saved = await SetGoalAsync(userId, currentBandCode: null, targetBandCode, targetDate: null, weeklyStudyHours: null, cancellationToken);
        return new SaveTargetBandResult(saved.UserId, saved.CertId, saved.TargetBandCode, saved.SetAt);
    }

    public async Task<SaveGoalResult> SetGoalAsync(
        int userId,
        string? currentBandCode,
        string targetBandCode,
        DateOnly? targetDate,
        decimal? weeklyStudyHours,
        CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var bands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == certId)
            .ToDictionaryAsync(b => b.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        if (!bands.ContainsKey(targetBandCode))
        {
            throw new PlacementException(PlacementException.TargetBandInvalid, $"Band code '{targetBandCode}' is not valid for this certificate.");
        }

        // The starting band falls back to the learner's own assessed band, so the onboarding screen
        // does not have to know it, but an explicit band must still be a band of this certificate.
        var fromBandCode = currentBandCode;
        if (!string.IsNullOrWhiteSpace(fromBandCode) && !bands.ContainsKey(fromBandCode))
        {
            throw new PlacementException(PlacementException.TargetBandInvalid, $"Band code '{fromBandCode}' is not valid for this certificate.");
        }
        fromBandCode ??= await LatestAssessedBandCodeAsync(userId, certId, cancellationToken);

        // Half a goal cannot be judged: a date without a pace has no availability to compare with.
        if (targetDate.HasValue != weeklyStudyHours.HasValue)
        {
            throw new PlacementException(
                PlacementException.TargetDateInvalid,
                "A goal needs both an exam date and a weekly study pace, or neither.");
        }

        if (targetDate.HasValue && targetDate.Value < today)
        {
            throw new PlacementException(
                PlacementException.TargetDateInvalid,
                $"The exam date {targetDate.Value:yyyy-MM-dd} has already passed. Pick a date from today onwards.");
        }

        if (weeklyStudyHours is { } pace && (pace <= 0 || pace > GoalFeasibilityCalculator.MaxWeeklyHours))
        {
            throw new PlacementException(
                PlacementException.WeeklyHoursTooHigh,
                $"A week has to hold between 0 and {GoalFeasibilityCalculator.MaxWeeklyHours:0} hours of study; {pace:0.#} is not a pace anyone can keep.");
        }

        int? requiredHours = null;
        string? status = null;
        GoalFeasibility? feasibility = null;
        if (targetDate is { } date && weeklyStudyHours is { } hours)
        {
            var currentHours = fromBandCode is not null && bands.TryGetValue(fromBandCode, out var fromBand)
                ? fromBand.CumulativeStudyHours
                : null;

            feasibility = GoalFeasibilityCalculator.Evaluate(
                currentHours, bands[targetBandCode].CumulativeStudyHours, date, hours, today);

            requiredHours = feasibility?.RequiredHours;
            status = feasibility?.Status.ToCode();
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await db.TargetBands
            .FirstOrDefaultAsync(t => t.UserId == userId && t.CertId == certId, cancellationToken);

        if (existing is not null)
        {
            existing.TargetBandCode = targetBandCode;
            existing.CurrentBandCode = fromBandCode;
            existing.TargetDate = targetDate;
            existing.WeeklyStudyHours = weeklyStudyHours;
            existing.RequiredTotalHours = requiredHours;
            existing.FeasibilityStatus = status;      // NULL clears a stale verdict
            existing.SetAt = now;
        }
        else
        {
            db.TargetBands.Add(new TargetBand
            {
                UserId = userId,
                CertId = certId,
                TargetBandCode = targetBandCode,
                CurrentBandCode = fromBandCode,
                TargetDate = targetDate,
                WeeklyStudyHours = weeklyStudyHours,
                RequiredTotalHours = requiredHours,
                FeasibilityStatus = status,
                SetAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var concurrent = await db.TargetBands
                .FirstOrDefaultAsync(t => t.UserId == userId && t.CertId == certId, cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            concurrent.TargetBandCode = targetBandCode;
            concurrent.CurrentBandCode = fromBandCode;
            concurrent.TargetDate = targetDate;
            concurrent.WeeklyStudyHours = weeklyStudyHours;
            concurrent.RequiredTotalHours = requiredHours;
            concurrent.FeasibilityStatus = status;
            concurrent.SetAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        var goalStatus = BuildGoalStatus(
            fromBandCode, targetBandCode, targetDate, weeklyStudyHours, requiredHours, feasibility);

        return new SaveGoalResult(userId, certId, targetBandCode, now, goalStatus);
    }

    public async Task<GoalStatus> GetGoalStatusAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var saved = await db.TargetBands
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.CertId == certId, cancellationToken);

        if (saved is null)
        {
            return new GoalStatus(null, "", null, null, null, null,
                null, null, null);
        }

        FeasibilityStatus? status = null;
        int? suggested = null;
        DateOnly? earliest = null;
        decimal? available = null;
        if (saved.FeasibilityStatus is not null)
        {
            // Re-read the verdict from the stored code; the two numbers are re-derived so the screen
            // never shows a total that no longer matches the bands (an admin may have re-bandered).
            var bands = await db.ScoreBands
                .AsNoTracking()
                .Where(b => b.CertId == certId)
                .ToDictionaryAsync(b => b.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var fromHours = saved.CurrentBandCode is not null && bands.TryGetValue(saved.CurrentBandCode, out var fromBand)
                ? fromBand.CumulativeStudyHours
                : null;
            var toHours = bands.TryGetValue(saved.TargetBandCode, out var toBand) ? toBand.CumulativeStudyHours : null;

            if (saved.TargetDate is { } date && saved.WeeklyStudyHours is { } hours)
            {
                var feasibility = GoalFeasibilityCalculator.Evaluate(fromHours, toHours, date, hours, today);
                if (feasibility is not null)
                {
                    status = feasibility.Status;
                    suggested = feasibility.SuggestedWeeklyHours;
                    earliest = feasibility.EarliestFeasibleDate;
                    available = feasibility.AvailableHours;
                }
            }
        }

        return new GoalStatus(
            saved.CurrentBandCode,
            saved.TargetBandCode,
            saved.TargetDate,
            saved.WeeklyStudyHours,
            saved.RequiredTotalHours,
            available,
            status,
            suggested,
            earliest);
    }

    /// <summary>#153: the band the learner was last assessed at — the placement result, or failing
    /// that a graded mock. Null when they have never been assessed, which is not an error: the goal
    /// simply cannot be priced from a starting band the system does not know.</summary>
    private async Task<string?> LatestAssessedBandCodeAsync(int userId, int certId, CancellationToken cancellationToken)
    {
        var placementBand = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .Select(p => p.ResultBandCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrEmpty(placementBand))
        {
            return placementBand;
        }

        // #311: Cross-certificate mapping from the learner's Universal Placement Exam OverallPct
        var latestUniversalPct = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == PlacementStatus.Done.ToCode() && p.OverallPct != null)
            .OrderByDescending(p => p.TakenAt)
            .Select(p => p.OverallPct)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestUniversalPct is { } pct)
        {
            var certBands = await db.ScoreBands
                .AsNoTracking()
                .Where(b => b.CertId == certId)
                .OrderBy(b => b.MinTotal)
                .ToListAsync(cancellationToken);
            if (certBands.Count > 0)
            {
                return MapOverallPctToBand(pct, certBands).Band.Code;
            }
        }

        return await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.CertId == certId
                && a.Status == AttemptStatus.Graded.ToCode()
                && a.Exam.Purpose == ExamPurpose.Mock.ToCode()
                && a.BandCode != null)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .Select(a => a.BandCode)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static GoalStatus BuildGoalStatus(
        string? currentBandCode,
        string targetBandCode,
        DateOnly? targetDate,
        decimal? weeklyStudyHours,
        int? requiredHours,
        GoalFeasibility? feasibility) =>
        new(
            currentBandCode,
            targetBandCode,
            targetDate,
            weeklyStudyHours,
            requiredHours,
            feasibility?.AvailableHours,
            feasibility?.Status,
            feasibility?.SuggestedWeeklyHours,
            feasibility?.EarliestFeasibleDate);

    private async Task<PlacementResultView> SubmitPlacementInternalAsync(
        int placementId,
        int userId,
        bool isAutoSubmit,
        CancellationToken cancellationToken)
    {
        var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var placement = await db.PlacementTests
                .Include(p => p.ExamAttempt).ThenInclude(a => a!.Exam)
                .Include(p => p.Cert)
                .SingleOrDefaultAsync(p => p.PlacementId == placementId && p.UserId == userId, cancellationToken)
                ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

            if (placement.Status == PlacementStatus.Done.ToCode())
            {
                if (tx is not null) await tx.RollbackAsync(cancellationToken);
                return await BuildPlacementResultViewAsync(placement, cancellationToken);
            }

            if (placement.Status == PlacementStatus.Abandoned.ToCode())
            {
                throw new PlacementException(PlacementException.PlacementNotActive, "This placement test was abandoned.");
            }

            var attempt = placement.ExamAttempt
                ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Exam attempt for placement test {placementId} not found.");

            AttemptGradingResult grading;
            try
            {
                grading = await _attemptEngine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit, cancellationToken);
            }
            catch (AttemptEngineException ex)
            {
                throw MapPlacementException(ex);
            }

            // Snapshot matching invariant: PlacementTests.TotalScore must match ExamAttempts.TotalScore
            // #191 / D14: and the snapshot has exactly one writer. This method used to write it a
            // second time, unconditionally, after `AttemptEngine.SubmitAttemptAsync` had already done
            // so - which is the confirmed defect in docs/research/placement-free-response-defect.md:
            // a placement carrying a free response got `done` with a null band because the band was
            // not there yet. The snapshot is derived from the attempt, so the attempt's writer is the
            // only one that may produce it.
            //
            // Nothing to write here. The attempt engine closed the linked placement when it scored the
            // attempt; if the attempt is still `submitted` because a free response is being graded, the
            // placement correctly stays `in_progress` and the screen says "still grading".

            await db.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);

            // #217 / PLC-19, criterion 3: the engine's writer marks a placement `abandoned` when this
            // sitting measured nothing. Re-reading is deliberate - the status is the writer's decision,
            // not a second judgement made here - and returning a result for it would hand back the
            // fabricated figure the ticket forbids.
            var finished = await db.PlacementTests
                .AsNoTracking()
                .Where(p => p.PlacementId == placementId)
                .Select(p => p.Status)
                .SingleAsync(cancellationToken);

            if (finished == PlacementStatus.Abandoned.ToCode())
            {
                throw new PlacementException(
                    PlacementException.PlacementAbandoned,
                    "Nothing on this placement could be measured, so it was recorded as abandoned and has no result. "
                    + "You can start another one whenever you are ready.");
            }

            return await BuildPlacementResultViewAsync(placement, cancellationToken);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    private async Task<PlacementResultView> BuildPlacementResultViewAsync(
        PlacementTest placement,
        CancellationToken cancellationToken)
    {
        var attempt = placement.ExamAttempt
            ?? await db.ExamAttempts.Include(a => a.Exam).SingleAsync(a => a.AttemptId == placement.AttemptId, cancellationToken);

        var skillScores = await db.AttemptSkillScores
            .Where(s => s.AttemptId == attempt.AttemptId)
            .Include(s => s.Skill)
            .OrderBy(s => s.Skill.DisplayOrder)
            .ToListAsync(cancellationToken);

        // #269: same rule as the grading-status page - the per-skill total comes from each answer's own
        // row, because a pool answer has no question row to read a section from.
        var skillByAnswer = await RosterSkills.ByAnswerAsync(db, attempt.AttemptId, cancellationToken);
        var answerIds = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.AttemptId == attempt.AttemptId)
            .Select(a => a.AnswerId)
            .ToListAsync(cancellationToken);

        var bands = await db.ScoreBands
            .Where(b => b.CertId == attempt.CertId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        // #152: how many stored units make one reported point on this certificate. The page divides
        // every score by it, so an IELTS placement reads 6.5 where the column holds 65.
        var scoreDisplayFactor = AttemptEngine.DisplayFactor(
            await db.Certificates
                .Where(c => c.CertId == attempt.CertId)
                .Select(c => c.ScoreAggregationRule)
                .SingleAsync(cancellationToken));

        int totalScore = placement.TotalScore ?? attempt.TotalScore ?? 0;
        string resultBandCode = placement.ResultBandCode ?? attempt.BandCode ?? "";
        string resultBandName = bands.FirstOrDefault(b => b.Code == resultBandCode)?.Name ?? resultBandCode;

        var skillInfos = new List<PlacementSkillScoreInfo>();
        foreach (var sc in skillScores)
        {
            int totalInSkill = answerIds.Count(
                id => skillByAnswer.GetValueOrDefault(id) == sc.SkillId);
            skillInfos.Add(new PlacementSkillScoreInfo(
                sc.SkillId,
                sc.Skill.Code,
                sc.Skill.Name,
                sc.RawScore,
                totalInSkill,
                sc.ScaledScore,
                resultBandCode));
        }

        // #213 / story 55: the per-skill rows are READ, not derived here. They are the one writer's
        // product, and they are the only place a carried-over result exists: its skill has no
        // AttemptSkillScore (it was never sat), so a page built only from the scores above would omit
        // the very number the learner is being shown.
        var skillResults = await db.PlacementSkillResults
            .AsNoTracking()
            .Where(r => r.PlacementId == placement.PlacementId)
            .Join(db.Skills.AsNoTracking(), r => r.SkillId, s => s.SkillId, (r, s) => new { r, s })
            .OrderBy(x => x.s.DisplayOrder).ThenBy(x => x.s.SkillId)
            .Select(x => new PlacementSkillResultInfo(
                x.r.SkillId,
                x.s.Code,
                x.s.Name,
                x.r.ResultStatus,
                x.r.CefrLevel,
                x.r.PositionInBand,
                x.r.MeasuredAt,
                x.r.ReusedFromPrior,
                x.r.FailureReason))
            .ToListAsync(cancellationToken);

        // Weaker section analysis: compare percentage of raw scores
        string? weakerSkillName = null;
        if (skillInfos.Count >= 2)
        {
            var lowest = skillInfos
                .OrderBy(s => s.TotalQuestions > 0 ? (double)s.RawScore / s.TotalQuestions : 0)
                .First();
            weakerSkillName = lowest.SkillName;
        }

        // Cefr Bands for visualization
        var cefrBands = bands.Select(b => new CefrBandInfo(
            b.Code,
            b.Name,
            b.MinTotal,
            b.MaxTotal,
            totalScore >= b.MinTotal,
            b.Code == resultBandCode)).ToList();

        // Calculate band note
        string? bandNote = null;
        var currentBand = bands.FirstOrDefault(b => b.Code == resultBandCode);
        if (currentBand is not null)
        {
            var subsequentBands = bands.Where(b => b.MinTotal > currentBand.MaxTotal).OrderBy(b => b.MinTotal).ToList();
            if (subsequentBands.Count >= 2)
            {
                int gap1 = subsequentBands[0].MinTotal - totalScore;
                int gap2 = subsequentBands[1].MinTotal - totalScore;
                bandNote = $"{currentBand.Name} spans {currentBand.MinTotal}–{currentBand.MaxTotal} points. You are {gap1} points below {subsequentBands[0].Name} and {gap2} below {subsequentBands[1].Name}.";
            }
            else if (subsequentBands.Count == 1)
            {
                int gap1 = subsequentBands[0].MinTotal - totalScore;
                bandNote = $"{currentBand.Name} spans {currentBand.MinTotal}–{currentBand.MaxTotal} points. You are {gap1} points below {subsequentBands[0].Name}.";
            }
            else
            {
                bandNote = $"You reached the highest band {currentBand.Name}!";
            }
        }

        var duration = attempt.SubmittedAt.HasValue
            ? Math.Max(1, (int)(attempt.SubmittedAt.Value - attempt.StartedAt).TotalMinutes)
            : attempt.Exam.DurationMinutes;

        var (freeResponseCount, gradedFreeResponseCount, failedFreeResponseCount, isGraded) =
            await GetAttemptGradingCountsAsync(attempt.AttemptId, attempt.Status, cancellationToken);

        var resultScope = EnumCodes.Parse<PlacementScope>(placement.Scope);

        // #311: Re-read diagnostic percentages written by PlacementResultWriter
        var pctSnapshot = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.PlacementId == placement.PlacementId)
            .Select(p => new { p.ListeningPct, p.ReadingPct, p.SpeakingPct, p.WritingPct, p.OverallPct })
            .SingleAsync(cancellationToken);

        List<CertificateBandPreview>? certificatePreviews = null;
        if (pctSnapshot.OverallPct is { } overallPct)
        {
            var activeCerts = await db.Certificates
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.CertId)
                .Select(c => new
                {
                    c.CertId,
                    c.Code,
                    c.Name,
                    c.ScoreAggregationRule,
                    SkillCount = db.Skills.Count(s => s.CertId == c.CertId),
                })
                .ToListAsync(cancellationToken);

            var allBands = await db.ScoreBands
                .AsNoTracking()
                .OrderBy(b => b.CertId)
                .ThenBy(b => b.MinTotal)
                .ToListAsync(cancellationToken);
            var bandsByCert = allBands.GroupBy(b => b.CertId).ToDictionary(g => g.Key, g => g.ToList());

            certificatePreviews = new List<CertificateBandPreview>(activeCerts.Count);
            foreach (var cert in activeCerts)
            {
                if (!bandsByCert.TryGetValue(cert.CertId, out var certBands) || certBands.Count == 0)
                {
                    continue;
                }

                var (mappedScore, mappedBand) = MapOverallPctToBand(overallPct, certBands);
                certificatePreviews.Add(new CertificateBandPreview(
                    cert.CertId,
                    cert.Code,
                    cert.Name,
                    cert.SkillCount,
                    mappedScore,
                    mappedBand.Code,
                    mappedBand.Name,
                    AttemptEngine.DisplayFactor(cert.ScoreAggregationRule)));
            }
        }

        return new PlacementResultView(
            placement.PlacementId,
            attempt.AttemptId,
            attempt.CertId,
            placement.Cert?.Code ?? "",
            attempt.Exam?.Name ?? "Placement Test",
            placement.TakenAt,
            duration,
            answerIds.Count,
            totalScore,
            resultBandCode,
            resultBandName,
            cefrBands,
            skillInfos,
            weakerSkillName,
            bandNote,
            attempt.IsAutoSubmitted,
            freeResponseCount,
            gradedFreeResponseCount,
            failedFreeResponseCount,
            scoreDisplayFactor,
            isGraded)
        {
            SkillResults = skillResults,
            Scope = resultScope,
            ListeningPct = pctSnapshot.ListeningPct,
            ReadingPct = pctSnapshot.ReadingPct,
            SpeakingPct = pctSnapshot.SpeakingPct,
            WritingPct = pctSnapshot.WritingPct,
            OverallPct = pctSnapshot.OverallPct,
            CertificatePreviews = certificatePreviews,
        };
    }

    private static (int MappedScore, ScoreBand Band) MapOverallPctToBand(
        decimal overallPct,
        IReadOnlyList<ScoreBand> orderedBands)
    {
        var minScale = orderedBands.Min(b => b.MinTotal);
        var maxScale = orderedBands.Max(b => b.MaxTotal);
        var clampedPct = Math.Clamp(overallPct, 0m, 100m);
        var mappedScore = (int)Math.Round(minScale + (clampedPct / 100m) * (maxScale - minScale));
        mappedScore = Math.Clamp(mappedScore, minScale, maxScale);

        var matched = orderedBands.FirstOrDefault(b => mappedScore >= b.MinTotal && mappedScore <= b.MaxTotal)
            ?? orderedBands
                .OrderBy(b => Math.Abs(((b.MinTotal + b.MaxTotal) / 2.0) - mappedScore))
                .First();

        return (mappedScore, matched);
    }
    private async Task<(int Total, int Graded, int Failed, bool IsGraded)> GetAttemptGradingCountsAsync(
        int attemptId,
        string attemptStatus,
        CancellationToken cancellationToken)
    {
        var gradingStates = await db.AiGradings
            .AsNoTracking()
            .Where(g => g.FreeResponse.AttemptId == attemptId)
            .Select(g => g.Status)
            .ToListAsync(cancellationToken);

        var total = await db.FreeResponses
            .CountAsync(fr => fr.AttemptId == attemptId, cancellationToken);
        var graded = gradingStates.Count(s => s == AiGradingStatus.Done.ToCode());
        var failed = gradingStates.Count(s => s == AiGradingStatus.Failed.ToCode());
        var isGraded = attemptStatus == AttemptStatus.Graded.ToCode()
            || total == 0
            || (graded + failed >= total && total > 0);

        return (total, graded, failed, isGraded);
    }
}