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

public class MockExamException(string code, string message) : Exception(message)
{
    public const string InsufficientCredits = "insufficient_credits";
    public const string AttemptExpired = "attempt_expired";
    public const string ExamNotFound = "exam_not_found";
    public const string AttemptNotFound = "attempt_not_found";
    public const string AttemptNotActive = "attempt_not_active";
    public const string InvalidOption = "invalid_option";
    public const string QuestionNotFound = "question_not_found";
    public const string AttemptNotFinished = "attempt_not_finished";

    public string Code { get; } = code;
}

public interface IMockExamService
{
    Task<MockExamListView> GetMockExamsAsync(int userId, CancellationToken cancellationToken = default);

    Task<MockExamInstructionsView> GetExamInstructionsAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<ExamAttemptInfo> StartAttemptAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<AttemptTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionDisplayOrder, CancellationToken cancellationToken = default);

    Task<SaveAttemptAnswerResult> SaveAnswerAsync(
        int userId,
        int attemptId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
        CancellationToken cancellationToken = default,
        string? responseText = null);

    Task<SaveAttemptDraftResult> SaveDraftAsync(int userId, int attemptId, int questionId, string? responseText, CancellationToken cancellationToken = default);

    Task<AttemptResultView> SubmitAttemptAsync(
        int userId,
        int attemptId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default,
        IReadOnlyList<AttemptFinalDraft>? finalDrafts = null);

    Task CancelAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<AttemptResultView> GetAttemptResultAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<ExamHistoryView> GetExamHistoryAsync(int userId, CancellationToken cancellationToken = default);

    Task<AttemptReviewView> GetAttemptReviewAsync(int userId, int attemptId, CancellationToken cancellationToken = default);
}

public sealed class MockExamService(
    CompanionDbContext db,
    ILearnerService learners,
    ICreditLedger? creditLedger = null,
    IAttemptEngine? attemptEngine = null) : IMockExamService
{
    public const int ExamCreditCost = 1;
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db);
    private readonly IAttemptEngine _attemptEngine = attemptEngine ?? new AttemptEngine(db);

    private static MockExamException MapException(AttemptEngineException ex) =>
        new(ex.Code, ex.Message);

    public async Task<MockExamListView> GetMockExamsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new CertificateOption(c.Code, c.Name))
            .SingleAsync(cancellationToken);

        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);

        // Auto-submit any expired in-progress attempts first
        await _attemptEngine.AutoSubmitExpiredAttemptsAsync(userId, certId, cancellationToken);

        var exams = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId && e.Purpose == ExamPurpose.Mock.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .OrderBy(e => e.Name)
            .ToListAsync(cancellationToken);

        var userAttempts = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.CertId == certId)
            .ToListAsync(cancellationToken);

        var items = new List<MockExamItem>();
        foreach (var exam in exams)
        {
            var attemptsForExam = userAttempts.Where(a => a.ExamId == exam.ExamId).ToList();
            var activeAttempt = attemptsForExam.FirstOrDefault(a => a.Status == AttemptStatus.InProgress.ToCode());
            var latestGraded = attemptsForExam
                .Where(a => a.Status == AttemptStatus.Graded.ToCode())
                .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
                .FirstOrDefault();

            items.Add(new MockExamItem(
                exam.ExamId,
                exam.CertId,
                cert.Code,
                exam.Name,
                exam.Description,
                exam.DurationMinutes,
                exam.QuestionCount,
                exam.SourceName,
                ExamCreditCost,
                activeAttempt?.AttemptId,
                activeAttempt?.ExpiresAt,
                latestGraded?.TotalScore,
                latestGraded?.BandCode));
        }

        return new MockExamListView(cert, balance, items);
    }

    public async Task<MockExamInstructionsView> GetExamInstructionsAsync(int userId, int examId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var exam = await db.Exams
            .AsNoTracking()
            .Where(e => e.ExamId == examId && e.CertId == certId && e.Purpose == ExamPurpose.Mock.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .Include(e => e.Cert)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new MockExamException(MockExamException.ExamNotFound, $"Mock exam {examId} not found.");

        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);

        // Auto-submit expired attempt if any
        var activeAttempt = await db.ExamAttempts
            .Where(a => a.UserId == userId && a.ExamId == examId && a.Status == AttemptStatus.InProgress.ToCode())
            .FirstOrDefaultAsync(cancellationToken);

        if (activeAttempt is not null && DateTimeOffset.UtcNow >= activeAttempt.ExpiresAt)
        {
            await _attemptEngine.SubmitAttemptAsync(activeAttempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            activeAttempt = null;
        }

        // Only the sections this exam has questions in, counted from the exam itself — the
        // certificate may define sections (e.g. Speaking on TOEIC L&R) this exam never uses.
        var questionCounts = await db.Questions
            .AsNoTracking()
            .Where(q => q.ExamId == examId)
            .GroupBy(q => q.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SectionId, x => x.Count, cancellationToken);
        var examSectionIds = questionCounts.Keys.ToList();

        var sections = (await db.Sections
                .AsNoTracking()
                .Where(s => s.CertId == certId && examSectionIds.Contains(s.SectionId))
                .Include(s => s.Skill)
                .OrderBy(s => s.Skill.DisplayOrder)
                .ThenBy(s => s.DisplayOrder)
                .ToListAsync(cancellationToken))
            .Select(s => new MockExamSectionInfo(
                s.SectionId,
                s.Skill.Code,
                s.Skill.Name,
                EnumCodes.Parse<Modality>(s.Modality),
                s.Code,
                s.Name,
                s.ExpectedQuestionCount,
                questionCounts[s.SectionId],
                s.Skill.DurationMinutes,
                s.DisplayOrder))
            .ToList();

        return new MockExamInstructionsView(
            exam.ExamId,
            exam.CertId,
            exam.Cert.Code,
            exam.Name,
            exam.Description,
            exam.DurationMinutes,
            exam.QuestionCount,
            ExamCreditCost,
            balance,
            activeAttempt?.AttemptId,
            sections);
    }

    public async Task<ExamAttemptInfo> StartAttemptAsync(int userId, int examId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var exam = await db.Exams
            .Where(e => e.ExamId == examId && e.CertId == certId && e.Purpose == ExamPurpose.Mock.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new MockExamException(MockExamException.ExamNotFound, $"Mock exam {examId} not found.");

        // Check if there is already an in-progress attempt for this exam
        var existingAttempt = await db.ExamAttempts
            .Where(a => a.UserId == userId && a.ExamId == examId && a.Status == AttemptStatus.InProgress.ToCode())
            .FirstOrDefaultAsync(cancellationToken);

        if (existingAttempt is not null)
        {
            if (DateTimeOffset.UtcNow >= existingAttempt.ExpiresAt)
            {
                await _attemptEngine.SubmitAttemptAsync(existingAttempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            }
            else
            {
                // Resume active attempt without charging again
                var remaining = Math.Max(0, (int)(existingAttempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
                return new ExamAttemptInfo(
                    existingAttempt.AttemptId,
                    existingAttempt.ExamId,
                    EnumCodes.Parse<AttemptStatus>(existingAttempt.Status),
                    existingAttempt.StartedAt,
                    existingAttempt.ExpiresAt,
                    remaining,
                    existingAttempt.IsAutoSubmitted);
            }
        }

        // Check credits before charging: block before charge invariant
        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);
        if (balance < ExamCreditCost)
        {
            throw new MockExamException(MockExamException.InsufficientCredits, "You do not have enough credits to start this mock exam.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var attempt = await _attemptEngine.CreateAttemptAsync(userId, certId, examId, exam.DurationMinutes, cancellationToken);

        // Charge 1 credit via CreditLedger
        try
        {
            await _creditLedger.ChargeMockExamAsync(userId, attempt.AttemptId, ExamCreditCost, cancellationToken);
        }
        catch (CreditLedgerException ex) when (ex.Code == CreditLedgerException.InsufficientCredits)
        {
            throw new MockExamException(MockExamException.InsufficientCredits, "You do not have enough credits to start this mock exam.");
        }

        await tx.CommitAsync(cancellationToken);

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return new ExamAttemptInfo(
            attempt.AttemptId,
            attempt.ExamId,
            EnumCodes.Parse<AttemptStatus>(attempt.Status),
            attempt.StartedAt,
            attempt.ExpiresAt,
            remainingSeconds,
            attempt.IsAutoSubmitted);
    }

    public async Task<AttemptTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionDisplayOrder, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _attemptEngine.GetAttemptTakeAsync(attemptId, userId, questionDisplayOrder, cancellationToken);
        }
        catch (AttemptEngineException ex)
        {
            throw MapException(ex);
        }
    }

    public async Task<SaveAttemptAnswerResult> SaveAnswerAsync(
        int userId,
        int attemptId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
        CancellationToken cancellationToken = default,
        string? responseText = null)
    {
        try
        {
            return await _attemptEngine.SaveAnswerAsync(
                attemptId,
                userId,
                questionId,
                selectedOptionId,
                isFlagged,
                null,
                cancellationToken,
                responseText);
        }
        catch (AttemptEngineException ex)
        {
            throw MapException(ex);
        }
    }

    public async Task<SaveAttemptDraftResult> SaveDraftAsync(
        int userId,
        int attemptId,
        int questionId,
        string? responseText,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _attemptEngine.SaveDraftAsync(attemptId, userId, questionId, responseText, cancellationToken);
        }
        catch (AttemptEngineException ex)
        {
            throw MapException(ex);
        }
    }

    public async Task<AttemptResultView> SubmitAttemptAsync(
        int userId,
        int attemptId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default,
        IReadOnlyList<AttemptFinalDraft>? finalDrafts = null)
    {
        try
        {
            await _attemptEngine.SubmitAttemptAsync(attemptId, userId, isAutoSubmit, cancellationToken, finalDrafts);
            return await _attemptEngine.GetAttemptResultAsync(attemptId, userId, cancellationToken);
        }
        catch (AttemptEngineException ex)
        {
            throw MapException(ex);
        }
    }

    public async Task CancelAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _attemptEngine.CancelAttemptAsync(attemptId, userId, cancellationToken);
        }
        catch (AttemptEngineException ex)
        {
            throw MapException(ex);
        }
    }

    public async Task<AttemptResultView> GetAttemptResultAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _attemptEngine.GetAttemptResultAsync(attemptId, userId, cancellationToken);
        }
        catch (AttemptEngineException ex)
        {
            throw MapException(ex);
        }
    }

    public async Task<ExamHistoryView> GetExamHistoryAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new CertificateOption(c.Code, c.Name))
            .SingleAsync(cancellationToken);

        // Auto-submit any expired in-progress attempts first
        await _attemptEngine.AutoSubmitExpiredAttemptsAsync(userId, certId, cancellationToken);

        var attempts = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.CertId == certId && a.Status == AttemptStatus.Graded.ToCode() && a.TotalScore.HasValue)
            .Include(a => a.Exam)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .ToListAsync(cancellationToken);

        var bands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == certId)
            .OrderBy(b => b.MinTotal)
            .Select(b => new ScoreBandItem(b.Code, b.Name, b.MinTotal, b.MaxTotal, b.DisplayOrder))
            .ToListAsync(cancellationToken);

        var attemptIds = attempts.Select(a => a.AttemptId).ToList();
        var skillScores = await db.AttemptSkillScores
            .AsNoTracking()
            .Where(s => attemptIds.Contains(s.AttemptId))
            .Include(s => s.Skill)
            .ToListAsync(cancellationToken);

        var questionCounts = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => attemptIds.Contains(a.AttemptId))
            .GroupBy(a => new { a.AttemptId, a.Question.Section.SkillId })
            .Select(g => new { g.Key.AttemptId, g.Key.SkillId, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var bandDict = bands.ToDictionary(b => b.Code, b => b.Name);

        var attemptItems = new List<ExamHistoryItem>();
        foreach (var att in attempts)
        {
            var attScores = skillScores
                .Where(s => s.AttemptId == att.AttemptId)
                .OrderBy(s => s.Skill.DisplayOrder)
                .Select(s => new AttemptSkillScoreItem(
                    s.SkillId,
                    s.Skill.Code,
                    s.Skill.Name,
                    EnumCodes.Parse<Modality>(s.Skill.Modality),
                    s.RawScore,
                    s.ScaledScore,
                    questionCounts.FirstOrDefault(q => q.AttemptId == att.AttemptId && q.SkillId == s.SkillId)?.Count ?? 0))
                .ToList();

            string? bandName = att.BandCode != null && bandDict.TryGetValue(att.BandCode, out var bName) ? bName : null;

            attemptItems.Add(new ExamHistoryItem(
                att.AttemptId,
                att.ExamId,
                att.Exam?.Name ?? "Mock Exam",
                att.CertId,
                cert.Code,
                att.StartedAt,
                att.SubmittedAt ?? att.StartedAt,
                att.TotalScore ?? 0,
                att.BandCode,
                bandName,
                attScores,
                EnumCodes.Parse<AttemptStatus>(att.Status)));
        }

        int totalAttempts = attempts.Count;
        int? bestScore = attempts.Count > 0 ? attempts.Max(a => a.TotalScore) : null;
        int? averageScore = attempts.Count > 0 ? (int)Math.Round(attempts.Average(a => a.TotalScore!.Value)) : null;

        var bestAttempt = attempts
            .Where(a => a.TotalScore.HasValue)
            .OrderByDescending(a => a.TotalScore!.Value)
            .ThenByDescending(a => a.SubmittedAt)
            .FirstOrDefault();

        string? bestScoreBandCode = bestAttempt?.BandCode;
        string? bestScoreBandName = null;
        if (bestScoreBandCode != null)
        {
            bestScoreBandName = bandDict.TryGetValue(bestScoreBandCode, out var bName)
                ? bName
                : bestScoreBandCode;
        }

        return new ExamHistoryView(
            cert,
            totalAttempts,
            bestScore,
            averageScore,
            bestScoreBandCode,
            bestScoreBandName,
            bands,
            attemptItems);
    }

    public async Task<AttemptReviewView> GetAttemptReviewAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _attemptEngine.GetAttemptReviewAsync(attemptId, userId, cancellationToken);
        }
        catch (AttemptEngineException ex)
        {
            throw MapException(ex);
        }
    }
}
