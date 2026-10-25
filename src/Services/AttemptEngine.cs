using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
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
    public const string PreflightRequired = "preflight_required";

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

public sealed record WritingEvaluationResult(
    int WordCount,
    bool IsUnderLength,
    int? MinWords,
    bool HasRequiredKeywords,
    IReadOnlyList<string> MissingKeywords,
    string? Feedback);

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
        string? responseText = null,
        string? selectedOptionIdsJson = null);

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

    Task<bool> RequiresPreflightAsync(int examId, CancellationToken cancellationToken = default);

    Task<PreflightStatusView> GetPreflightStatusAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    Task<PreflightStatusView> VerifyPreflightAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    Task CancelAttemptAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaveAttemptAnswerResult>> SaveBatchAnswersAsync(
        int attemptId,
        int userId,
        IReadOnlyList<BatchAnswerItem> answers,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> TransitionToNextSkillAsync(
        int attemptId,
        int? userId = null,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> TransitionUnitAsync(
        int attemptId,
        int targetUnitNumber,
        int? userId = null,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> TransitionGroupAsync(
        int attemptId,
        int targetGroupId,
        int? userId = null,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> CompleteAudioAndStartReviewAsync(
        int attemptId,
        int? userId = null,
        CancellationToken cancellationToken = default);

    Task<SittingSyncResult> SaveSittingSyncAsync(
        int attemptId,
        SittingSyncRequest request,
        int? userId = null,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default);

    Task<AttemptResumeView> ResumeAttemptByCodeAsync(
        string authorizationCode,
        int userId,
        CancellationToken cancellationToken = default);

    WritingEvaluationResult EvaluateWriting(Question question, string? responseText);
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
            var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(examId, cancellationToken);
            var requiresPreflight = requiresAudio || requiresMic;

            var questions = await db.Questions
                .Where(q => q.ExamId == examId)
                .Include(q => q.Group)
                .Include(q => q.Section).ThenInclude(s => s.Skill)
                .OrderBy(q => q.Section.Skill.DisplayOrder)
                .ThenBy(q => q.Section.DisplayOrder)
                .ThenBy(q => q.Group.DisplayOrder)
                .ThenBy(q => q.DisplayOrder)
                .ToListAsync(cancellationToken);

            var firstSkill = questions
                .Select(q => q.Section.Skill)
                .OrderBy(s => s.DisplayOrder)
                .FirstOrDefault();

            var firstSection = questions.FirstOrDefault()?.Section;
            var firstGroup = questions.FirstOrDefault()?.Group;

            int? activeSkillId = firstSkill?.SkillId;
            int activeUnitNumber = 1;
            int? activeGroupId = null;
            if (firstSection != null && string.Equals(firstSection.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase))
            {
                activeGroupId = firstGroup?.GroupId;
            }

            DateTime? phaseDeadlineAt = null;
            if (firstSection != null && string.Equals(firstSection.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase) && firstGroup?.ResponseSeconds.HasValue == true)
            {
                phaseDeadlineAt = now.UtcDateTime.AddSeconds(firstGroup.ResponseSeconds.Value);
            }
            else if (firstSkill?.DurationMinutes is int skillDur && skillDur > 0)
            {
                phaseDeadlineAt = now.UtcDateTime.AddMinutes(skillDur);
            }
            else
            {
                phaseDeadlineAt = expiresAt.UtcDateTime;
            }

            string authorizationCode;
            do
            {
                authorizationCode = GenerateAuthorizationCode();
            } while (await db.ExamAttempts.AnyAsync(a => a.AuthorizationCode == authorizationCode, cancellationToken));

            var attempt = new ExamAttempt
            {
                UserId = userId,
                ExamId = examId,
                CertId = certId,
                AuthorizationCode = authorizationCode,
                Status = AttemptStatus.InProgress.ToCode(),
                StartedAt = now,
                ExpiresAt = expiresAt,
                ActiveSkillId = activeSkillId,
                ActiveUnitNumber = activeUnitNumber,
                ActiveGroupId = activeGroupId,
                PhaseDeadlineAt = phaseDeadlineAt,
                IsAutoSubmitted = false,
                PreflightVerifiedAt = requiresPreflight ? null : now
            };

            db.ExamAttempts.Add(attempt);
            await db.SaveChangesAsync(cancellationToken);

            if (questions.Count > 0)
            {
                attempt.LastQuestionId = questions[0].QuestionId;
                foreach (var q in questions)
                {
                    if (q.QuestionType != QuestionType.FreeResponse.ToCode())
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
                    else
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

        if (attempt.Status == AttemptStatus.InProgress.ToCode() && attempt.PreflightVerifiedAt == null)
        {
            await EnsurePreflightVerifiedAsync(attempt, cancellationToken);
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

        if (attempt.Status == AttemptStatus.InProgress.ToCode() && string.IsNullOrWhiteSpace(attempt.AuthorizationCode))
        {
            attempt.AuthorizationCode = GenerateAuthorizationCode();
            await db.SaveChangesAsync(cancellationToken);
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
            q.QuestionType == QuestionType.FreeResponse.ToCode()
                ? freeResponses.TryGetValue(q.QuestionId, out var response) && (!string.IsNullOrWhiteSpace(response.ResponseText) || !string.IsNullOrWhiteSpace(response.AudioPath))
                : answersByQuestion.TryGetValue(q.QuestionId, out var answer) && (answer.SelectedOptionId.HasValue || !string.IsNullOrWhiteSpace(answer.ResponseText) || !string.IsNullOrWhiteSpace(answer.SelectedOptionIdsJson)));
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
                response?.ResponseText ?? ans?.ResponseText,
                response?.AudioPath,
                response?.DurationMs,
                response?.DraftSavedAt,
                q.Section.Skill.DurationMinutes,
                q.Section.Skill.DisplayOrder,
                ans?.SelectedOptionIdsJson != null ? ParseOptionIds(ans.SelectedOptionIdsJson).ToList() : null,
                q.MaxSelections,
                q.AllowNumber,
                q.AcceptedAnswersJson,
                q.RequiredKeywords,
                q.Section.LayoutType,
                q.Section.RequirePassage,
                q.Section.NavigationMode,
                q.Section.ToolsConfigJson,
                q.Section.HideOptionText,
                q.Section.OptionCount > 0 ? q.Section.OptionCount : 4,
                q.Section.ClusterSize > 0 ? q.Section.ClusterSize : 1,
                q.Section.RequireImage,
                q.Group.UnitNumber,
                q.GroupId));
        }

        var currentQuestionInfo = totalQuestions > 0 ? allQuestions[targetIndex] : new AttemptQuestionInfo(
            0, 0, 0, 0, "", "", "", "", Modality.Reading, "", null, null, null, null, null, null, null, [], null, false);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);
        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);
        var isPhaseExpired = phaseDeadline.HasValue && DateTimeOffset.UtcNow >= phaseDeadline.Value;

        var navigator = questions.Select((q, idx) =>
        {
            answersByQuestion.TryGetValue(q.QuestionId, out var answer);
            freeResponses.TryGetValue(q.QuestionId, out var response);
            var isLocked = isPhaseExpired
                || (attempt.ActiveSkillId.HasValue && q.Section.SkillId != attempt.ActiveSkillId.Value)
                || (q.Group.UnitNumber != attempt.ActiveUnitNumber)
                || (string.Equals(q.Section.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase)
                    && attempt.ActiveGroupId.HasValue
                    && q.GroupId != attempt.ActiveGroupId.Value);

            return new NavigatorItem(
                q.QuestionId,
                idx + 1,
                q.SectionId,
                q.Section.Code,
                EnumCodes.Parse<Modality>(q.Section.Modality),
                q.QuestionType == QuestionType.FreeResponse.ToCode()
                    ? (!string.IsNullOrWhiteSpace(response?.ResponseText) || !string.IsNullOrWhiteSpace(response?.AudioPath))
                    : (answer?.SelectedOptionId != null || !string.IsNullOrWhiteSpace(answer?.ResponseText) || !string.IsNullOrWhiteSpace(answer?.SelectedOptionIdsJson)),
                answer?.IsFlagged ?? false,
                idx == targetIndex,
                IsLocked: isLocked);
        }).ToList();

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
            phases,
            ActiveSkillId: attempt.ActiveSkillId,
            ActiveUnitNumber: attempt.ActiveUnitNumber,
            ActiveGroupId: attempt.ActiveGroupId,
            PhaseDeadlineAt: phaseDeadline,
            IsSkillLocked: isPhaseExpired,
            AuthorizationCode: attempt.AuthorizationCode,
            LastAudioOffsetMs: attempt.LastAudioOffsetMs,
            CandidateNotesJson: attempt.CandidateNotesJson);
    }

    public async Task<SaveAttemptAnswerResult> SaveAnswerAsync(
        int attemptId,
        int userId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default,
        string? responseText = null,
        string? selectedOptionIdsJson = null)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        if (attempt.PreflightVerifiedAt == null)
        {
            await EnsurePreflightVerifiedAsync(attempt, cancellationToken);
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

        await ValidateQuestionSittingStateAsync(attempt, questionId, cancellationToken);

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
            if (!string.IsNullOrWhiteSpace(responseText) && (answer.Question.QuestionType == QuestionType.Mcq.ToCode() || answer.Question.QuestionType == QuestionType.Mcq_3.ToCode()))
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
            answer.SelectedOptionIdsJson = selectedOptionIdsJson;
            answer.ResponseText = responseText;

            var options = await db.QuestionOptions
                .Where(o => o.QuestionId == questionId)
                .ToListAsync(cancellationToken);
            answer.IsCorrect = EvaluateObjectiveAnswer(
                answer.Question,
                options,
                selectedOptionId,
                selectedOptionIdsJson,
                responseText);
        }

        answer.IsFlagged = isFlagged;
        attempt.LastQuestionId = questionId;

        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);
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

        if (attempt.PreflightVerifiedAt == null)
        {
            await EnsurePreflightVerifiedAsync(attempt, cancellationToken);
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        await ValidateQuestionSittingStateAsync(attempt, questionId, cancellationToken);

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

    private async Task ValidateQuestionSittingStateAsync(
        ExamAttempt attempt,
        int questionId,
        CancellationToken cancellationToken)
    {
        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);
        if (phaseDeadline.HasValue && DateTimeOffset.UtcNow >= phaseDeadline.Value)
        {
            throw new AttemptEngineException(
                AttemptEngineException.AttemptExpired,
                "Exam phase time has expired.");
        }

        var question = await db.Questions
            .Where(q => q.QuestionId == questionId && q.ExamId == attempt.ExamId)
            .Select(q => new
            {
                q.SkillId,
                q.GroupId,
                q.SectionId,
                UnitNumber = q.Group.UnitNumber,
                NavigationMode = q.Section.NavigationMode
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question {questionId} not found in attempt.");

        if (attempt.ActiveSkillId.HasValue && question.SkillId != attempt.ActiveSkillId.Value)
        {
            throw new AttemptEngineException(
                AttemptEngineException.WrongPhase,
                "This question is not in the active exam skill.");
        }

        if (question.UnitNumber != attempt.ActiveUnitNumber)
        {
            throw new AttemptEngineException(
                AttemptEngineException.WrongPhase,
                "This question belongs to a locked unit.");
        }

        if (string.Equals(question.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            if (attempt.ActiveGroupId.HasValue && question.GroupId != attempt.ActiveGroupId.Value)
            {
                throw new AttemptEngineException(
                    AttemptEngineException.WrongPhase,
                    "This question belongs to a locked question group.");
            }
        }
    }

    private Task EnsureQuestionInActivePhaseAsync(
        ExamAttempt attempt,
        int questionId,
        CancellationToken cancellationToken) =>
        ValidateQuestionSittingStateAsync(attempt, questionId, cancellationToken);

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
            var questionIds = answers.Select(answer => answer.QuestionId).Distinct().ToList();
            var optionsByQuestion = (await db.QuestionOptions
                .Where(option => questionIds.Contains(option.QuestionId))
                .ToListAsync(cancellationToken))
                .ToLookup(option => option.QuestionId);

            // Score each objective answer
            foreach (var ans in answers.Where(a => a.Question.QuestionType != QuestionType.FreeResponse.ToCode()))
            {
                var opts = optionsByQuestion[ans.QuestionId];
                ans.IsCorrect = EvaluateObjectiveAnswer(
                    ans.Question,
                    opts,
                    ans.SelectedOptionId,
                    ans.SelectedOptionIdsJson,
                    ans.ResponseText);
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
                .Where(a => a.Question.QuestionType != QuestionType.FreeResponse.ToCode())
                .GroupBy(a => a.Question.Section.Skill)
                .ToList();
            var newSkillScores = new List<AttemptSkillScore>();

            foreach (var group in skillGroups)
            {
                var skill = group.Key;
                var sectionGroups = group.GroupBy(a => a.Question.Section).ToList();
                int rawScore;
                int scaledScore;

                bool hasVaryingWeights = sectionGroups.Any(sg => sg.Key.ScoreWeight != sectionGroups[0].Key.ScoreWeight);
                if (hasVaryingWeights)
                {
                    decimal totalWeight = sectionGroups.Sum(sg => sg.Key.ScoreWeight > 0 ? sg.Key.ScoreWeight : 1.0m);
                    decimal weightedSectionRaw = sectionGroups.Sum(sg =>
                        (decimal)sg.Count(a => a.IsCorrect == true) * (sg.Key.ScoreWeight > 0 ? sg.Key.ScoreWeight : 1.0m)) / totalWeight;

                    rawScore = (int)Math.Round(weightedSectionRaw, MidpointRounding.AwayFromZero);
                    scaledScore = await ComputeScaledScoreAsync(
                        attempt.CertId, skill.SkillId, attempt.ExamId, weightedSectionRaw, group.Count(), cancellationToken);
                }
                else
                {
                    rawScore = group.Count(a => a.IsCorrect == true);
                    scaledScore = await ComputeScaledScoreAsync(
                        attempt.CertId, skill.SkillId, attempt.ExamId, rawScore, group.Count(), cancellationToken);
                }

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

            var cert = await db.Certificates
                .Where(c => c.CertId == attempt.CertId)
                .Select(c => new { c.Code, c.ScoreAggregationRule })
                .SingleAsync(cancellationToken);
            var totalScore = AggregateTotalScore(cert.ScoreAggregationRule, newSkillScores);
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

    public WritingEvaluationResult EvaluateWriting(Question question, string? responseText) =>
        EvaluateWritingResponse(question, responseText);

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
                .Include(fr => fr.AiGradingFreeResponse).ThenInclude(g => g!.AiGradingScores)
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

            foreach (var response in freeResponses)
            {
                var eval = EvaluateWritingResponse(response.Question, response.ResponseText);
                if (eval.IsUnderLength && response.AiGradingFreeResponse != null)
                {
                    foreach (var s in response.AiGradingFreeResponse.AiGradingScores)
                    {
                        if (string.IsNullOrWhiteSpace(s.Comment))
                        {
                            s.Comment = "IsUnderLength=true; Length penalty applied.";
                        }
                        else if (!s.Comment.Contains("IsUnderLength"))
                        {
                            s.Comment = $"{s.Comment} [IsUnderLength=true]";
                        }
                    }

                    var hasExistingRec = await db.Recommendations
                        .AnyAsync(r => r.GradingId == response.AiGradingFreeResponse.GradingId && r.Text.Contains("IsUnderLength"), cancellationToken);
                    if (!hasExistingRec)
                    {
                        db.Recommendations.Add(new Recommendation
                        {
                            UserId = attempt.UserId,
                            CertId = attempt.CertId,
                            AttemptId = attempt.AttemptId,
                            GradingId = response.AiGradingFreeResponse.GradingId,
                            SkillId = response.SkillId,
                            Severity = "minor",
                            Text = $"IsUnderLength=true: Bài viết có {eval.WordCount} từ, dưới mức tối thiểu {response.Question.MinWords} từ.",
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }
                }
            }

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

            foreach (var answer in answers.Where(a => a.Question.QuestionType != QuestionType.FreeResponse.ToCode()))
            {
                var opts = answer.Question.QuestionOptionQuestionNavigations;
                answer.IsCorrect = EvaluateObjectiveAnswer(
                    answer.Question,
                    opts,
                    answer.SelectedOptionId,
                    answer.SelectedOptionIdsJson,
                    answer.ResponseText);
            }

            var skills = answers.Select(a => a.Question.Section.Skill)
                .Concat(freeResponses.Select(fr => fr.Question.Section.Skill))
                .DistinctBy(s => s.SkillId)
                .ToList();

            var newSkillScores = new List<AttemptSkillScore>();
            foreach (var skill in skills)
            {
                var objectiveAnswers = answers.Where(a => a.Question.Section.SkillId == skill.SkillId && a.Question.QuestionType != QuestionType.FreeResponse.ToCode()).ToList();
                var freeResponsesInSkill = freeResponses.Where(fr => fr.SkillId == skill.SkillId).ToList();
                var totalQuestionsInSkill = objectiveAnswers.Count + freeResponsesInSkill.Count;
                if (totalQuestionsInSkill == 0) continue;

                var hasScales = await db.ScoreScales.AnyAsync(s => s.CertId == attempt.CertId && s.SkillId == skill.SkillId, cancellationToken);
                if (objectiveAnswers.Count == 0 && !hasScales && freeResponsesInSkill.Count == 0) continue;

                int scaledScore;
                decimal rawScore;

                if (freeResponsesInSkill.Count > 0 && objectiveAnswers.Count == 0)
                {
                    // Pure free response skill (e.g. IELTS Writing Task 1 + Task 2)
                    var frSections = freeResponsesInSkill.Select(fr => fr.Question.Section).DistinctBy(s => s.SectionId).ToList();
                    decimal totalWeight = frSections.Sum(s => s.ScoreWeight > 0 ? s.ScoreWeight : 1.0m);
                    decimal weightedSectionSum = 0m;

                    foreach (var fr in freeResponsesInSkill)
                    {
                        var secWeight = fr.Question.Section.ScoreWeight > 0 ? fr.Question.Section.ScoreWeight : 1.0m;
                        var frScore = fr.AiGradingFreeResponse?.OverallScaled ?? 0m;
                        weightedSectionSum += frScore * secWeight;
                    }

                    decimal weightedScore = totalWeight > 0 ? weightedSectionSum / totalWeight : 0m;
                    rawScore = weightedScore;

                    var certRule = await db.Certificates
                        .Where(c => c.CertId == attempt.CertId)
                        .Select(c => c.ScoreAggregationRule)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (string.Equals(certRule, "avg_half_step", StringComparison.OrdinalIgnoreCase))
                    {
                        scaledScore = RoundHalfStep((double)weightedScore);
                    }
                    else
                    {
                        scaledScore = (int)Math.Round(weightedScore, MidpointRounding.AwayFromZero);
                    }
                }
                else
                {
                    rawScore = objectiveAnswers.Count(a => a.IsCorrect == true) + responsePointsBySkill.GetValueOrDefault(skill.SkillId);
                    scaledScore = await ComputeScaledScoreAsync(
                        attempt.CertId,
                        skill.SkillId,
                        attempt.ExamId,
                        rawScore,
                        totalQuestionsInSkill,
                        cancellationToken);
                }

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

            var cert = await db.Certificates
                .Where(c => c.CertId == attempt.CertId)
                .Select(c => new { c.Code, c.ScoreAggregationRule })
                .SingleAsync(cancellationToken);
            var totalScore = AggregateTotalScore(cert.ScoreAggregationRule, newSkillScores);
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

    public static int RoundHalfStep(double average)
    {
        // IELTS official rounding:
        // if decimal is < 0.25 -> .0; >= 0.25 and < 0.75 -> .5; >= 0.75 -> next whole band
        if (average > 10.0)
        {
            double band = average / 10.0;
            double floor = Math.Floor(band);
            double rem = band - floor;
            double roundedBand = rem < 0.25 ? floor : rem < 0.75 ? floor + 0.5 : floor + 1.0;
            return (int)Math.Round(roundedBand * 10.0);
        }
        else
        {
            double floor = Math.Floor(average);
            double rem = average - floor;
            double roundedBand = rem < 0.25 ? floor : rem < 0.75 ? floor + 0.5 : floor + 1.0;
            return (int)Math.Round(roundedBand * 10.0);
        }
    }

    public static int AggregateTotalScore(
        string? scoreAggregationRule,
        IReadOnlyCollection<AttemptSkillScore> skillScores)
    {
        if (skillScores.Count == 0) return 0;

        var average = skillScores.Average(score => (double)score.ScaledScore);
        return scoreAggregationRule?.ToLowerInvariant() switch
        {
            "avg_half_step" => RoundHalfStep(average),
            "avg_whole" => (int)Math.Round(average, MidpointRounding.AwayFromZero),
            "independent" => 0,
            _ => skillScores.Sum(score => score.ScaledScore)
        };
    }

    public static bool EvaluateObjectiveAnswer(
        Question question,
        IEnumerable<QuestionOption> options,
        int? selectedOptionId,
        string? selectedOptionIdsJson,
        string? responseText)
    {
        var qType = question.QuestionType?.ToLowerInvariant() ?? QuestionType.Mcq.ToCode();

        switch (qType)
        {
            case "mcq":
            case "mcq_3":
            {
                if (!selectedOptionId.HasValue) return false;
                return options.Any(o => o.OptionId == selectedOptionId.Value && o.IsCorrect);
            }

            case "multi_select":
            {
                var selectedIds = ParseOptionIds(selectedOptionIdsJson, selectedOptionId);
                var correctIds = options.Where(o => o.IsCorrect).Select(o => o.OptionId).ToHashSet();
                if (correctIds.Count == 0) return false;
                return selectedIds.SetEquals(correctIds);
            }

            case "inline_completion":
            {
                return EvaluateTextCompletion(question, options, responseText);
            }

            case "dropdown":
            case "drag_drop":
            {
                if (selectedOptionId.HasValue && options.Any(o => o.OptionId == selectedOptionId.Value && o.IsCorrect))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    return EvaluateTextCompletion(question, options, responseText);
                }

                return false;
            }

            default:
                return false;
        }
    }

    public static HashSet<int> ParseOptionIds(string? selectedOptionIdsJson, int? fallbackOptionId = null)
    {
        var set = new HashSet<int>();
        if (!string.IsNullOrWhiteSpace(selectedOptionIdsJson))
        {
            var trimmed = selectedOptionIdsJson.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                try
                {
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<int>>(trimmed);
                    if (list != null)
                    {
                        foreach (var id in list) set.Add(id);
                        return set;
                    }
                }
                catch
                {
                    // Fallback to split if invalid JSON
                }
            }

            var parts = trimmed.Trim('[', ']').Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (int.TryParse(p.Trim(), out var parsedId))
                {
                    set.Add(parsedId);
                }
            }
        }

        if (set.Count == 0 && fallbackOptionId.HasValue)
        {
            set.Add(fallbackOptionId.Value);
        }

        return set;
    }

    public static WritingEvaluationResult EvaluateWritingResponse(Question question, string? responseText)
    {
        var text = responseText ?? string.Empty;
        var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int wordCount = words.Length;

        bool isUnderLength = question.MinWords.HasValue && question.MinWords.Value > 0 && wordCount < question.MinWords.Value;

        var missingKeywords = new List<string>();
        bool hasRequiredKeywords = true;

        if (!string.IsNullOrWhiteSpace(question.RequiredKeywords))
        {
            var keywords = ParseKeywords(question.RequiredKeywords);
            foreach (var kw in keywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                if (!ContainsKeywordStem(text, kw))
                {
                    missingKeywords.Add(kw.Trim());
                }
            }
            hasRequiredKeywords = missingKeywords.Count == 0;
        }

        var feedbackParts = new List<string>();
        if (isUnderLength)
        {
            feedbackParts.Add($"IsUnderLength=true; WordCount={wordCount}; MinWords={question.MinWords}; Length penalty applied.");
        }
        if (!hasRequiredKeywords)
        {
            feedbackParts.Add($"MissingRequiredKeywords=[{string.Join(", ", missingKeywords)}]");
        }

        string? feedback = feedbackParts.Count > 0 ? string.Join(" | ", feedbackParts) : null;

        return new WritingEvaluationResult(
            wordCount,
            isUnderLength,
            question.MinWords,
            hasRequiredKeywords,
            missingKeywords,
            feedback);
    }

    public static bool EvaluateTextCompletion(
        Question question,
        IEnumerable<QuestionOption> options,
        string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return false;

        var normalizedCandidate = System.Text.RegularExpressions.Regex.Replace(responseText.Trim(), @"\s+", " ");

        if (!question.AllowNumber && System.Text.RegularExpressions.Regex.IsMatch(normalizedCandidate, @"\d"))
        {
            return false;
        }

        var words = normalizedCandidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (question.MaxWords.HasValue && words.Length > question.MaxWords.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(question.RequiredKeywords))
        {
            var keywords = ParseKeywords(question.RequiredKeywords);
            foreach (var kw in keywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                if (!ContainsKeywordStem(normalizedCandidate, kw.Trim()))
                {
                    return false;
                }
            }
        }

        var accepted = ParseAcceptedAnswers(question.AcceptedAnswersJson);
        foreach (var opt in options.Where(o => o.IsCorrect))
        {
            if (!string.IsNullOrWhiteSpace(opt.Text))
            {
                accepted.Add(opt.Text.Trim());
            }
        }

        foreach (var acc in accepted)
        {
            var normalizedAcc = System.Text.RegularExpressions.Regex.Replace(acc.Trim(), @"\s+", " ");
            if (string.Equals(normalizedCandidate, normalizedAcc, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static List<string> ParseKeywords(string requiredKeywords)
    {
        var list = new List<string>();
        var trimmed = requiredKeywords.Trim();
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed != null) return parsed.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
            catch { }
        }
        return trimmed.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                      .Select(k => k.Trim())
                      .Where(k => !string.IsNullOrWhiteSpace(k))
                      .ToList();
    }

    public static bool ContainsKeywordStem(string text, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim().ToLowerInvariant();
        var lowerText = text.ToLowerInvariant();

        if (lowerText.Contains(keyword)) return true;

        var stem = StripSuffixes(keyword);
        if (stem.Length >= 3 && lowerText.Contains(stem)) return true;

        var candidateWords = lowerText.Split([' ', ',', '.', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in candidateWords)
        {
            if (w.Contains(stem) || StripSuffixes(w) == stem) return true;
        }

        return false;
    }

    private static string StripSuffixes(string word)
    {
        if (word.EndsWith("ations") && word.Length > 7) return word[..^6];
        if (word.EndsWith("ation") && word.Length > 6) return word[..^5];
        if (word.EndsWith("tion") && word.Length > 6) return word[..^4];
        if (word.EndsWith("ment") && word.Length > 6) return word[..^4];
        if (word.EndsWith("ing") && word.Length > 5) return word[..^3];
        if (word.EndsWith("ed") && word.Length > 4) return word[..^2];
        if (word.EndsWith("es") && word.Length > 4) return word[..^2];
        if (word.EndsWith("s") && word.Length > 3) return word[..^1];
        if (word.EndsWith("e") && word.Length > 4) return word[..^1];
        return word;
    }

    public static List<string> ParseAcceptedAnswers(string? acceptedAnswersJson)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(acceptedAnswersJson)) return list;
        var trimmed = acceptedAnswersJson.Trim();
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed != null) return parsed.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
            catch { }
        }
        return trimmed.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
                      .Select(a => a.Trim())
                      .Where(a => !string.IsNullOrWhiteSpace(a))
                      .ToList();
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

    public async Task<bool> RequiresPreflightAsync(int examId, CancellationToken cancellationToken = default)
    {
        var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(examId, cancellationToken);
        return requiresAudio || requiresMic;
    }

    public async Task<PreflightStatusView> GetPreflightStatusAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam).ThenInclude(e => e.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(attempt.ExamId, cancellationToken);
        var audioUrls = await GetExamAudioUrlsAsync(attempt.ExamId, cancellationToken);
        var requiresPreflight = requiresAudio || requiresMic;

        return new PreflightStatusView(
            AttemptId: attempt.AttemptId,
            ExamId: attempt.ExamId,
            ExamName: attempt.Exam.Name,
            CertCode: attempt.Exam.Cert.Code,
            CertName: attempt.Exam.Cert.Name,
            DurationMinutes: attempt.Exam.DurationMinutes,
            RequiresPreflight: requiresPreflight,
            RequiresAudio: requiresAudio,
            RequiresMicrophone: requiresMic,
            IsVerified: !requiresPreflight || attempt.PreflightVerifiedAt.HasValue,
            VerifiedAt: attempt.PreflightVerifiedAt,
            AudioUrls: audioUrls);
    }

    public async Task<PreflightStatusView> VerifyPreflightAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam).ThenInclude(e => e.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        var now = DateTimeOffset.UtcNow;
        if (attempt.PreflightVerifiedAt == null)
        {
            attempt.PreflightVerifiedAt = now;
            // The candidate timer starts upon passing the preflight onboarding gate!
            attempt.StartedAt = now;
            attempt.ExpiresAt = now.AddMinutes(attempt.Exam.DurationMinutes);

            var firstSkill = await db.Questions
                .Where(q => q.ExamId == attempt.ExamId && q.Section.SkillId == attempt.ActiveSkillId)
                .Select(q => q.Section.Skill)
                .FirstOrDefaultAsync(cancellationToken);
            var firstSection = await db.Questions
                .Where(q => q.ExamId == attempt.ExamId && q.Section.SkillId == attempt.ActiveSkillId)
                .Select(q => q.Section)
                .FirstOrDefaultAsync(cancellationToken);
            var firstGroup = attempt.ActiveGroupId.HasValue
                ? await db.QuestionGroups.SingleOrDefaultAsync(g => g.GroupId == attempt.ActiveGroupId.Value, cancellationToken)
                : null;

            if (firstSection != null && string.Equals(firstSection.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase) && firstGroup?.ResponseSeconds.HasValue == true)
            {
                attempt.PhaseDeadlineAt = now.UtcDateTime.AddSeconds(firstGroup.ResponseSeconds.Value);
            }
            else if (firstSkill?.DurationMinutes is int skillDur && skillDur > 0)
            {
                attempt.PhaseDeadlineAt = now.UtcDateTime.AddMinutes(skillDur);
            }
            else
            {
                attempt.PhaseDeadlineAt = attempt.ExpiresAt.UtcDateTime;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetPreflightStatusAsync(attemptId, userId, cancellationToken);
    }

    private async Task EnsurePreflightVerifiedAsync(ExamAttempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.PreflightVerifiedAt != null)
        {
            return;
        }

        var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(attempt.ExamId, cancellationToken);
        if (requiresAudio || requiresMic)
        {
            throw new AttemptEngineException(
                AttemptEngineException.PreflightRequired,
                "Preflight equipment and audio buffering verification must be completed before entering the exam sitting.");
        }
    }

    private async Task<(bool RequiresAudio, bool RequiresMicrophone)> InspectPreflightRequirementsAsync(int examId, CancellationToken cancellationToken)
    {
        var questionSectionIds = await db.Questions
            .AsNoTracking()
            .Where(q => q.ExamId == examId)
            .Select(q => q.SectionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        List<Section> sections;
        if (questionSectionIds.Count > 0)
        {
            sections = await db.Sections
                .AsNoTracking()
                .Where(s => questionSectionIds.Contains(s.SectionId))
                .Include(s => s.Skill)
                .ToListAsync(cancellationToken);
        }
        else
        {
            var certId = await db.Exams
                .AsNoTracking()
                .Where(e => e.ExamId == examId)
                .Select(e => e.CertId)
                .SingleOrDefaultAsync(cancellationToken);

            sections = await db.Sections
                .AsNoTracking()
                .Where(s => s.CertId == certId)
                .Include(s => s.Skill)
                .ToListAsync(cancellationToken);
        }

        var hasSectionAudioFlag = sections.Any(s => s.RequireAudio);
        var hasUploadedAudio = await db.ExamSectionAudios.AnyAsync(a => a.ExamId == examId, cancellationToken)
            || await db.QuestionGroups.AnyAsync(g => g.ExamId == examId && !string.IsNullOrWhiteSpace(g.AudioPath), cancellationToken);
        var requiresAudio = hasSectionAudioFlag || hasUploadedAudio;

        var requiresMicrophone = sections.Any(s =>
            string.Equals(s.Modality, "speaking", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Skill?.Modality, "speaking", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Skill?.Code, "speaking", StringComparison.OrdinalIgnoreCase));

        return (requiresAudio, requiresMicrophone);
    }

    private async Task<IReadOnlyList<string>> GetExamAudioUrlsAsync(int examId, CancellationToken cancellationToken)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sectionAudios = await db.ExamSectionAudios
            .AsNoTracking()
            .Where(a => a.ExamId == examId && !string.IsNullOrWhiteSpace(a.AudioPath))
            .Select(a => a.AudioPath)
            .ToListAsync(cancellationToken);

        foreach (var path in sectionAudios)
        {
            var normalized = MediaUrlHelper.NormalizeMediaUrl(path);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                urls.Add(normalized);
            }
        }

        var groupAudios = await db.QuestionGroups
            .AsNoTracking()
            .Where(g => g.ExamId == examId && !string.IsNullOrWhiteSpace(g.AudioPath))
            .Select(g => g.AudioPath!)
            .ToListAsync(cancellationToken);

        foreach (var path in groupAudios)
        {
            var normalized = MediaUrlHelper.NormalizeMediaUrl(path);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                urls.Add(normalized);
            }
        }

        return urls.OrderBy(u => u).ToList();
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

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remaining = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);
        return new SaveAttemptAnswerResult(questionId, null, isFlagged, remaining, attempt.IsAutoSubmitted);
    }

    public async Task<IReadOnlyList<SaveAttemptAnswerResult>> SaveBatchAnswersAsync(
        int attemptId,
        int userId,
        IReadOnlyList<BatchAnswerItem> answers,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        if (attempt.PreflightVerifiedAt == null)
        {
            await EnsurePreflightVerifiedAsync(attempt, cancellationToken);
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            if (onAutoSubmitted != null)
            {
                await onAutoSubmitted(attempt.AttemptId, userId);
            }
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);
        if (phaseDeadline.HasValue && DateTimeOffset.UtcNow >= phaseDeadline.Value)
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam phase time has expired.");
        }

        var results = new List<SaveAttemptAnswerResult>(answers.Count);
        foreach (var item in answers)
        {
            var res = await SaveAnswerAsync(
                attemptId,
                userId,
                item.QuestionId,
                item.SelectedOptionId,
                item.IsFlagged,
                onAutoSubmitted,
                cancellationToken,
                item.ResponseText,
                item.SelectedOptionIdsJson);
            results.Add(res);
        }

        return results;
    }

    public async Task<AttemptTransitionResult> TransitionToNextSkillAsync(
        int attemptId,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && (userId == null || a.UserId == userId), cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        var examSkills = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId)
            .Select(q => q.Section.Skill)
            .Distinct()
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        int currentIndex = -1;
        if (attempt.ActiveSkillId.HasValue)
        {
            currentIndex = examSkills.FindIndex(s => s.SkillId == attempt.ActiveSkillId.Value);
        }

        int nextIndex = currentIndex + 1;
        if (nextIndex >= examSkills.Count)
        {
            var grading = await SubmitAttemptAsync(attempt.AttemptId, attempt.UserId, isAutoSubmit: false, cancellationToken);
            return new AttemptTransitionResult(
                attempt.AttemptId,
                attempt.ActiveSkillId,
                attempt.ActiveUnitNumber,
                attempt.ActiveGroupId,
                ToUtcOffset(attempt.PhaseDeadlineAt),
                0,
                EnumCodes.Parse<AttemptStatus>(grading.Attempt.Status));
        }

        var nextSkill = examSkills[nextIndex];
        attempt.ActiveSkillId = nextSkill.SkillId;
        attempt.ActiveUnitNumber = 1;

        var firstSectionAndGroup = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId && q.Section.SkillId == nextSkill.SkillId)
            .OrderBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .Select(q => new
            {
                q.Section,
                q.Group,
                q.QuestionId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (firstSectionAndGroup != null && string.Equals(firstSectionAndGroup.Section.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            attempt.ActiveGroupId = firstSectionAndGroup.Group.GroupId;
        }
        else
        {
            attempt.ActiveGroupId = null;
        }

        var now = DateTimeOffset.UtcNow;
        if (firstSectionAndGroup != null && string.Equals(firstSectionAndGroup.Section.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase) && firstSectionAndGroup.Group.ResponseSeconds.HasValue)
        {
            attempt.PhaseDeadlineAt = now.UtcDateTime.AddSeconds(firstSectionAndGroup.Group.ResponseSeconds.Value);
        }
        else if (nextSkill.DurationMinutes is int dur && dur > 0)
        {
            attempt.PhaseDeadlineAt = now.UtcDateTime.AddMinutes(dur);
        }
        else
        {
            attempt.PhaseDeadlineAt = attempt.ExpiresAt.UtcDateTime;
        }

        if (firstSectionAndGroup != null)
        {
            attempt.LastQuestionId = firstSectionAndGroup.QuestionId;
        }

        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - now).TotalSeconds);

        return new AttemptTransitionResult(
            attempt.AttemptId,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            ToUtcOffset(attempt.PhaseDeadlineAt),
            remainingSeconds,
            EnumCodes.Parse<AttemptStatus>(attempt.Status));
    }

    public async Task<AttemptTransitionResult> TransitionUnitAsync(
        int attemptId,
        int targetUnitNumber,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && (userId == null || a.UserId == userId), cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        if (targetUnitNumber <= attempt.ActiveUnitNumber)
        {
            throw new AttemptEngineException(AttemptEngineException.WrongPhase, "Cannot navigate back to a locked unit.");
        }

        attempt.ActiveUnitNumber = targetUnitNumber;

        var firstUnitQuestionId = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId && q.Section.SkillId == attempt.ActiveSkillId && q.Group.UnitNumber == targetUnitNumber)
            .OrderBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .Select(q => (int?)q.QuestionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstUnitQuestionId.HasValue)
        {
            attempt.LastQuestionId = firstUnitQuestionId.Value;
        }

        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);

        return new AttemptTransitionResult(
            attempt.AttemptId,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            ToUtcOffset(attempt.PhaseDeadlineAt),
            remainingSeconds,
            EnumCodes.Parse<AttemptStatus>(attempt.Status));
    }

    public async Task<AttemptTransitionResult> TransitionGroupAsync(
        int attemptId,
        int targetGroupId,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && (userId == null || a.UserId == userId), cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        var targetGroup = await db.QuestionGroups
            .Include(g => g.Section)
            .SingleOrDefaultAsync(g => g.GroupId == targetGroupId && g.ExamId == attempt.ExamId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question group {targetGroupId} not found.");

        if (attempt.ActiveSkillId.HasValue && targetGroup.Section.SkillId != attempt.ActiveSkillId.Value)
        {
            throw new AttemptEngineException(AttemptEngineException.WrongPhase, "Target group belongs to a different skill.");
        }

        attempt.ActiveGroupId = targetGroupId;

        int subTimerSeconds = targetGroup.ResponseSeconds
            ?? await db.Questions.Where(q => q.GroupId == targetGroupId && q.ResponseSeconds.HasValue).Select(q => q.ResponseSeconds!.Value).FirstOrDefaultAsync(cancellationToken);
        if (subTimerSeconds <= 0)
        {
            subTimerSeconds = 600;
        }

        attempt.PhaseDeadlineAt = DateTimeOffset.UtcNow.UtcDateTime.AddSeconds(subTimerSeconds);

        var firstGroupQuestionId = await db.Questions
            .Where(q => q.GroupId == targetGroupId)
            .OrderBy(q => q.DisplayOrder)
            .Select(q => (int?)q.QuestionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstGroupQuestionId.HasValue)
        {
            attempt.LastQuestionId = firstGroupQuestionId.Value;
        }

        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);

        return new AttemptTransitionResult(
            attempt.AttemptId,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            ToUtcOffset(attempt.PhaseDeadlineAt),
            remainingSeconds,
            EnumCodes.Parse<AttemptStatus>(attempt.Status));
    }

    public async Task<AttemptTransitionResult> CompleteAudioAndStartReviewAsync(
        int attemptId,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && (userId == null || a.UserId == userId), cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        var section = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId && (attempt.ActiveSkillId == null || q.Section.SkillId == attempt.ActiveSkillId.Value))
            .Select(q => q.Section)
            .FirstOrDefaultAsync(cancellationToken);

        int checkTimeSec = 120;
        if (section?.TimerPolicyJson != null)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(section.TimerPolicyJson);
                if (doc.RootElement.TryGetProperty("reviewSeconds", out var revProp) && revProp.TryGetInt32(out var rev))
                {
                    checkTimeSec = rev;
                }
                else if (doc.RootElement.TryGetProperty("checkTimeSec", out var chkProp) && chkProp.TryGetInt32(out var chk))
                {
                    checkTimeSec = chk;
                }
            }
            catch
            {
                // fallback to default
            }
        }

        attempt.PhaseDeadlineAt = DateTimeOffset.UtcNow.UtcDateTime.AddSeconds(checkTimeSec);
        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);

        return new AttemptTransitionResult(
            attempt.AttemptId,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            ToUtcOffset(attempt.PhaseDeadlineAt),
            remainingSeconds,
            EnumCodes.Parse<AttemptStatus>(attempt.Status));
    }

    public static string GenerateAuthorizationCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return $"EX-{RandomNumberGenerator.GetString(chars, 6)}";
    }

    public async Task<SittingSyncResult> SaveSittingSyncAsync(
        int attemptId,
        SittingSyncRequest request,
        int? userId = null,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && (userId == null || a.UserId == userId), cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        if (attempt.PreflightVerifiedAt == null)
        {
            await EnsurePreflightVerifiedAsync(attempt, cancellationToken);
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, attempt.UserId, isAutoSubmit: true, cancellationToken);
            if (onAutoSubmitted != null)
            {
                await onAutoSubmitted(attempt.AttemptId, attempt.UserId);
            }
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);
        if (phaseDeadline.HasValue && DateTimeOffset.UtcNow >= phaseDeadline.Value)
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam phase time has expired.");
        }

        int savedCount = 0;
        if (request.Answers != null && request.Answers.Count > 0)
        {
            foreach (var ans in request.Answers)
            {
                await SaveAnswerAsync(
                    attempt.AttemptId,
                    attempt.UserId,
                    ans.QuestionId,
                    ans.SelectedOptionId,
                    ans.IsFlagged,
                    onAutoSubmitted,
                    cancellationToken,
                    ans.ResponseText,
                    ans.SelectedOptionIdsJson);
                savedCount++;
            }
        }

        if (request.LastAudioOffsetMs.HasValue && request.LastAudioOffsetMs.Value >= 0)
        {
            attempt.LastAudioOffsetMs = request.LastAudioOffsetMs.Value;
        }

        if (request.CandidateNotesJson != null)
        {
            attempt.CandidateNotesJson = request.CandidateNotesJson;
        }

        if (request.LastQuestionId.HasValue && (request.Answers == null || request.Answers.Count == 0))
        {
            attempt.LastQuestionId = request.LastQuestionId.Value;
        }

        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);

        return new SittingSyncResult(
            attempt.AttemptId,
            remainingSeconds,
            attempt.IsAutoSubmitted,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            phaseDeadline,
            attempt.LastAudioOffsetMs,
            savedCount);
    }

    public async Task<AttemptResumeView> ResumeAttemptByCodeAsync(
        string authorizationCode,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, "Authorization code is required.");
        }

        var normalizedCode = authorizationCode.Trim().ToUpperInvariant();
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .SingleOrDefaultAsync(a => a.AuthorizationCode == normalizedCode && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"No exam attempt found for authorization code '{authorizationCode}'.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "This exam attempt is no longer in progress.");
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.AttemptId)
            .ToDictionaryAsync(a => a.QuestionId, cancellationToken);

        var freeResponses = await db.FreeResponses
            .Where(fr => fr.AttemptId == attempt.AttemptId)
            .ToDictionaryAsync(fr => fr.QuestionId, cancellationToken);

        var questionIds = await db.Questions
            .Where(q => q.ExamId == attempt.ExamId)
            .OrderBy(q => q.DisplayOrder)
            .Select(q => q.QuestionId)
            .ToListAsync(cancellationToken);

        var answerSheet = questionIds.Select(qId =>
        {
            answers.TryGetValue(qId, out var ans);
            freeResponses.TryGetValue(qId, out var fr);
            return new AttemptAnswerSheetItem(
                qId,
                ans?.SelectedOptionId,
                ans?.SelectedOptionIdsJson,
                fr?.ResponseText ?? ans?.ResponseText,
                ans?.IsFlagged ?? false);
        }).ToList();

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);
        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);

        return new AttemptResumeView(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.AuthorizationCode ?? normalizedCode,
            attempt.ActiveSkillId,
            attempt.ActiveUnitNumber,
            attempt.ActiveGroupId,
            attempt.LastQuestionId,
            attempt.LastAudioOffsetMs,
            attempt.CandidateNotesJson,
            remainingSeconds,
            attempt.ExpiresAt,
            phaseDeadline,
            answerSheet);
    }

    private static DateTimeOffset? ToUtcOffset(DateTime? dt) =>
        dt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc), TimeSpan.Zero) : null;

    private static DateTimeOffset ResolveEffectiveDeadline(ExamAttempt attempt)
    {
        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);
        return phaseDeadline.HasValue && phaseDeadline.Value < attempt.ExpiresAt
            ? phaseDeadline.Value
            : attempt.ExpiresAt;
    }
}
