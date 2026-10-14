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

public class MockExamException(string code, string message) : Exception(message)
{
    public const string InsufficientCredits = "insufficient_credits";
    public const string AttemptExpired = "attempt_expired";
    public const string ExamNotFound = "exam_not_found";
    public const string AttemptNotFound = "attempt_not_found";
    public const string AttemptNotActive = "attempt_not_active";
    public const string InvalidOption = "invalid_option";
    public const string QuestionNotFound = "question_not_found";

    public string Code { get; } = code;
}

public interface IMockExamService
{
    Task<MockExamListView> GetMockExamsAsync(int userId, CancellationToken cancellationToken = default);

    Task<MockExamInstructionsView> GetExamInstructionsAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<ExamAttemptInfo> StartAttemptAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<AttemptTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionDisplayOrder, CancellationToken cancellationToken = default);

    Task<SaveAttemptAnswerResult> SaveAnswerAsync(int userId, int attemptId, int questionId, int? selectedOptionId, bool isFlagged, CancellationToken cancellationToken = default);

    Task<AttemptResultView> SubmitAttemptAsync(int userId, int attemptId, bool isAutoSubmit, CancellationToken cancellationToken = default);

    Task CancelAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<AttemptResultView> GetAttemptResultAsync(int userId, int attemptId, CancellationToken cancellationToken = default);
}

public sealed class MockExamService(CompanionDbContext db, ILearnerService learners) : IMockExamService
{
    public const int ExamCreditCost = 1;

    public async Task<MockExamListView> GetMockExamsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new CertificateOption(c.Code, c.Name))
            .SingleAsync(cancellationToken);

        var balance = await db.vw_UserCreditBalances
            .Where(b => b.UserId == userId)
            .Select(b => b.Balance)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        // Auto-submit any expired in-progress attempts first
        var expiredAttempts = await db.ExamAttempts
            .Where(a => a.UserId == userId && a.CertId == certId && a.Status == "in_progress" && DateTimeOffset.UtcNow >= a.ExpiresAt)
            .Select(a => a.AttemptId)
            .ToListAsync(cancellationToken);

        foreach (var expiredId in expiredAttempts)
        {
            await SubmitAttemptInternalAsync(expiredId, userId, isAutoSubmit: true, cancellationToken);
        }

        var exams = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId && e.Purpose == "mock" && e.Status == "published" && !e.IsDeleted)
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
            var activeAttempt = attemptsForExam.FirstOrDefault(a => a.Status == "in_progress");
            var latestGraded = attemptsForExam
                .Where(a => a.Status == "graded")
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
            .Where(e => e.ExamId == examId && e.CertId == certId && e.Purpose == "mock" && e.Status == "published" && !e.IsDeleted)
            .Include(e => e.Cert)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new MockExamException(MockExamException.ExamNotFound, $"Mock exam {examId} not found.");

        var balance = await db.vw_UserCreditBalances
            .Where(b => b.UserId == userId)
            .Select(b => b.Balance)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        // Auto-submit expired attempt if any
        var activeAttempt = await db.ExamAttempts
            .Where(a => a.UserId == userId && a.ExamId == examId && a.Status == "in_progress")
            .FirstOrDefaultAsync(cancellationToken);

        if (activeAttempt is not null && DateTimeOffset.UtcNow >= activeAttempt.ExpiresAt)
        {
            await SubmitAttemptInternalAsync(activeAttempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            activeAttempt = null;
        }

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .Include(s => s.Skill)
            .OrderBy(s => s.Skill.DisplayOrder)
            .ThenBy(s => s.DisplayOrder)
            .Select(s => new MockExamSectionInfo(
                s.SectionId,
                s.Skill.Code,
                s.Skill.Name,
                s.Modality,
                s.Code,
                s.Name,
                s.ExpectedQuestionCount,
                s.Skill.DurationMinutes,
                s.DisplayOrder))
            .ToListAsync(cancellationToken);

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
            .Where(e => e.ExamId == examId && e.CertId == certId && e.Purpose == "mock" && e.Status == "published" && !e.IsDeleted)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new MockExamException(MockExamException.ExamNotFound, $"Mock exam {examId} not found.");

        // Check if there is already an in-progress attempt for this exam
        var existingAttempt = await db.ExamAttempts
            .Where(a => a.UserId == userId && a.ExamId == examId && a.Status == "in_progress")
            .FirstOrDefaultAsync(cancellationToken);

        if (existingAttempt is not null)
        {
            if (DateTimeOffset.UtcNow >= existingAttempt.ExpiresAt)
            {
                await SubmitAttemptInternalAsync(existingAttempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            }
            else
            {
                // Resume active attempt without charging again
                var remaining = Math.Max(0, (int)(existingAttempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
                return new ExamAttemptInfo(
                    existingAttempt.AttemptId,
                    existingAttempt.ExamId,
                    existingAttempt.Status,
                    existingAttempt.StartedAt,
                    existingAttempt.ExpiresAt,
                    remaining,
                    existingAttempt.IsAutoSubmitted);
            }
        }

        // Check credits before charging: block before charge invariant
        var balance = await db.vw_UserCreditBalances
            .Where(b => b.UserId == userId)
            .Select(b => b.Balance)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        if (balance < ExamCreditCost)
        {
            throw new MockExamException(MockExamException.InsufficientCredits, "You do not have enough credits to start this mock exam.");
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(exam.DurationMinutes);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var attempt = new ExamAttempt
        {
            UserId = userId,
            ExamId = examId,
            CertId = certId,
            Status = "in_progress",
            StartedAt = now,
            ExpiresAt = expiresAt,
            IsAutoSubmitted = false
        };

        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);

        // Charge 1 credit
        var creditTxn = new CreditTransaction
        {
            UserId = userId,
            Reason = "mock_exam_start",
            Delta = -ExamCreditCost,
            AttemptId = attempt.AttemptId,
            CreatedAt = now
        };
        db.CreditTransactions.Add(creditTxn);

        // Populate questions for the attempt
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
                    IsFlagged = false
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var remainingSeconds = Math.Max(0, (int)(expiresAt - now).TotalSeconds);
        return new ExamAttemptInfo(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Status,
            attempt.StartedAt,
            attempt.ExpiresAt,
            remainingSeconds,
            attempt.IsAutoSubmitted);
    }

    public async Task<AttemptTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionDisplayOrder, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new MockExamException(MockExamException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status == "abandoned")
        {
            throw new MockExamException(MockExamException.AttemptNotActive, "This exam attempt has been cancelled.");
        }

        // Auto-submit if expired
        if (attempt.Status == "in_progress" && DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptInternalAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            // Reload after auto-submit
            attempt = await db.ExamAttempts
                .Include(a => a.Exam)
                .Include(a => a.Cert)
                .SingleAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken);
        }

        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attemptId)
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

        // Determine current question
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
                q.Section.Modality,
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
                0, 0, 0, 0, "", "", "", "", "reading", "", null, null, null, null, null, [], null, false);
        }

        var navigator = answers.Select((a, idx) => new NavigatorItem(
            a.QuestionId,
            idx + 1,
            a.Question.SectionId,
            a.Question.Section.Code,
            a.Question.Section.Modality,
            a.SelectedOptionId != null,
            a.IsFlagged,
            idx == targetIndex)).ToList();

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);

        return new AttemptTakeView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam.Name,
            attempt.Cert.Code,
            attempt.Status,
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
        int attemptId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new MockExamException(MockExamException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != "in_progress")
        {
            throw new MockExamException(MockExamException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        // Check if expired on touch -> auto-submit and refuse answer
        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptInternalAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            throw new MockExamException(MockExamException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        var answer = await db.AttemptAnswers
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.QuestionId == questionId, cancellationToken)
            ?? throw new MockExamException(MockExamException.QuestionNotFound, $"Question {questionId} not found in attempt.");

        if (selectedOptionId.HasValue)
        {
            var valid = await db.QuestionOptions
                .AnyAsync(o => o.OptionId == selectedOptionId.Value && o.QuestionId == questionId, cancellationToken);
            if (!valid)
            {
                throw new MockExamException(MockExamException.InvalidOption, "Invalid option for this question.");
            }
        }

        answer.SelectedOptionId = selectedOptionId;
        answer.IsFlagged = isFlagged;
        attempt.LastQuestionId = questionId;

        await db.SaveChangesAsync(cancellationToken);

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return new SaveAttemptAnswerResult(questionId, selectedOptionId, isFlagged, remainingSeconds, attempt.IsAutoSubmitted);
    }

    public async Task<AttemptResultView> SubmitAttemptAsync(
        int userId,
        int attemptId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default)
    {
        return await SubmitAttemptInternalAsync(attemptId, userId, isAutoSubmit, cancellationToken);
    }

    public async Task CancelAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new MockExamException(MockExamException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status == "abandoned")
        {
            return;
        }

        if (attempt.Status == "graded")
        {
            throw new MockExamException(MockExamException.AttemptNotActive, "Graded attempt cannot be cancelled.");
        }

        attempt.Status = "abandoned";
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AttemptResultView> GetAttemptResultAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new MockExamException(MockExamException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status == "in_progress" && DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            return await SubmitAttemptInternalAsync(attemptId, userId, isAutoSubmit: true, cancellationToken);
        }

        if (attempt.Status != "graded")
        {
            throw new MockExamException(MockExamException.AttemptNotActive, "Attempt is not graded.");
        }

        return await BuildAttemptResultViewAsync(attempt, cancellationToken);
    }

    private async Task<AttemptResultView> SubmitAttemptInternalAsync(
        int attemptId,
        int userId,
        bool isAutoSubmit,
        CancellationToken cancellationToken)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new MockExamException(MockExamException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status == "graded")
        {
            return await BuildAttemptResultViewAsync(attempt, cancellationToken);
        }

        if (attempt.Status == "abandoned")
        {
            throw new MockExamException(MockExamException.AttemptNotActive, "This attempt was abandoned.");
        }

        var now = DateTimeOffset.UtcNow;
        attempt.Status = "graded";
        attempt.SubmittedAt = now;
        if (isAutoSubmit || now >= attempt.ExpiresAt)
        {
            attempt.IsAutoSubmitted = true;
        }

        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attemptId)
            .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
            .Include(a => a.Question).ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .ToListAsync(cancellationToken);

        // Score each answer
        foreach (var ans in answers)
        {
            var correctOpt = ans.Question.QuestionOptionQuestionNavigations.FirstOrDefault(o => o.IsCorrect);
            ans.IsCorrect = ans.SelectedOptionId.HasValue && ans.SelectedOptionId.Value == correctOpt?.OptionId;
        }

        // Clean any existing skill scores for idempotent resubmits
        var existingSkillScores = await db.AttemptSkillScores
            .Where(s => s.AttemptId == attemptId)
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
                ScaledScore = scaledScore
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

        await db.SaveChangesAsync(cancellationToken);

        return await BuildAttemptResultViewAsync(attempt, cancellationToken);
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

    private async Task<AttemptResultView> BuildAttemptResultViewAsync(
        ExamAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.Exam == null)
        {
            await db.Entry(attempt).Reference(a => a.Exam).LoadAsync(cancellationToken);
        }
        if (attempt.Cert == null)
        {
            await db.Entry(attempt).Reference(a => a.Cert).LoadAsync(cancellationToken);
        }

        var skillScores = await db.AttemptSkillScores
            .Where(s => s.AttemptId == attempt.AttemptId)
            .Include(s => s.Skill)
            .ToListAsync(cancellationToken);

        var questionCounts = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.AttemptId)
            .GroupBy(a => a.Question.Section.SkillId)
            .Select(g => new { SkillId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SkillId, g => g.Count, cancellationToken);

        var scoreItems = skillScores
            .OrderBy(s => s.Skill.DisplayOrder)
            .Select(s => new AttemptSkillScoreItem(
                s.SkillId,
                s.Skill.Code,
                s.Skill.Name,
                s.Skill.Modality,
                s.RawScore,
                s.ScaledScore,
                questionCounts.TryGetValue(s.SkillId, out var count) ? count : 0))
            .ToList();

        var band = await db.ScoreBands
            .Where(b => b.CertId == attempt.CertId && b.Code == attempt.BandCode)
            .FirstOrDefaultAsync(cancellationToken);

        var prevAttempt = await db.ExamAttempts
            .Where(a => a.UserId == attempt.UserId && a.CertId == attempt.CertId && a.AttemptId != attempt.AttemptId && a.Status == "graded" && a.TotalScore.HasValue)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        int? prevScore = prevAttempt?.TotalScore;
        int? scoreDelta = prevScore.HasValue && attempt.TotalScore.HasValue
            ? attempt.TotalScore.Value - prevScore.Value
            : null;

        int durationSeconds = (int)Math.Max(0, ((attempt.SubmittedAt ?? DateTimeOffset.UtcNow) - attempt.StartedAt).TotalSeconds);

        return new AttemptResultView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam!.Name,
            attempt.CertId,
            attempt.Cert!.Code,
            attempt.Cert!.Name,
            attempt.Status,
            attempt.StartedAt,
            attempt.SubmittedAt ?? DateTimeOffset.UtcNow,
            durationSeconds,
            attempt.IsAutoSubmitted,
            attempt.TotalScore ?? 0,
            attempt.BandCode,
            band?.Name,
            scoreItems,
            prevScore,
            scoreDelta);
    }
}
