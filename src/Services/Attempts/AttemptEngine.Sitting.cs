using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    private sealed record AttemptSkillPhaseSource(
        int SkillId,
        string Code,
        string Name,
        int DisplayOrder,
        int? DurationMinutes);

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

        // Determine authoritative saved question index within the active phase/unit/group
        int savedIndex = -1;
        if (attempt.LastQuestionId.HasValue)
        {
            savedIndex = questions.FindIndex(q => q.QuestionId == attempt.LastQuestionId.Value);
        }

        if (savedIndex < 0 || IsPhaseOrUnitOrGroupLocked(attempt, questions[savedIndex]))
        {
            savedIndex = questions.FindIndex(q => !IsPhaseOrUnitOrGroupLocked(attempt, q));
            if (savedIndex < 0) savedIndex = 0;
        }

        int minAllowedAudioIndex = savedIndex >= 0 && savedIndex < totalQuestions
            ? GetFirstClusterIndex(questions, savedIndex)
            : 0;

        int targetIndex = savedIndex >= 0 ? savedIndex : 0;
        if (questionDisplayOrder.HasValue && questionDisplayOrder.Value >= 1 && questionDisplayOrder.Value <= totalQuestions)
        {
            int requestedIndex = questionDisplayOrder.Value - 1;
            var requestedQuestion = questions[requestedIndex];
            var currentSavedQuestion = savedIndex >= 0 && savedIndex < totalQuestions ? questions[savedIndex] : requestedQuestion;

            bool phaseOrGroupLocked = IsPhaseOrUnitOrGroupLocked(attempt, requestedQuestion);
            bool blockedBySystemPaced = string.Equals(currentSavedQuestion.Section.NavigationMode, "SYSTEM_PACED_AUTO", StringComparison.OrdinalIgnoreCase)
                && requestedIndex != savedIndex;
            bool blockedByOneWayAudio = string.Equals(currentSavedQuestion.Section.NavigationMode, "AUDIO_DRIVEN", StringComparison.OrdinalIgnoreCase)
                && requestedIndex < minAllowedAudioIndex;

            if (!phaseOrGroupLocked && !blockedBySystemPaced && !blockedByOneWayAudio)
            {
                targetIndex = requestedIndex;
                if (attempt.Status == AttemptStatus.InProgress.ToCode()
                    && targetIndex > savedIndex
                    && string.Equals(requestedQuestion.Section.NavigationMode, "AUDIO_DRIVEN", StringComparison.OrdinalIgnoreCase))
                {
                    attempt.LastQuestionId = requestedQuestion.QuestionId;
                    await db.SaveChangesAsync(cancellationToken);
                    savedIndex = targetIndex;
                    minAllowedAudioIndex = GetFirstClusterIndex(questions, savedIndex);
                }
            }
            else if (!attempt.ActiveSkillId.HasValue)
            {
                targetIndex = requestedIndex;
            }
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
                q.GroupId,
                q.Section.TimerPolicyJson));
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
                || IsPhaseOrUnitOrGroupLocked(attempt, q)
                || (string.Equals(q.Section.NavigationMode, "SYSTEM_PACED_AUTO", StringComparison.OrdinalIgnoreCase)
                    && idx != targetIndex)
                || (string.Equals(q.Section.NavigationMode, "AUDIO_DRIVEN", StringComparison.OrdinalIgnoreCase)
                    && idx < GetFirstClusterIndex(questions, targetIndex));

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

    private static bool IsPhaseOrUnitOrGroupLocked(ExamAttempt attempt, Question q) =>
        (attempt.ActiveSkillId.HasValue && q.Section.SkillId != attempt.ActiveSkillId.Value)
        || (q.Group.UnitNumber != attempt.ActiveUnitNumber)
        || (string.Equals(q.Section.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase)
            && attempt.ActiveGroupId.HasValue
            && q.GroupId != attempt.ActiveGroupId.Value);

    private static int GetFirstClusterIndex(IReadOnlyList<Question> questions, int currentIndex)
    {
        if (currentIndex <= 0 || currentIndex >= questions.Count) return Math.Max(0, currentIndex);
        var current = questions[currentIndex];
        if (current.Section.ClusterSize != 3) return currentIndex;

        for (int i = 0; i < currentIndex; i++)
        {
            var candidate = questions[i];
            if (candidate.GroupId == current.GroupId && candidate.SectionId == current.SectionId)
            {
                return i;
            }
        }
        return currentIndex;
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
}
