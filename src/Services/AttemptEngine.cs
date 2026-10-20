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

public class AttemptEngineException(string code, string message) : Exception(message)
{
    public const string AttemptNotFound = "attempt_not_found";
    public const string AttemptNotActive = "attempt_not_active";
    public const string AttemptExpired = "attempt_expired";
    public const string QuestionNotFound = "question_not_found";
    public const string InvalidOption = "invalid_option";
    public const string AttemptNotFinished = "attempt_not_finished";
    public const string InvalidGradingRubric = "invalid_grading_rubric";

    public string Code { get; } = code;
}

public record AttemptGradingResult(
    ExamAttempt Attempt,
    int TotalScore,
    string? BandCode,
    IReadOnlyList<AttemptSkillScore> SkillScores,
    IReadOnlyList<AttemptAnswer> Answers,
    DateTimeOffset SubmittedAt,
    bool IsAutoSubmitted);

public interface IAttemptEngine
{
    Task<ExamAttempt> CreateAttemptAsync(
        int userId,
        int certId,
        int examId,
        int durationMinutes,
        CancellationToken cancellationToken = default);

    Task<AttemptTakeView> GetAttemptTakeAsync(
        int attemptId,
        int userId,
        int? questionDisplayOrder,
        CancellationToken cancellationToken = default);

    Task<SaveAttemptAnswerResult> SaveAnswerAsync(
        int attemptId,
        int userId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default,
        string? responseText = null);

    Task<AttemptGradingResult> SubmitAttemptAsync(
        int attemptId,
        int userId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default);

    Task<AttemptResultView> GetAttemptResultAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    Task FinalizeAiGradedAttemptAsync(int attemptId, CancellationToken cancellationToken = default);

    Task<AttemptReviewView> GetAttemptReviewAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    Task<int> AutoSubmitExpiredAttemptsAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken = default);

    Task<int> ComputeScaledScoreAsync(
        int certId,
        int skillId,
        int examId,
        int rawScore,
        int totalQuestionsInSkill,
        CancellationToken cancellationToken = default);

    Task CancelAttemptAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);
}

public sealed class AttemptEngine(CompanionDbContext db) : IAttemptEngine
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
            var expiresAt = now.AddMinutes(durationMinutes);

            var attempt = new ExamAttempt
            {
                UserId = userId,
                ExamId = examId,
                CertId = certId,
                Status = AttemptStatus.InProgress.ToCode(),
                StartedAt = now,
                ExpiresAt = expiresAt,
                IsAutoSubmitted = false
            };

            db.ExamAttempts.Add(attempt);
            await db.SaveChangesAsync(cancellationToken);

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

                    if (q.QuestionType == QuestionType.FreeResponse.ToCode())
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

    /// <summary>Studio-uploaded master track per section of the exam (ExamSectionAudio).</summary>
    private Task<Dictionary<int, string>> LoadSectionMasterAudioAsync(int examId, CancellationToken cancellationToken) =>
        db.ExamSectionAudios
            .AsNoTracking()
            .Where(a => a.ExamId == examId)
            .ToDictionaryAsync(a => a.SectionId, a => a.AudioPath, cancellationToken);

    private static ResolvedGroupAudio? ResolveGroupAudio(QuestionGroup group, Dictionary<int, string> sectionMasters) =>
        ExamAudio.Resolve(
            group.AudioPath,
            group.AudioStartMs,
            group.AudioEndMs,
            sectionMasters.GetValueOrDefault(group.SectionId));

    public async Task<AttemptTakeView> GetAttemptTakeAsync(
        int attemptId,
        int userId,
        int? questionDisplayOrder,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status == AttemptStatus.Abandoned.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "This exam attempt has been cancelled.");
        }

        // Auto-submit if expired
        if (attempt.Status == AttemptStatus.InProgress.ToCode() && DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
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

        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Where(fr => fr.AttemptId == attemptId)
            .ToDictionaryAsync(fr => fr.QuestionId, cancellationToken);

        int totalQuestions = answers.Count;
        int answeredCount = answers.Count(a =>
            a.Question.QuestionType == QuestionType.FreeResponse.ToCode()
                ? freeResponses.TryGetValue(a.QuestionId, out var response) && !string.IsNullOrWhiteSpace(response.ResponseText)
                : a.SelectedOptionId != null);
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

        var sectionMasterAudio = await LoadSectionMasterAudioAsync(attempt.ExamId, cancellationToken);
        var allQuestions = new List<AttemptQuestionInfo>(totalQuestions);
        for (int i = 0; i < totalQuestions; i++)
        {
            var ans = answers[i];
            var q = ans.Question;
            var opts = q.QuestionOptionQuestionNavigations
                .OrderBy(o => o.Label)
                .Select(o => new AttemptOption(o.OptionId, o.Label, o.Text))
                .ToList();
            var audio = ResolveGroupAudio(q.Group, sectionMasterAudio);

            allQuestions.Add(new AttemptQuestionInfo(
                q.QuestionId,
                i + 1,
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
                audio?.Path,
                audio?.StartMs,
                audio?.EndMs,
                q.Group.Transcript,
                q.Group.Passage,
                opts,
                ans.SelectedOptionId,
                ans.IsFlagged,
                q.QuestionType,
                q.MinWords,
                q.MaxWords,
                q.PrepSeconds,
                q.ResponseSeconds,
                q.Hint,
                q.Explanation,
                freeResponses.GetValueOrDefault(q.QuestionId)?.ResponseText));
        }

        var currentQuestionInfo = totalQuestions > 0 ? allQuestions[targetIndex] : new AttemptQuestionInfo(
            0, 0, 0, 0, "", "", "", "", Modality.Reading, "", null, null, null, null, null, null, null, [], null, false);

        var navigator = answers.Select((a, idx) => new NavigatorItem(
            a.QuestionId,
            idx + 1,
            a.Question.SectionId,
            a.Question.Section.Code,
            EnumCodes.Parse<Modality>(a.Question.Section.Modality),
            a.Question.QuestionType == QuestionType.FreeResponse.ToCode()
                ? freeResponses.TryGetValue(a.QuestionId, out var response) && !string.IsNullOrWhiteSpace(response.ResponseText)
                : a.SelectedOptionId != null,
            a.IsFlagged,
            idx == targetIndex)).ToList();

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);

        return new AttemptTakeView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam?.Name ?? "",
            attempt.Cert?.Code ?? "",
            EnumCodes.Parse<AttemptStatus>(attempt.Status),
            attempt.StartedAt,
            attempt.ExpiresAt,
            remainingSeconds,
            totalQuestions,
            answeredCount,
            flaggedCount,
            currentQuestionInfo,
            navigator,
            attempt.IsAutoSubmitted,
            allQuestions);
    }

    public async Task<SaveAttemptAnswerResult> SaveAnswerAsync(
        int attemptId,
        int userId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default,
        string? responseText = null)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        // Check if expired on touch -> auto-submit and refuse answer
        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            if (onAutoSubmitted != null)
            {
                await onAutoSubmitted(attempt.AttemptId, userId);
            }
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        var answer = await db.AttemptAnswers
            .Include(a => a.Question)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.QuestionId == questionId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question {questionId} not found in attempt.");

        if (answer.Question.QuestionType == QuestionType.FreeResponse.ToCode())
        {
            if (selectedOptionId.HasValue)
            {
                throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Free-response questions do not accept multiple-choice options.");
            }

            var response = await db.FreeResponses
                .SingleOrDefaultAsync(fr => fr.AttemptId == attemptId && fr.QuestionId == questionId, cancellationToken);
            if (response is null)
            {
                response = new FreeResponse
                {
                    AttemptId = attemptId,
                    QuestionId = questionId,
                    ExamId = attempt.ExamId,
                    SkillId = answer.Question.SkillId,
                    QuestionType = QuestionType.FreeResponse.ToCode(),
                    Status = FreeResponseStatus.Draft.ToCode(),
                    SubmittedAt = null
                };
                db.FreeResponses.Add(response);
            }

            response.ResponseText = responseText ?? string.Empty;
            response.Status = FreeResponseStatus.Draft.ToCode();
            response.SubmittedAt = null;
            response.DraftSavedAt = DateTimeOffset.UtcNow;
        }
        else if (selectedOptionId.HasValue)
        {
            var valid = await db.QuestionOptions
                .AnyAsync(o => o.OptionId == selectedOptionId.Value && o.QuestionId == questionId, cancellationToken);
            if (!valid)
            {
                throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Invalid option for this question.");
            }
        }

        answer.SelectedOptionId = selectedOptionId;
        answer.IsFlagged = isFlagged;
        attempt.LastQuestionId = questionId;

        await db.SaveChangesAsync(cancellationToken);

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return new SaveAttemptAnswerResult(questionId, selectedOptionId, isFlagged, remainingSeconds, attempt.IsAutoSubmitted, responseText);
    }

    public async Task<AttemptGradingResult> SubmitAttemptAsync(
        int attemptId,
        int userId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default)
    {
        var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var attempt = await db.ExamAttempts
                .Include(a => a.Exam)
                .Include(a => a.Cert)
                .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
                ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

            if (attempt.Status == AttemptStatus.Graded.ToCode())
            {
                var existingScores = await db.AttemptSkillScores
                    .Where(s => s.AttemptId == attemptId)
                    .ToListAsync(cancellationToken);
                var existingAnswers = await db.AttemptAnswers
                    .Where(a => a.AttemptId == attemptId)
                    .ToListAsync(cancellationToken);

                if (tx is not null) await tx.RollbackAsync(cancellationToken);

                return new AttemptGradingResult(
                    attempt,
                    attempt.TotalScore ?? 0,
                    attempt.BandCode,
                    existingScores,
                    existingAnswers,
                    attempt.SubmittedAt ?? DateTimeOffset.UtcNow,
                    attempt.IsAutoSubmitted);
            }

            if (attempt.Status == AttemptStatus.Submitted.ToCode())
            {
                var submittedScores = await db.AttemptSkillScores
                    .Where(s => s.AttemptId == attemptId)
                    .ToListAsync(cancellationToken);
                var submittedAnswers = await db.AttemptAnswers
                    .Where(a => a.AttemptId == attemptId)
                    .ToListAsync(cancellationToken);

                if (tx is not null) await tx.RollbackAsync(cancellationToken);

                return new AttemptGradingResult(
                    attempt,
                    attempt.TotalScore ?? 0,
                    attempt.BandCode,
                    submittedScores,
                    submittedAnswers,
                    attempt.SubmittedAt ?? DateTimeOffset.UtcNow,
                    attempt.IsAutoSubmitted);
            }

            if (attempt.Status == AttemptStatus.Abandoned.ToCode())
            {
                throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "This attempt was abandoned.");
            }

            var now = DateTimeOffset.UtcNow;

            if (attempt.Exam.Purpose == ExamPurpose.Mock.ToCode())
            {
                var freeResponses = await db.FreeResponses
                    .Where(fr => fr.AttemptId == attemptId)
                    .ToListAsync(cancellationToken);

                foreach (var response in freeResponses)
                {
                    if (response.Status == FreeResponseStatus.Draft.ToCode())
                    {
                        response.Status = FreeResponseStatus.PendingAi.ToCode();
                        response.SubmittedAt = now;
                    }
                }

                if (freeResponses.Any(fr => fr.Status == FreeResponseStatus.PendingAi.ToCode()))
                {
                    await db.SaveChangesAsync(cancellationToken);

                    foreach (var response in freeResponses.Where(fr => fr.Status == FreeResponseStatus.PendingAi.ToCode()))
                    {
                        var hasGrading = await db.AiGradings
                            .AnyAsync(g => g.FreeResponseId == response.FreeResponseId, cancellationToken);
                        if (!hasGrading)
                        {
                            db.AiGradings.Add(new AiGrading
                            {
                                FreeResponseId = response.FreeResponseId,
                                SkillId = response.SkillId,
                                Status = AiGradingStatus.Pending.ToCode(),
                                AttemptCount = 1,
                                RequestedAt = now
                            });
                        }
                    }
                }
            }

            attempt.SubmittedAt = now;
            if (isAutoSubmit || now >= attempt.ExpiresAt)
            {
                attempt.IsAutoSubmitted = true;
            }

            var hasPendingMockResponses = attempt.Exam.Purpose == ExamPurpose.Mock.ToCode()
                && await db.FreeResponses.AnyAsync(
                    fr => fr.AttemptId == attemptId && fr.Status != FreeResponseStatus.Graded.ToCode(),
                    cancellationToken);
            attempt.Status = hasPendingMockResponses
                ? AttemptStatus.Submitted.ToCode()
                : AttemptStatus.Graded.ToCode();

            var answers = await db.AttemptAnswers
                .Where(a => a.AttemptId == attemptId)
                .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
                .Include(a => a.Question).ThenInclude(q => q.QuestionOptionQuestionNavigations)
                .ToListAsync(cancellationToken);

            var objectiveAnswers = answers
                .Where(ans => ans.Question.QuestionType == QuestionType.Mcq.ToCode())
                .ToList();

            foreach (var ans in objectiveAnswers)
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

            var newSkillScores = new List<AttemptSkillScore>();

            foreach (var group in objectiveAnswers.GroupBy(a => a.Question.Section.Skill))
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

            int totalScore = 0;
            if (hasPendingMockResponses)
            {
                attempt.TotalScore = null;
                attempt.BandCode = null;
            }
            else
            {
                var certCode = await db.Certificates
                    .Where(c => c.CertId == attempt.CertId)
                    .Select(c => c.Code)
                    .SingleAsync(cancellationToken);
                totalScore = AggregateTotalScore(certCode, newSkillScores);
                attempt.TotalScore = totalScore;
                attempt.BandCode = await FindBandCodeAsync(attempt.CertId, totalScore, cancellationToken);
            }

            // Snapshot matching invariant: PlacementTests.TotalScore must match ExamAttempts.TotalScore
            if (!hasPendingMockResponses)
            {
                var linkedPlacements = await db.PlacementTests
                    .Where(p => p.AttemptId == attempt.AttemptId && p.Status == PlacementStatus.InProgress.ToCode())
                    .ToListAsync(cancellationToken);

                foreach (var placement in linkedPlacements)
                {
                    placement.Status = PlacementStatus.Done.ToCode();
                    placement.TotalScore = totalScore;
                    placement.ResultBandCode = attempt.BandCode;
                    placement.TakenAt = now;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);

            return new AttemptGradingResult(
                attempt,
                totalScore,
                attempt.BandCode,
                newSkillScores,
                answers,
                now,
                attempt.IsAutoSubmitted);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    public async Task FinalizeAiGradedAttemptAsync(int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Exam.Purpose != ExamPurpose.Mock.ToCode())
        {
            if (attempt.Status != AttemptStatus.Graded.ToCode())
            {
                attempt.Status = AttemptStatus.Graded.ToCode();
                attempt.TotalScore = null;
                attempt.BandCode = null;
                await db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        if (attempt.Status == AttemptStatus.Graded.ToCode()) return;
        if (attempt.Status != AttemptStatus.Submitted.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "The mock attempt must be submitted before AI grading can be finalized.");
        }

        var freeResponses = await db.FreeResponses
            .Where(fr => fr.AttemptId == attemptId)
            .Include(fr => fr.Question).ThenInclude(q => q.Section)
            .Include(fr => fr.AiGradingFreeResponse)
            .ToListAsync(cancellationToken);

        if (freeResponses.Count == 0
            || freeResponses.Any(fr => fr.Status != FreeResponseStatus.Graded.ToCode()
                || fr.AiGradingFreeResponse?.Status != AiGradingStatus.Done.ToCode()))
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotFinished, "All free responses must finish AI grading before the mock result can be finalized.");
        }

        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attemptId)
            .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
            .Include(a => a.Question).ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .ToListAsync(cancellationToken);

        var existingScores = await db.AttemptSkillScores
            .Where(s => s.AttemptId == attemptId)
            .ToListAsync(cancellationToken);
        db.AttemptSkillScores.RemoveRange(existingScores);

        var responsePointsBySkill = new Dictionary<int, decimal>();
        foreach (var response in freeResponses)
        {
            var sectionCriteria = await db.SectionCriteria
                .Where(sc => sc.SectionId == response.Question.SectionId)
                .Include(sc => sc.GradingCriterion)
                .ToListAsync(cancellationToken);

            var maxScore = sectionCriteria.Count > 0
                ? sectionCriteria.Sum(sc => sc.MaxScore ?? sc.GradingCriterion.MaxScore)
                : await db.GradingCriteria
                    .Where(c => c.SkillId == response.SkillId)
                    .SumAsync(c => (decimal?)c.MaxScore, cancellationToken) ?? 0m;

            if (maxScore <= 0m)
            {
                throw new AttemptEngineException(
                    AttemptEngineException.InvalidGradingRubric,
                    $"Free-response question {response.QuestionId} has no positive rubric maximum.");
            }

            var responseFraction = Math.Clamp((response.AiGradingFreeResponse!.OverallScaled ?? 0m) / maxScore, 0m, 1m);
            responsePointsBySkill[response.SkillId] = responsePointsBySkill.GetValueOrDefault(response.SkillId) + responseFraction;
        }

        foreach (var answer in answers.Where(a => a.Question.QuestionType == QuestionType.Mcq.ToCode()))
        {
            var correctOption = answer.Question.QuestionOptionQuestionNavigations.FirstOrDefault(o => o.IsCorrect);
            answer.IsCorrect = answer.SelectedOptionId.HasValue && answer.SelectedOptionId.Value == correctOption?.OptionId;
        }

        var newSkillScores = new List<AttemptSkillScore>();
        foreach (var group in answers.GroupBy(a => a.Question.Section.Skill))
        {
            var skill = group.Key;
            var objectiveAnswers = group.Where(a => a.Question.QuestionType == QuestionType.Mcq.ToCode()).ToList();
            var objectiveRawScore = objectiveAnswers.Count(a => a.IsCorrect == true);
            var rawScore = objectiveRawScore + responsePointsBySkill.GetValueOrDefault(skill.SkillId);
            var rawScoreInteger = (int)Math.Round(rawScore, MidpointRounding.AwayFromZero);
            var scaledScore = await ComputeScaledScoreAsync(
                attempt.CertId,
                skill.SkillId,
                attempt.ExamId,
                rawScore,
                group.Count(),
                cancellationToken);

            newSkillScores.Add(new AttemptSkillScore
            {
                AttemptId = attempt.AttemptId,
                SkillId = skill.SkillId,
                CertId = attempt.CertId,
                RawScore = rawScoreInteger,
                ScaledScore = scaledScore
            });
        }

        db.AttemptSkillScores.AddRange(newSkillScores);
        var certCode = await db.Certificates
            .Where(c => c.CertId == attempt.CertId)
            .Select(c => c.Code)
            .SingleAsync(cancellationToken);
        var totalScore = AggregateTotalScore(certCode, newSkillScores);
        attempt.TotalScore = totalScore;
        attempt.BandCode = await FindBandCodeAsync(attempt.CertId, totalScore, cancellationToken);
        attempt.Status = AttemptStatus.Graded.ToCode();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ComputeScaledScoreAsync(
        int certId,
        int skillId,
        int examId,
        int rawScore,
        int totalQuestionsInSkill,
        CancellationToken cancellationToken = default)
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
        var certCode = await db.Certificates
            .Where(c => c.CertId == certId)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken);
        var (minimum, maximum, step) = GetFallbackScale(certCode);
        if (totalQuestionsInSkill <= 0) return minimum;
        double fraction = Math.Clamp((double)rawScore / totalQuestionsInSkill, 0.0, 1.0);
        return minimum + (int)Math.Round(
            (fraction * (maximum - minimum)) / step,
            MidpointRounding.AwayFromZero) * step;
    }

    private async Task<int> ComputeScaledScoreAsync(
        int certId,
        int skillId,
        int examId,
        decimal rawScore,
        int totalQuestionsInSkill,
        CancellationToken cancellationToken)
    {
        if (rawScore == decimal.Truncate(rawScore))
        {
            return await ComputeScaledScoreAsync(
                certId,
                skillId,
                examId,
                (int)rawScore,
                totalQuestionsInSkill,
                cancellationToken);
        }

        var scaleRows = await db.ScoreScales
            .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == examId)
            .OrderBy(s => s.RawScore)
            .ToListAsync(cancellationToken);
        if (scaleRows.Count == 0)
        {
            scaleRows = await db.ScoreScales
                .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == null)
                .OrderBy(s => s.RawScore)
                .ToListAsync(cancellationToken);
        }

        if (scaleRows.Count > 0)
        {
            var lower = scaleRows.LastOrDefault(row => row.RawScore <= rawScore);
            var upper = scaleRows.FirstOrDefault(row => row.RawScore >= rawScore);
            decimal scaled = lower is null
                ? scaleRows[0].ScaledScore
                : upper is null
                    ? scaleRows[^1].ScaledScore
                    : lower.RawScore == upper.RawScore
                        ? lower.ScaledScore
                        : lower.ScaledScore
                            + (rawScore - lower.RawScore)
                            * (upper.ScaledScore - lower.ScaledScore)
                            / (upper.RawScore - lower.RawScore);

            var certCode = await db.Certificates
                .Where(c => c.CertId == certId)
                .Select(c => c.Code)
                .SingleAsync(cancellationToken);
            var step = GetFallbackScale(certCode).Step;
            return (int)Math.Round(scaled / step, MidpointRounding.AwayFromZero) * step;
        }

        var fallbackCertCode = await db.Certificates
            .Where(c => c.CertId == certId)
            .Select(c => c.Code)
            .SingleAsync(cancellationToken);
        var (minimum, maximum, fallbackStep) = GetFallbackScale(fallbackCertCode);
        if (totalQuestionsInSkill <= 0) return minimum;
        var fraction = Math.Clamp((double)rawScore / totalQuestionsInSkill, 0.0, 1.0);
        return minimum + (int)Math.Round(
            (fraction * (maximum - minimum)) / fallbackStep,
            MidpointRounding.AwayFromZero) * fallbackStep;
    }

    private async Task<string?> FindBandCodeAsync(
        int certId,
        int totalScore,
        CancellationToken cancellationToken)
    {
        var bands = await db.ScoreBands
            .Where(b => b.CertId == certId)
            .OrderBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        var matchingBand = bands.FirstOrDefault(b => totalScore >= b.MinTotal && totalScore <= b.MaxTotal);
        if (matchingBand is null && bands.Count > 0)
        {
            matchingBand = totalScore < bands[0].MinTotal
                ? bands[0]
                : bands.LastOrDefault(b => totalScore >= b.MinTotal) ?? bands[^1];
        }

        return matchingBand?.Code;
    }

    private static (int Minimum, int Maximum, int Step) GetFallbackScale(string? certCode) =>
        certCode?.ToUpperInvariant() switch
        {
            "IELTS" => (10, 90, 5),
            "VSTEP" => (10, 100, 1),
            _ => (5, 495, 5)
        };

    private static int AggregateTotalScore(
        string certCode,
        IReadOnlyCollection<AttemptSkillScore> skillScores)
    {
        if (skillScores.Count == 0) return 0;

        var average = skillScores.Average(score => (double)score.ScaledScore);
        return certCode.ToUpperInvariant() switch
        {
            "IELTS" => (int)Math.Round(average / 5.0, MidpointRounding.AwayFromZero) * 5,
            "VSTEP" => (int)Math.Round(average, MidpointRounding.AwayFromZero),
            _ => skillScores.Sum(score => score.ScaledScore)
        };
    }

    public async Task<AttemptResultView> GetAttemptResultAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status == AttemptStatus.InProgress.ToCode() && DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            var grading = await SubmitAttemptAsync(attemptId, userId, isAutoSubmit: true, cancellationToken);
            attempt = grading.Attempt;
        }

        if (attempt.Status != AttemptStatus.Graded.ToCode()
            && attempt.Status != AttemptStatus.Submitted.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotFinished, "Exam attempt is not finished.");
        }

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
                EnumCodes.Parse<Modality>(s.Skill.Modality),
                s.RawScore,
                s.ScaledScore,
                questionCounts.TryGetValue(s.SkillId, out var count) ? count : 0))
            .ToList();

        var band = await db.ScoreBands
            .Where(b => b.CertId == attempt.CertId && b.Code == attempt.BandCode)
            .FirstOrDefaultAsync(cancellationToken);

        var prevAttempt = await db.ExamAttempts
            .Where(a => a.UserId == attempt.UserId && a.CertId == attempt.CertId && a.AttemptId != attempt.AttemptId && a.Status == AttemptStatus.Graded.ToCode() && a.TotalScore.HasValue)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        int? prevScore = prevAttempt?.TotalScore;
        int? scoreDelta = prevScore.HasValue && attempt.TotalScore.HasValue
            ? attempt.TotalScore.Value - prevScore.Value
            : null;

        var gradingStates = await db.AiGradings
            .AsNoTracking()
            .Where(g => g.FreeResponse.AttemptId == attemptId)
            .Select(g => g.Status)
            .ToListAsync(cancellationToken);
        var freeResponseCount = await db.FreeResponses
            .CountAsync(fr => fr.AttemptId == attemptId, cancellationToken);
        var gradedFreeResponseCount = gradingStates.Count(status => status == AiGradingStatus.Done.ToCode());
        var failedFreeResponseCount = gradingStates.Count(status => status == AiGradingStatus.Failed.ToCode());

        int durationSeconds = (int)Math.Max(0, ((attempt.SubmittedAt ?? DateTimeOffset.UtcNow) - attempt.StartedAt).TotalSeconds);

        return new AttemptResultView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam!.Name,
            attempt.CertId,
            attempt.Cert!.Code,
            attempt.Cert!.Name,
            EnumCodes.Parse<AttemptStatus>(attempt.Status),
            attempt.StartedAt,
            attempt.SubmittedAt ?? DateTimeOffset.UtcNow,
            durationSeconds,
            attempt.IsAutoSubmitted,
            attempt.TotalScore ?? 0,
            attempt.BandCode,
            band?.Name,
            scoreItems,
            prevScore,
            scoreDelta,
            freeResponseCount,
            gradedFreeResponseCount,
            failedFreeResponseCount);
    }

    public async Task<AttemptReviewView> GetAttemptReviewAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.UserId != userId)
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");
        }

        if (attempt.Status != AttemptStatus.Graded.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotFinished, "Review is available only for finished exam attempts.");
        }

        var questions = await db.Questions
            .AsNoTracking()
            .Where(q => q.ExamId == attempt.ExamId)
            .Include(q => q.Section).ThenInclude(s => s.Skill)
            .Include(q => q.Group)
            .Include(q => q.QuestionOptionQuestionNavigations)
            .OrderBy(q => q.Section.Skill.DisplayOrder)
            .ThenBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .ToListAsync(cancellationToken);

        var answers = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId)
            .ToDictionaryAsync(a => a.QuestionId, cancellationToken);

        var skillScores = await db.AttemptSkillScores
            .AsNoTracking()
            .Where(s => s.AttemptId == attemptId)
            .Include(s => s.Skill)
            .OrderBy(s => s.Skill.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questionCounts = questions
            .GroupBy(q => q.Section.SkillId)
            .ToDictionary(g => g.Key, g => g.Count());

        var scoreItems = skillScores
            .Select(s => new AttemptSkillScoreItem(
                s.SkillId,
                s.Skill.Code,
                s.Skill.Name,
                EnumCodes.Parse<Modality>(s.Skill.Modality),
                s.RawScore,
                s.ScaledScore,
                questionCounts.TryGetValue(s.SkillId, out var count) ? count : 0))
            .ToList();

        var band = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == attempt.CertId && b.Code == attempt.BandCode)
            .FirstOrDefaultAsync(cancellationToken);

        var sectionMasterAudio = await LoadSectionMasterAudioAsync(attempt.ExamId, cancellationToken);
        var reviewQuestions = new List<AttemptReviewQuestion>();
        var navigator = new List<ReviewNavigatorItem>();

        for (int idx = 0; idx < questions.Count; idx++)
        {
            var q = questions[idx];
            int displayOrder = idx + 1;

            var options = q.QuestionOptionQuestionNavigations
                .OrderBy(o => o.Label)
                .Select(o => new AttemptReviewOption(o.OptionId, o.Label, o.Text, o.IsCorrect, o.Rationale))
                .ToList();

            bool hasAnswer = answers.TryGetValue(q.QuestionId, out var ans);
            int? selectedOptionId = ans?.SelectedOptionId;
            bool isFlagged = ans?.IsFlagged ?? false;
            bool isAnswered = selectedOptionId.HasValue;
            bool? isCorrect = isAnswered ? ans?.IsCorrect : null;
            var audio = ResolveGroupAudio(q.Group, sectionMasterAudio);

            reviewQuestions.Add(new AttemptReviewQuestion(
                q.QuestionId,
                displayOrder,
                questions.Count,
                q.SectionId,
                q.Section.Code,
                q.Section.Name,
                q.Section.Skill.Code,
                q.Section.Skill.Name,
                EnumCodes.Parse<Modality>(q.Section.Modality),
                q.Stem,
                q.Group.ImagePath,
                q.Group.ImageAlt,
                audio?.Path,
                audio?.StartMs,
                audio?.EndMs,
                q.Group.Transcript,
                q.Group.Passage,
                q.Explanation,
                options,
                selectedOptionId,
                isCorrect,
                isFlagged));

            navigator.Add(new ReviewNavigatorItem(
                q.QuestionId,
                displayOrder,
                q.SectionId,
                q.Section.Code,
                EnumCodes.Parse<Modality>(q.Section.Modality),
                isAnswered,
                isFlagged,
                isCorrect));
        }

        int totalQuestions = questions.Count;
        int answeredCount = questions.Count(q => answers.TryGetValue(q.QuestionId, out var a) && a.SelectedOptionId.HasValue);
        int correctCount = questions.Count(q => answers.TryGetValue(q.QuestionId, out var a) && a.IsCorrect == true);
        int incorrectCount = questions.Count(q => answers.TryGetValue(q.QuestionId, out var a) && a.SelectedOptionId.HasValue && a.IsCorrect == false);
        int blankCount = totalQuestions - answeredCount;
        int flaggedCount = questions.Count(q => answers.TryGetValue(q.QuestionId, out var a) && a.IsFlagged);
        int durationSeconds = (int)Math.Max(0, ((attempt.SubmittedAt ?? DateTimeOffset.UtcNow) - attempt.StartedAt).TotalSeconds);

        return new AttemptReviewView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Exam?.Name ?? "Mock Exam",
            attempt.CertId,
            attempt.Cert?.Code ?? "",
            attempt.Cert?.Name ?? "",
            EnumCodes.Parse<AttemptStatus>(attempt.Status),
            attempt.StartedAt,
            attempt.SubmittedAt ?? attempt.StartedAt,
            durationSeconds,
            attempt.TotalScore ?? 0,
            attempt.BandCode,
            band?.Name ?? attempt.BandCode,
            scoreItems,
            totalQuestions,
            answeredCount,
            correctCount,
            incorrectCount,
            blankCount,
            flaggedCount,
            reviewQuestions,
            navigator);
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
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        attempt.Status = AttemptStatus.Abandoned.ToCode();
        await db.SaveChangesAsync(cancellationToken);
    }
}
