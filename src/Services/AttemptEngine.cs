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
    public const string WrongPhase = "wrong_phase";

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

    Task<SaveAttemptDraftResult> SaveDraftAsync(
        int attemptId,
        int userId,
        int questionId,
        string? responseText,
        CancellationToken cancellationToken = default);

    Task<AttemptGradingResult> SubmitAttemptAsync(
        int attemptId,
        int userId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default,
        IReadOnlyList<AttemptFinalDraft>? finalDrafts = null);

    Task<AttemptResultView> GetAttemptResultAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

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

    Task FinalizeAiGradedAttemptAsync(int attemptId, CancellationToken cancellationToken = default);

    Task CancelAttemptAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);
}

public sealed class AttemptEngine(CompanionDbContext db) : IAttemptEngine
{
    private sealed record AttemptSkillPhaseSource(
        int SkillId,
        string Code,
        string Name,
        int DisplayOrder,
        int? DurationMinutes);

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
                    if (q.QuestionType == QuestionType.Mcq.ToCode())
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
                    else if (q.QuestionType == QuestionType.FreeResponse.ToCode())
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

        var questions = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId)
            .Include(q => q.Group)
            .Include(q => q.Section).ThenInclude(s => s.Skill)
            .Include(q => q.QuestionOptionQuestionNavigations)
            .OrderBy(q => q.Section.Skill.DisplayOrder)
            .ThenBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .ToListAsync(cancellationToken);

        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attemptId)
            .ToListAsync(cancellationToken);
        var answersByQuestion = answers.ToDictionary(a => a.QuestionId);
        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Where(r => r.AttemptId == attemptId)
            .ToDictionaryAsync(r => r.QuestionId, cancellationToken);

        int totalQuestions = questions.Count;
        int answeredCount = questions.Count(q =>
            q.QuestionType == QuestionType.Mcq.ToCode()
                ? answersByQuestion.TryGetValue(q.QuestionId, out var answer) && answer.SelectedOptionId.HasValue
                : freeResponses.TryGetValue(q.QuestionId, out var response)
                    && (!string.IsNullOrWhiteSpace(response.ResponseText) || !string.IsNullOrWhiteSpace(response.AudioPath)));
        int flaggedCount = answers.Count(a => a.IsFlagged);

        // Determine current question
        int targetIndex = 0;
        if (questionDisplayOrder.HasValue && questionDisplayOrder.Value >= 1 && questionDisplayOrder.Value <= totalQuestions)
        {
            targetIndex = questionDisplayOrder.Value - 1;
        }
        else if (attempt.LastQuestionId.HasValue)
        {
            var idx = questions.FindIndex(q => q.QuestionId == attempt.LastQuestionId.Value);
            if (idx >= 0) targetIndex = idx;
        }

        var sectionMasterAudio = await LoadSectionMasterAudioAsync(attempt.ExamId, cancellationToken);
        var allQuestions = new List<AttemptQuestionInfo>(totalQuestions);
        for (int i = 0; i < totalQuestions; i++)
        {
            var q = questions[i];
            answersByQuestion.TryGetValue(q.QuestionId, out var ans);
            freeResponses.TryGetValue(q.QuestionId, out var response);
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
                ans?.SelectedOptionId,
                ans?.IsFlagged ?? false,
                q.QuestionType,
                q.MinWords,
                q.MaxWords,
                q.PrepSeconds,
                q.ResponseSeconds,
                response?.ResponseText,
                response?.AudioPath,
                response?.DurationMs,
                response?.DraftSavedAt,
                q.Section.Skill.DurationMinutes,
                q.Section.Skill.DisplayOrder));
        }

        var currentQuestionInfo = totalQuestions > 0 ? allQuestions[targetIndex] : new AttemptQuestionInfo(
            0, 0, 0, 0, "", "", "", "", Modality.Reading, "", null, null, null, null, null, null, null, [], null, false);

        var navigator = questions.Select((q, idx) =>
        {
            answersByQuestion.TryGetValue(q.QuestionId, out var answer);
            freeResponses.TryGetValue(q.QuestionId, out var response);
            return new NavigatorItem(
            q.QuestionId,
            idx + 1,
            q.SectionId,
            q.Section.Code,
            EnumCodes.Parse<Modality>(q.Section.Modality),
            q.QuestionType == QuestionType.Mcq.ToCode()
                ? answer?.SelectedOptionId != null
                : !string.IsNullOrWhiteSpace(response?.ResponseText) || !string.IsNullOrWhiteSpace(response?.AudioPath),
            answer?.IsFlagged ?? false,
            idx == targetIndex);
        }).ToList();

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        var phaseGroups = questions
            .GroupBy(question => question.Section.SkillId)
            .Select(group => new AttemptSkillPhaseSource(
                group.Key,
                group.First().Section.Skill.Code,
                group.First().Section.Skill.Name,
                group.First().Section.Skill.DisplayOrder,
                group.First().Section.Skill.DurationMinutes))
            .OrderBy(skill => skill.DisplayOrder)
            .ToList();
        var phases = BuildAttemptPhases(
            phaseGroups,
            Math.Max(1, (int)(attempt.ExpiresAt - attempt.StartedAt).TotalSeconds));

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
            allQuestions,
            phases);
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

        await EnsureQuestionInActivePhaseAsync(attempt, questionId, cancellationToken);

        var answer = await db.AttemptAnswers
            .Include(a => a.Question)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.QuestionId == questionId, cancellationToken);

        if (answer is null)
        {
            return await SaveFreeResponseInternalAsync(
                attempt, attemptId, questionId, selectedOptionId, responseText, isFlagged, null, cancellationToken);
        }

        if (answer.Question.QuestionType == QuestionType.FreeResponse.ToCode())
        {
            return await SaveFreeResponseInternalAsync(
                attempt, attemptId, questionId, selectedOptionId, responseText, isFlagged, answer, cancellationToken);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Multiple-choice questions do not accept free-response text.");
            }

            if (selectedOptionId.HasValue)
            {
                var valid = await db.QuestionOptions
                    .AnyAsync(o => o.OptionId == selectedOptionId.Value && o.QuestionId == questionId, cancellationToken);
                if (!valid)
                {
                    throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Invalid option for this question.");
                }
            }

            answer.SelectedOptionId = selectedOptionId;
        }

        answer.IsFlagged = isFlagged;
        attempt.LastQuestionId = questionId;

        await db.SaveChangesAsync(cancellationToken);

        var remainingSeconds = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return new SaveAttemptAnswerResult(questionId, selectedOptionId, isFlagged, remainingSeconds, attempt.IsAutoSubmitted);
    }

    public async Task<SaveAttemptDraftResult> SaveDraftAsync(
        int attemptId,
        int userId,
        int questionId,
        string? responseText,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        await EnsureQuestionInActivePhaseAsync(attempt, questionId, cancellationToken);

        var freeResponse = await db.FreeResponses
            .SingleOrDefaultAsync(response =>
                response.AttemptId == attemptId &&
                response.QuestionId == questionId &&
                response.Status == FreeResponseStatus.Draft.ToCode(),
                cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Free-response question {questionId} not found in attempt.");

        var savedAt = DateTimeOffset.UtcNow;
        freeResponse.ResponseText = responseText ?? string.Empty;
        freeResponse.DraftSavedAt = savedAt;
        attempt.LastQuestionId = questionId;
        await db.SaveChangesAsync(cancellationToken);

        var words = freeResponse.ResponseText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;
        return new SaveAttemptDraftResult(freeResponse.FreeResponseId, questionId, savedAt, words);
    }

    private async Task EnsureQuestionInActivePhaseAsync(
        ExamAttempt attempt,
        int questionId,
        CancellationToken cancellationToken)
    {
        var questionSkill = await db.Questions
            .Where(question => question.QuestionId == questionId && question.ExamId == attempt.ExamId)
            .Select(question => new
            {
                question.Section.SkillId,
                question.Section.Skill.DisplayOrder
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question {questionId} not found in attempt.");

        var skillRows = await db.Questions
            .Where(question => question.ExamId == attempt.ExamId)
            .Select(question => new
            {
                question.Section.SkillId,
                question.Section.Skill.Code,
                question.Section.Skill.Name,
                question.Section.Skill.DisplayOrder,
                question.Section.Skill.DurationMinutes
            })
            .Distinct()
            .OrderBy(skill => skill.DisplayOrder)
            .ToListAsync(cancellationToken);
        var skills = skillRows
            .Select(skill => new AttemptSkillPhaseSource(
                skill.SkillId,
                skill.Code,
                skill.Name,
                skill.DisplayOrder,
                skill.DurationMinutes))
            .ToList();
        var phases = BuildAttemptPhases(
            skills,
            Math.Max(1, (int)(attempt.ExpiresAt - attempt.StartedAt).TotalSeconds));
        var elapsedSeconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - attempt.StartedAt).TotalSeconds);
        var activePhase = phases.LastOrDefault(phase => elapsedSeconds >= phase.StartOffsetSeconds);
        if (activePhase is null || elapsedSeconds >= activePhase.StartOffsetSeconds + activePhase.DurationSeconds ||
            activePhase.SkillId != questionSkill.SkillId)
        {
            throw new AttemptEngineException(AttemptEngineException.WrongPhase, "This question is not available in the current exam phase.");
        }
    }

    private static IReadOnlyList<AttemptPhaseInfo> BuildAttemptPhases(
        IReadOnlyList<AttemptSkillPhaseSource> skills,
        int totalSeconds)
    {
        if (skills.Count == 0)
        {
            return [];
        }

        var positiveWeights = skills
            .Select(skill => (int?)skill.DurationMinutes)
            .Where(duration => duration > 0)
            .Select(duration => duration!.Value)
            .ToList();
        var fallbackWeight = positiveWeights.Count > 0
            ? positiveWeights.Average()
            : 1d;
        var weights = skills
            .Select(skill => skill.DurationMinutes is int duration && duration > 0 ? duration : fallbackWeight)
            .ToList();
        var totalWeight = weights.Sum();
        var allocatedSeconds = 0;
        var phases = new List<AttemptPhaseInfo>(skills.Count);

        for (var index = 0; index < skills.Count; index++)
        {
            var skill = skills[index];
            var durationSeconds = index == skills.Count - 1
                ? totalSeconds - allocatedSeconds
                : (int)Math.Floor(totalSeconds * weights[index] / totalWeight);
            phases.Add(new AttemptPhaseInfo(
                skill.SkillId,
                skill.Code,
                skill.Name,
                skill.DisplayOrder,
                allocatedSeconds,
                durationSeconds));
            allocatedSeconds += durationSeconds;
        }

        return phases;
    }

    public async Task<AttemptGradingResult> SubmitAttemptAsync(
        int attemptId,
        int userId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default,
        IReadOnlyList<AttemptFinalDraft>? finalDrafts = null)
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

            if (attempt.Status == AttemptStatus.Graded.ToCode()
                || attempt.Status == AttemptStatus.Submitted.ToCode())
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

            if (attempt.Status == AttemptStatus.Abandoned.ToCode())
            {
                throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "This attempt was abandoned.");
            }
            var now = DateTimeOffset.UtcNow;
            if (isAutoSubmit || now >= attempt.ExpiresAt)
            {
                attempt.IsAutoSubmitted = true;
            }

            var freeResponses = attempt.Exam.Purpose == ExamPurpose.Mock.ToCode() || attempt.Exam.Purpose == ExamPurpose.Placement.ToCode()
                ? await db.FreeResponses.Where(fr => fr.AttemptId == attemptId).ToListAsync(cancellationToken)
                : [];
            if (finalDrafts is { Count: > 0 })
            {
                if (finalDrafts.Select(draft => draft.QuestionId).Distinct().Count() != finalDrafts.Count)
                {
                    throw new AttemptEngineException(
                        AttemptEngineException.QuestionNotFound,
                        "A final response draft was submitted more than once.");
                }

                foreach (var finalDraft in finalDrafts)
                {
                    var response = freeResponses.SingleOrDefault(item => item.QuestionId == finalDraft.QuestionId);
                    if (response is null)
                    {
                        var question = await db.Questions
                            .SingleOrDefaultAsync(q =>
                                q.QuestionId == finalDraft.QuestionId
                                && q.ExamId == attempt.ExamId
                                && q.QuestionType == QuestionType.FreeResponse.ToCode(),
                                cancellationToken)
                            ?? throw new AttemptEngineException(
                                AttemptEngineException.QuestionNotFound,
                                $"Free-response question {finalDraft.QuestionId} was not found for this attempt.");

                        response = new FreeResponse
                        {
                            AttemptId = attemptId,
                            QuestionId = question.QuestionId,
                            ExamId = attempt.ExamId,
                            SkillId = question.SkillId,
                            QuestionType = QuestionType.FreeResponse.ToCode(),
                            ResponseText = finalDraft.ResponseText ?? string.Empty,
                            Status = FreeResponseStatus.Draft.ToCode(),
                            DraftSavedAt = now,
                            SubmittedAt = null
                        };
                        freeResponses.Add(response);
                        db.FreeResponses.Add(response);
                    }
                    else
                    {
                        response.ResponseText = finalDraft.ResponseText ?? string.Empty;
                        response.DraftSavedAt = now;
                    }
                }
            }

            foreach (var response in freeResponses.Where(fr => fr.Status == FreeResponseStatus.Draft.ToCode()))
            {
                response.Status = FreeResponseStatus.PendingAi.ToCode();
                response.SubmittedAt = now;
            }

            if (freeResponses.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            var hasPendingMockResponses = freeResponses.Count > 0
                && freeResponses.Any(fr => fr.Status != FreeResponseStatus.Graded.ToCode());
            attempt.SubmittedAt = now;
            attempt.Status = hasPendingMockResponses
                ? AttemptStatus.Submitted.ToCode()
                : AttemptStatus.Graded.ToCode();

            var answers = await db.AttemptAnswers
                .Where(a => a.AttemptId == attemptId)
                .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
                .ToListAsync(cancellationToken);
            var correctOptionsByQuestion = await db.QuestionOptions
                .Where(option =>
                    option.IsCorrect &&
                    answers.Select(answer => answer.QuestionId).Contains(option.QuestionId))
                .ToDictionaryAsync(option => option.QuestionId, cancellationToken);

            // Score each answer
            foreach (var ans in answers.Where(a => a.Question.QuestionType == QuestionType.Mcq.ToCode()))
            {
                ans.IsCorrect = ans.SelectedOptionId.HasValue &&
                    correctOptionsByQuestion.TryGetValue(ans.QuestionId, out var correctOption) &&
                    ans.SelectedOptionId.Value == correctOption.OptionId;
            }

            // Clean any existing skill scores for idempotent resubmits
            var existingSkillScores = await db.AttemptSkillScores
                .Where(s => s.AttemptId == attemptId)
                .ToListAsync(cancellationToken);
            if (existingSkillScores.Count > 0)
            {
                db.AttemptSkillScores.RemoveRange(existingSkillScores);
            }

            var skillGroups = answers
                .Where(a => a.Question.QuestionType == QuestionType.Mcq.ToCode())
                .GroupBy(a => a.Question.Section.Skill)
                .ToList();
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

            var certCode = await db.Certificates
                .Where(c => c.CertId == attempt.CertId)
                .Select(c => c.Code)
                .SingleAsync(cancellationToken);
            var totalScore = AggregateTotalScore(certCode, newSkillScores);
            attempt.TotalScore = totalScore;
            string? matchingBandCode = null;
            if (!hasPendingMockResponses)
            {
                matchingBandCode = await FindBandCodeAsync(attempt.CertId, totalScore, cancellationToken);
                attempt.BandCode = matchingBandCode;
            }
            else
            {
                attempt.BandCode = null;
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

            var hasFreeResponses = freeResponses.Count > 0;
            attempt.Status = hasFreeResponses
                ? AttemptStatus.Submitted.ToCode()
                : AttemptStatus.Graded.ToCode();
            attempt.SubmittedAt = now;
            attempt.TotalScore = totalScore;
            attempt.BandCode = hasPendingMockResponses ? null : matchingBandCode;

            if (freeResponses.Count > 0)
            {
                var responseIds = freeResponses.Select(r => r.FreeResponseId).ToList();
                var existingGradingIds = await db.AiGradings
                    .Where(g => responseIds.Contains(g.FreeResponseId))
                    .Select(g => g.FreeResponseId)
                    .ToListAsync(cancellationToken);

                foreach (var response in freeResponses.Where(r => !existingGradingIds.Contains(r.FreeResponseId)))
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
        var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var attempt = await db.ExamAttempts
                .Include(a => a.Exam)
                .SingleOrDefaultAsync(a => a.AttemptId == attemptId, cancellationToken)
                ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

            if (attempt.Status == AttemptStatus.Graded.ToCode()) return;
            if ((attempt.Exam.Purpose != ExamPurpose.Mock.ToCode() && attempt.Exam.Purpose != ExamPurpose.Placement.ToCode())
                || attempt.Status != AttemptStatus.Submitted.ToCode())
            {
                throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Only a submitted mock or placement attempt can be finalized after AI grading.");
            }

            var freeResponses = await db.FreeResponses
                .Where(fr => fr.AttemptId == attemptId)
                .Include(fr => fr.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
                .Include(fr => fr.AiGradingFreeResponse)
                .ToListAsync(cancellationToken);

            if (freeResponses.Count == 0
                || freeResponses.Any(fr => fr.Status != FreeResponseStatus.Graded.ToCode()
                    || fr.AiGradingFreeResponse?.Status != AiGradingStatus.Done.ToCode()))
            {
                throw new AttemptEngineException(AttemptEngineException.AttemptNotFinished, "All free responses must finish AI grading before the result can be finalized.");
            }

            var answers = await db.AttemptAnswers
                .Where(a => a.AttemptId == attemptId)
                .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
                .Include(a => a.Question).ThenInclude(q => q.QuestionOptionQuestionNavigations)
                .ToListAsync(cancellationToken);

            attempt.TotalScore = null;
            await db.SaveChangesAsync(cancellationToken);

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

                var fraction = maxScore > 0m
                    ? Math.Clamp((response.AiGradingFreeResponse!.OverallScaled ?? 0m) / maxScore, 0m, 1m)
                    : 0m;
                responsePointsBySkill[response.SkillId] = responsePointsBySkill.GetValueOrDefault(response.SkillId) + fraction;
            }

            foreach (var answer in answers.Where(a => a.Question.QuestionType == QuestionType.Mcq.ToCode()))
            {
                var correctOption = answer.Question.QuestionOptionQuestionNavigations.FirstOrDefault(o => o.IsCorrect);
                answer.IsCorrect = answer.SelectedOptionId.HasValue && answer.SelectedOptionId.Value == correctOption?.OptionId;
            }

            var skills = answers.Select(a => a.Question.Section.Skill)
                .Concat(freeResponses.Select(fr => fr.Question.Section.Skill))
                .DistinctBy(s => s.SkillId)
                .ToList();

            var newSkillScores = new List<AttemptSkillScore>();
            foreach (var skill in skills)
            {
                var objectiveAnswers = answers.Where(a => a.Question.Section.SkillId == skill.SkillId && a.Question.QuestionType == QuestionType.Mcq.ToCode()).ToList();
                var freeResponsesInSkill = freeResponses.Where(fr => fr.SkillId == skill.SkillId).ToList();
                var totalQuestionsInSkill = objectiveAnswers.Count + freeResponsesInSkill.Count;
                if (totalQuestionsInSkill == 0) continue;

                var hasScales = await db.ScoreScales.AnyAsync(s => s.CertId == attempt.CertId && s.SkillId == skill.SkillId, cancellationToken);
                if (objectiveAnswers.Count == 0 && !hasScales) continue;

                var rawScore = objectiveAnswers.Count(a => a.IsCorrect == true) + responsePointsBySkill.GetValueOrDefault(skill.SkillId);
                var scaledScore = await ComputeScaledScoreAsync(
                    attempt.CertId,
                    skill.SkillId,
                    attempt.ExamId,
                    rawScore,
                    totalQuestionsInSkill,
                    cancellationToken);

                newSkillScores.Add(new AttemptSkillScore
                {
                    AttemptId = attempt.AttemptId,
                    SkillId = skill.SkillId,
                    CertId = attempt.CertId,
                    RawScore = (int)Math.Round(rawScore, MidpointRounding.AwayFromZero),
                    ScaledScore = scaledScore
                });
            }

            db.AttemptSkillScores.AddRange(newSkillScores);
            await db.SaveChangesAsync(cancellationToken);

            var certCode = await db.Certificates
                .Where(c => c.CertId == attempt.CertId)
                .Select(c => c.Code)
                .SingleAsync(cancellationToken);
            var totalScore = AggregateTotalScore(certCode, newSkillScores);
            attempt.TotalScore = totalScore;
            attempt.BandCode = await FindBandCodeAsync(attempt.CertId, totalScore, cancellationToken);
            attempt.Status = AttemptStatus.Graded.ToCode();
            await db.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
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

        var certCode = await db.Certificates
            .Where(c => c.CertId == certId)
            .Select(c => c.Code)
            .SingleAsync(cancellationToken);
        var step = GetFallbackScale(certCode).Step;
        if (scaleRows.Count == 0)
        {
            var (minimum, maximum, fallbackStep) = GetFallbackScale(certCode);
            if (totalQuestionsInSkill <= 0) return minimum;
            var fraction = Math.Clamp((double)rawScore / totalQuestionsInSkill, 0.0, 1.0);
            return minimum + (int)Math.Round(
                (fraction * (maximum - minimum)) / fallbackStep,
                MidpointRounding.AwayFromZero) * fallbackStep;
        }

        var lower = scaleRows.LastOrDefault(row => row.RawScore <= rawScore);
        var upper = scaleRows.FirstOrDefault(row => row.RawScore >= rawScore);
        var scaled = lower is null
            ? scaleRows[0].ScaledScore
            : upper is null
                ? scaleRows[^1].ScaledScore
                : lower.RawScore == upper.RawScore
                    ? lower.ScaledScore
                    : lower.ScaledScore
                        + (rawScore - lower.RawScore)
                        * (upper.ScaledScore - lower.ScaledScore)
                        / (upper.RawScore - lower.RawScore);

        return (int)Math.Round(scaled / step, MidpointRounding.AwayFromZero) * step;
    }

    private async Task<string?> FindBandCodeAsync(int certId, int totalScore, CancellationToken cancellationToken)
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
            throw new AttemptEngineException(AttemptEngineException.AttemptNotFinished, "Exam attempt has not been submitted.");
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
            gradingStates.Count(status => status == AiGradingStatus.Done.ToCode()),
            gradingStates.Count(status => status == AiGradingStatus.Failed.ToCode()));
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

    private async Task<SaveAttemptAnswerResult> SaveFreeResponseInternalAsync(
        ExamAttempt attempt,
        int attemptId,
        int questionId,
        int? selectedOptionId,
        string? responseText,
        bool isFlagged,
        AttemptAnswer? answer,
        CancellationToken cancellationToken)
    {
        if (selectedOptionId.HasValue)
        {
            throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Free-response questions do not accept multiple-choice options.");
        }

        var response = await db.FreeResponses
            .Include(r => r.Question)
            .SingleOrDefaultAsync(fr => fr.AttemptId == attemptId && fr.QuestionId == questionId, cancellationToken);

        if (response is null)
        {
            if (answer is null)
            {
                throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question {questionId} not found in attempt.");
            }

            response = new FreeResponse
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                ExamId = answer.ExamId,
                SkillId = answer.Question.SkillId,
                QuestionType = answer.Question.QuestionType,
                Status = FreeResponseStatus.Draft.ToCode(),
                SubmittedAt = null
            };
            db.FreeResponses.Add(response);
        }

        response.ResponseText = responseText ?? string.Empty;
        response.Status = FreeResponseStatus.Draft.ToCode();
        response.DraftSavedAt = DateTimeOffset.UtcNow;
        if (answer is not null)
        {
            answer.SelectedOptionId = null;
        }

        attempt.LastQuestionId = questionId;
        await db.SaveChangesAsync(cancellationToken);

        var remaining = Math.Max(0, (int)(attempt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return new SaveAttemptAnswerResult(questionId, null, isFlagged, remaining, attempt.IsAutoSubmitted);
    }
}
