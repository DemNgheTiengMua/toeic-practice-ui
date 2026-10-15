using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class PlacementException(string code, string message) : Exception(message)
{
    public const string ExamNotFound = "exam_not_found";
    public const string PlacementNotFound = "placement_not_found";
    public const string PlacementNotActive = "placement_not_active";
    public const string PlacementExpired = "placement_expired";
    public const string PlacementNotFinished = "placement_not_finished";
    public const string QuestionNotFound = "question_not_found";
    public const string InvalidOption = "invalid_option";
    public const string TargetBandInvalid = "target_band_invalid";

    public string Code { get; } = code;
}

public interface IPlacementService
{
    Task<PlacementCatalogView> GetPlacementCatalogAsync(int userId, CancellationToken cancellationToken = default);

    Task<PlacementTestInfo> StartPlacementTestAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<PlacementTakeView> GetPlacementTakeAsync(int userId, int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default);

    Task<SaveAttemptAnswerResult> SaveAnswerAsync(int userId, int placementId, int questionId, int? selectedOptionId, bool isFlagged, CancellationToken cancellationToken = default);

    Task<PlacementResultView> SubmitPlacementTestAsync(int userId, int placementId, bool isAutoSubmit, CancellationToken cancellationToken = default);

    Task<PlacementResultView> GetPlacementResultAsync(int userId, int placementId, CancellationToken cancellationToken = default);

    Task<TargetBandView> GetTargetBandViewAsync(int userId, CancellationToken cancellationToken = default);

    Task<SaveTargetBandResult> SetTargetBandAsync(int userId, string targetBandCode, CancellationToken cancellationToken = default);
}

public sealed class PlacementService(CompanionDbContext db, ILearnerService learners) : IPlacementService
{
    public async Task<PlacementCatalogView> GetPlacementCatalogAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new CertificateOption(c.Code, c.Name))
            .SingleAsync(cancellationToken);

        // Auto-submit any expired in-progress placement tests
        var expiredPlacements = await db.PlacementTests
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.InProgress.ToCode() && p.ExamAttempt != null && DateTimeOffset.UtcNow >= p.ExamAttempt.ExpiresAt)
            .Select(p => p.PlacementId)
            .ToListAsync(cancellationToken);

        foreach (var expId in expiredPlacements)
        {
            await SubmitPlacementInternalAsync(expId, userId, isAutoSubmit: true, cancellationToken);
        }

        var activePlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.InProgress.ToCode())
            .FirstOrDefaultAsync(cancellationToken);

        var latestDone = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        string? currentBandName = null;
        if (!string.IsNullOrEmpty(latestDone?.ResultBandCode))
        {
            currentBandName = await db.ScoreBands
                .AsNoTracking()
                .Where(b => b.CertId == certId && b.Code == latestDone.ResultBandCode)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var exams = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId && e.Purpose == ExamPurpose.Placement.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .OrderBy(e => e.ExamId)
            .ToListAsync(cancellationToken);

        var items = new List<PlacementCatalogItem>();
        for (int i = 0; i < exams.Count; i++)
        {
            var exam = exams[i];
            bool isRecommended = exam.Name.Contains("Standard", StringComparison.OrdinalIgnoreCase) || i == 0;
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

        return new PlacementCatalogView(
            cert,
            latestDone?.ResultBandCode,
            currentBandName,
            latestDone?.TotalScore,
            items,
            activePlacement?.PlacementId,
            activePlacement?.AttemptId);
    }

    public async Task<PlacementTestInfo> StartPlacementTestAsync(int userId, int examId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var exam = await db.Exams
            .Where(e => e.ExamId == examId && e.CertId == certId && e.Purpose == ExamPurpose.Placement.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PlacementException(PlacementException.ExamNotFound, $"Placement exam {examId} not found.");

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
                var remaining = existingPlacement.ExamAttempt is not null
                    ? Math.Max(0, (int)(existingPlacement.ExamAttempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)
                    : 0;

                return new PlacementTestInfo(
                    existingPlacement.PlacementId,
                    existingPlacement.AttemptId ?? 0,
                    existingPlacement.ExamId ?? examId,
                    EnumCodes.Parse<PlacementStatus>(existingPlacement.Status),
                    existingPlacement.ExamAttempt?.StartedAt ?? existingPlacement.TakenAt,
                    existingPlacement.ExamAttempt?.ExpiresAt ?? DateTimeOffset.UtcNow,
                    remaining,
                    existingPlacement.ExamAttempt?.IsAutoSubmitted ?? false);
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // Abandon any other in-progress placement test for this certificate and its linked attempt
        var otherPlacements = await db.PlacementTests
            .Include(p => p.ExamAttempt)
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.InProgress.ToCode())
            .ToListAsync(cancellationToken);
        foreach (var other in otherPlacements)
        {
            other.Status = PlacementStatus.Abandoned.ToCode();
            if (other.ExamAttempt != null && other.ExamAttempt.Status == AttemptStatus.InProgress.ToCode())
            {
                other.ExamAttempt.Status = AttemptStatus.Abandoned.ToCode();
            }
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
        };

        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);

        // NOTE: Placement tests are 100% free; no CreditTransactions ledger entry is ever created.

        var questions = await db.Questions
            .Where(q => q.ExamId == examId)
            .Include(q => q.Group)
            .Include(q => q.Section).ThenInclude(s => s.Skill)
            .OrderBy(q => q.Section.Skill.DisplayOrder)
            .ThenBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .ToListAsync(cancellationToken);

        if (questions.Count > 0)
        {
            attempt.LastQuestionId = questions[0].QuestionId;
            foreach (var q in questions)
            {
                db.AttemptAnswers.Add(new AttemptAnswer
                {
                    AttemptId = attempt.AttemptId,
                    QuestionId = q.QuestionId,
                    ExamId = examId,
                    SelectedOptionId = null,
                    IsCorrect = null,
                    IsFlagged = false,
                });
            }
        }

        var placement = new PlacementTest
        {
            UserId = userId,
            CertId = certId,
            ExamId = examId,
            AttemptId = attempt.AttemptId,
            Status = PlacementStatus.InProgress.ToCode(),
            ExamPurpose = ExamPurpose.Placement.ToCode(),
            TakenAt = now,
            TotalScore = null,
            ResultBandCode = null,
        };

        db.PlacementTests.Add(placement);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var remainingSeconds = Math.Max(0, (int)(expiresAt - now).TotalSeconds);
        return new PlacementTestInfo(
            placement.PlacementId,
            attempt.AttemptId,
            examId,
            EnumCodes.Parse<PlacementStatus>(placement.Status),
            attempt.StartedAt,
            attempt.ExpiresAt,
            remainingSeconds,
            attempt.IsAutoSubmitted);
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
        if (placement.Status == PlacementStatus.InProgress.ToCode() && DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitPlacementInternalAsync(placementId, userId, isAutoSubmit: true, cancellationToken);
            placement = await db.PlacementTests
                .Include(p => p.ExamAttempt).ThenInclude(a => a!.Exam)
                .Include(p => p.Cert)
                .SingleAsync(p => p.PlacementId == placementId && p.UserId == userId, cancellationToken);
            attempt = placement.ExamAttempt!;
        }

        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.AttemptId)
            .Include(a => a.Question).ThenInclude(q => q.Group)
            .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
            .Include(a => a.Question).ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .OrderBy(a => a.Question.Section.Skill.DisplayOrder)
            .ThenBy(a => a.Question.Section.DisplayOrder)
            .ThenBy(a => a.Question.Group.DisplayOrder)
            .ThenBy(a => a.Question.DisplayOrder)
            .ToListAsync(cancellationToken);

        int totalQuestions = answers.Count;
        int answeredCount = answers.Count(a => a.SelectedOptionId != null);
        int flaggedCount = answers.Count(a => a.IsFlagged);

        int targetIndex = 0;
        if (questionDisplayOrder.HasValue && questionDisplayOrder.Value >= 1 && questionDisplayOrder.Value <= totalQuestions)
        {
            targetIndex = questionDisplayOrder.Value - 1;
        }
        else if (attempt.LastQuestionId.HasValue)
        {
            var idx = answers.FindIndex(a => a.QuestionId == attempt.LastQuestionId.Value);
            if (idx >= 0) targetIndex = idx;
        }

        var currentAnswer = totalQuestions > 0 ? answers[targetIndex] : null;
        AttemptQuestionInfo currentQuestionInfo;

        if (currentAnswer is not null)
        {
            var q = currentAnswer.Question;
            var opts = q.QuestionOptionQuestionNavigations
                .OrderBy(o => o.Label)
                .Select(o => new AttemptOption(o.OptionId, o.Label, o.Text))
                .ToList();

            currentQuestionInfo = new AttemptQuestionInfo(
                q.QuestionId,
                targetIndex + 1,
                totalQuestions,
                q.SectionId,
                q.Section.Code,
                q.Section.Name,
                q.Section.Skill.Code,
                q.Section.Skill.Name,
                EnumCodes.Parse<Modality>(q.Section.Modality),
                q.Stem,
                q.Group.ImagePath,
                q.Group.ImageAlt,
                q.Group.AudioPath,
                q.Group.Transcript,
                q.Group.Passage,
                opts,
                currentAnswer.SelectedOptionId,
                currentAnswer.IsFlagged);
        }
        else
        {
            currentQuestionInfo = new AttemptQuestionInfo(
                0, 0, 0, 0, "", "", "", "", Modality.Reading, "", null, null, null, null, null, [], null, false);
        }

        var navigator = answers.Select((a, idx) => new NavigatorItem(
            a.QuestionId,
            idx + 1,
            a.Question.SectionId,
            a.Question.Section.Code,
            EnumCodes.Parse<Modality>(a.Question.Section.Modality),
            a.SelectedOptionId != null,
            a.IsFlagged,
            idx == targetIndex)).ToList();

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);

        return new PlacementTakeView(
            placement.PlacementId,
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam.Name,
            placement.Cert.Code,
            EnumCodes.Parse<PlacementStatus>(placement.Status),
            attempt.StartedAt,
            attempt.ExpiresAt,
            remainingSeconds,
            totalQuestions,
            answeredCount,
            flaggedCount,
            currentQuestionInfo,
            navigator,
            attempt.IsAutoSubmitted);
    }

    public async Task<SaveAttemptAnswerResult> SaveAnswerAsync(
        int userId,
        int placementId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
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

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitPlacementInternalAsync(placementId, userId, isAutoSubmit: true, cancellationToken);
            throw new PlacementException(PlacementException.PlacementExpired, "Placement test time has expired.");
        }

        var answer = await db.AttemptAnswers
            .SingleOrDefaultAsync(a => a.AttemptId == attempt.AttemptId && a.QuestionId == questionId, cancellationToken)
            ?? throw new PlacementException(PlacementException.QuestionNotFound, $"Question {questionId} not found in placement test.");

        if (selectedOptionId.HasValue)
        {
            var valid = await db.QuestionOptions
                .AnyAsync(o => o.OptionId == selectedOptionId.Value && o.QuestionId == questionId, cancellationToken);
            if (!valid)
            {
                throw new PlacementException(PlacementException.InvalidOption, "Invalid option for this question.");
            }
        }

        answer.SelectedOptionId = selectedOptionId;
        answer.IsFlagged = isFlagged;
        attempt.LastQuestionId = questionId;

        await db.SaveChangesAsync(cancellationToken);

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return new SaveAttemptAnswerResult(questionId, selectedOptionId, isFlagged, remainingSeconds, attempt.IsAutoSubmitted);
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

        if (placement.Status != PlacementStatus.Done.ToCode())
        {
            throw new PlacementException(PlacementException.PlacementNotFinished, "Placement test is not finished.");
        }

        return await BuildPlacementResultViewAsync(placement, cancellationToken);
    }

    public async Task<TargetBandView> GetTargetBandViewAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new CertificateOption(c.Code, c.Name))
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
            targetOptions);
    }

    public async Task<SaveTargetBandResult> SetTargetBandAsync(int userId, string targetBandCode, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var validBand = await db.ScoreBands
            .AnyAsync(b => b.CertId == certId && b.Code == targetBandCode, cancellationToken);

        if (!validBand)
        {
            throw new PlacementException(PlacementException.TargetBandInvalid, $"Band code '{targetBandCode}' is not valid for this certificate.");
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await db.TargetBands
            .FirstOrDefaultAsync(t => t.UserId == userId && t.CertId == certId, cancellationToken);

        if (existing is not null)
        {
            existing.TargetBandCode = targetBandCode;
            existing.SetAt = now;
        }
        else
        {
            db.TargetBands.Add(new TargetBand
            {
                UserId = userId,
                CertId = certId,
                TargetBandCode = targetBandCode,
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
            if (concurrent is not null)
            {
                concurrent.TargetBandCode = targetBandCode;
                concurrent.SetAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw;
            }
        }

        return new SaveTargetBandResult(userId, certId, targetBandCode, now);
    }

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

            var now = DateTimeOffset.UtcNow;
            attempt.Status = AttemptStatus.Graded.ToCode();
            attempt.SubmittedAt = now;
            if (isAutoSubmit || now >= attempt.ExpiresAt)
            {
                attempt.IsAutoSubmitted = true;
            }

            var answers = await db.AttemptAnswers
                .Where(a => a.AttemptId == attempt.AttemptId)
                .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
                .Include(a => a.Question).ThenInclude(q => q.QuestionOptionQuestionNavigations)
                .ToListAsync(cancellationToken);

            foreach (var ans in answers)
            {
                var correctOpt = ans.Question.QuestionOptionQuestionNavigations.FirstOrDefault(o => o.IsCorrect);
                ans.IsCorrect = ans.SelectedOptionId.HasValue && ans.SelectedOptionId.Value == correctOpt?.OptionId;
            }

            var existingSkillScores = await db.AttemptSkillScores
                .Where(s => s.AttemptId == attempt.AttemptId)
                .ToListAsync(cancellationToken);
            if (existingSkillScores.Count > 0)
            {
                db.AttemptSkillScores.RemoveRange(existingSkillScores);
            }

            var skillGroups = answers.GroupBy(a => a.Question.Section.Skill).ToList();
            var newSkillScores = new List<AttemptSkillScore>();

            foreach (var group in skillGroups)
            {
                var skill = group.Key;
                int rawScore = group.Count(a => a.IsCorrect == true);
                int scaledScore = await ComputeScaledScoreAsync(
                    attempt.CertId, skill.SkillId, attempt.ExamId, rawScore, group.Count(), cancellationToken);

                newSkillScores.Add(new AttemptSkillScore
                {
                    AttemptId = attempt.AttemptId,
                    SkillId = skill.SkillId,
                    CertId = attempt.CertId,
                    RawScore = rawScore,
                    ScaledScore = scaledScore,
                });
            }

            db.AttemptSkillScores.AddRange(newSkillScores);

            int totalScore = newSkillScores.Sum(s => s.ScaledScore);
            attempt.TotalScore = totalScore;

            var bands = await db.ScoreBands
                .Where(b => b.CertId == attempt.CertId)
                .OrderBy(b => b.MinTotal)
                .ToListAsync(cancellationToken);

            var matchingBand = bands.FirstOrDefault(b => totalScore >= b.MinTotal && totalScore <= b.MaxTotal);
            if (matchingBand is null && bands.Count > 0)
            {
                matchingBand = totalScore < bands[0].MinTotal ? bands[0] : bands[^1];
            }
            attempt.BandCode = matchingBand?.Code;

            // Snapshot matching invariant: PlacementTests.TotalScore must match ExamAttempts.TotalScore
            placement.Status = PlacementStatus.Done.ToCode();
            placement.TotalScore = totalScore;
            placement.ResultBandCode = matchingBand?.Code;
            placement.TakenAt = now;

            await db.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);

            return await BuildPlacementResultViewAsync(placement, cancellationToken);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    private async Task<int> ComputeScaledScoreAsync(
        int certId,
        int skillId,
        int examId,
        int rawScore,
        int totalQuestionsInSkill,
        CancellationToken cancellationToken)
    {
        // 1. Try exam-specific exact match
        var scale = await db.ScoreScales
            .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == examId && s.RawScore == rawScore)
            .Select(s => (int?)s.ScaledScore)
            .FirstOrDefaultAsync(cancellationToken);

        if (scale.HasValue) return scale.Value;

        // 2. Try default scale (ExamId == null) exact match
        scale = await db.ScoreScales
            .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == null && s.RawScore == rawScore)
            .Select(s => (int?)s.ScaledScore)
            .FirstOrDefaultAsync(cancellationToken);

        if (scale.HasValue) return scale.Value;

        // 3. Fallback to closest available score scale row for this skill
        var allRows = await db.ScoreScales
            .Where(s => s.CertId == certId && s.SkillId == skillId && (s.ExamId == examId || s.ExamId == null))
            .OrderByDescending(s => s.ExamId)
            .ThenBy(s => s.RawScore)
            .ToListAsync(cancellationToken);

        if (allRows.Count > 0)
        {
            var closest = allRows.OrderBy(r => Math.Abs(r.RawScore - rawScore)).First();
            return closest.ScaledScore;
        }

        // 4. Default proportional fallback if no score scales seeded
        if (totalQuestionsInSkill <= 0) return 5;
        double fraction = Math.Clamp((double)rawScore / totalQuestionsInSkill, 0.0, 1.0);
        int scaled = 5 + (int)Math.Round((fraction * 490) / 5.0) * 5;
        return scaled;
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

        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.AttemptId)
            .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
            .ToListAsync(cancellationToken);

        var bands = await db.ScoreBands
            .Where(b => b.CertId == attempt.CertId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        int totalScore = placement.TotalScore ?? attempt.TotalScore ?? 0;
        string resultBandCode = placement.ResultBandCode ?? attempt.BandCode ?? "";
        string resultBandName = bands.FirstOrDefault(b => b.Code == resultBandCode)?.Name ?? resultBandCode;

        var skillInfos = new List<PlacementSkillScoreInfo>();
        foreach (var sc in skillScores)
        {
            int totalInSkill = answers.Count(a => a.Question.Section.SkillId == sc.SkillId);
            skillInfos.Add(new PlacementSkillScoreInfo(
                sc.SkillId,
                sc.Skill.Code,
                sc.Skill.Name,
                sc.RawScore,
                totalInSkill,
                sc.ScaledScore,
                resultBandCode));
        }

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

        return new PlacementResultView(
            placement.PlacementId,
            attempt.AttemptId,
            attempt.CertId,
            placement.Cert?.Code ?? "",
            attempt.Exam?.Name ?? "Placement Test",
            placement.TakenAt,
            duration,
            answers.Count,
            totalScore,
            resultBandCode,
            resultBandName,
            cefrBands,
            skillInfos,
            weakerSkillName,
            bandNote,
            attempt.IsAutoSubmitted);
    }
}
