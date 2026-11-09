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
        int? DurationMinutes,
        bool LockNavigationOneWay);

    /// <summary>
    /// The one guard a sitting write passes through: the attempt must exist, belong to the caller when
    /// one is named, and still be in progress. The caller supplies the query because two callers need
    /// the exam navigation loaded; the two refusals and their messages are the contract, not the query.
    /// </summary>
    private async Task<ExamAttempt> LoadSittingAttemptAsync(
        IQueryable<ExamAttempt> attempts,
        int attemptId,
        int? userId,
        CancellationToken cancellationToken)
    {
        var attempt = await attempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && (userId == null || a.UserId == userId), cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        return attempt;
    }

    /// <summary>
    /// #227 / #257: the aggregation rule this attempt was scored under.
    /// <para>
    /// <b>The edition wins; the certificate is the fallback, not the rule.</b> #227 froze
    /// <c>ScoreAggregationRule</c> on the edition because changing a certificate's rule after a sitting
    /// changes what that sitting's stored total means. Every reader went on selecting the live column,
    /// so the freeze was a claim nothing consulted: re-standardise a certificate from <c>sum</c> to
    /// <c>avg_half_step</c> and a re-score, a re-projection or a displayed factor uses the new rule on
    /// an attempt computed under the old one.
    /// </para>
    /// <para>
    /// <b>An attempt with no edition row reads live configuration, and that is intended.</b> The row's
    /// absence is the record - "no edition existed" - which
    /// <c>A_historical_attempt_with_no_edition_row_reads_todays_configuration_and_says_so</c> pins. A
    /// silent <c>??</c> would make that path invisible, so the fallback is a named branch here.
    /// </para>
    /// </summary>
    private async Task<string> LoadAggregationRuleAsync(int examId, int certId, CancellationToken cancellationToken)
    {
        var frozen = await db.ExamEditions
            .AsNoTracking()
            .Where(e => e.ExamId == examId)
            .Select(e => e.ScoreAggregationRule)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(frozen))
        {
            return frozen;
        }

        return await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => c.ScoreAggregationRule)
            .SingleAsync(cancellationToken);
    }

    /// <summary>Studio-uploaded master track per section of the exam (ExamSectionAudio).</summary>
    private Task<Dictionary<int, string>> LoadSectionMasterAudioAsync(int examId, CancellationToken cancellationToken) =>
        db.ExamSectionAudios
            .AsNoTracking()
            .Where(a => a.ExamId == examId)
            .ToDictionaryAsync(a => a.SectionId, a => a.AudioPath, cancellationToken);

    /// <summary>
    /// #227: the frozen configuration an attempt sits on, read from <c>ExamEditions</c>. NULL for a
    /// paper with no edition row - a draft, which no learner can sit
    /// (<c>trg_ExamAttempts_PublishedExamOnly</c>), or a catalog loaded before the column existed.
    /// <para>
    /// This is the whole point of the ticket: the phase bar is allocated from THESE weights, not from
    /// <c>Skills.DurationMinutes</c> as it stands today, so editing a skill cannot move the clock on a
    /// sitting that is already underway, and re-reading a finished attempt's phase split gives the
    /// numbers it was actually scored against.
    /// </para>
    /// </summary>
    private Task<ExamEdition?> LoadEditionAsync(int examId, CancellationToken cancellationToken) =>
        db.ExamEditions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ExamId == examId, cancellationToken);

    /// <summary>
    /// #227: the frozen phase weights, or NULL when the paper has no edition. Deserialised rather
    /// than recomputed: a fallback that consulted the live <c>Skills</c> rows would be the
    /// re-derivation this ticket forbids, so a missing edition simply leaves the caller on its
    /// legacy path.
    /// </summary>
    private static IReadOnlyList<FrozenPhaseWeight>? ParseFrozenPhases(ExamEdition? edition)
    {
        if (edition is null || string.IsNullOrWhiteSpace(edition.PhasePlanJson))
        {
            return null;
        }

        var weights = System.Text.Json.JsonSerializer.Deserialize<List<FrozenPhaseWeight>>(edition.PhasePlanJson);
        return weights is { Count: > 0 } ? weights : null;
    }

    /// <summary>
    /// One row of <see cref="ExamEdition.PhasePlanJson"/>. Property names match the trigger's
    /// <c>FOR JSON PATH</c> output, so the serializer needs no naming policy.
    /// </summary>
    private sealed record FrozenPhaseWeight(
        int SkillId,
        string Code,
        string Name,
        int DisplayOrder,
        int? DurationMinutes,
        bool LockNavigationOneWay);

    private static ResolvedGroupAudio? ResolveGroupAudio(QuestionGroup group, Dictionary<int, string> sectionMasters) =>
        ExamAudio.Resolve(
            group.AudioPath,
            group.AudioStartMs,
            group.AudioEndMs,
            sectionMasters.GetValueOrDefault(group.SectionId));

    /// <summary>The exam's questions in sitting order, with everything the sitting projection reads.</summary>
    private Task<List<Question>> LoadExamQuestionsAsync(int examId, CancellationToken cancellationToken) =>
        db.Questions
            .Where(q => q.ExamId == examId)
            .Include(q => q.Group)
            .Include(q => q.Section).ThenInclude(s => s.Skill)
            .Include(q => q.QuestionOptionQuestionNavigations)
            .OrderBy(q => q.Section.Skill.DisplayOrder)
            .ThenBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .ToListAsync(cancellationToken);

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

        return await BuildAttemptTakeViewAsync(
            attempt,
            questionDisplayOrder,
            persistPosition: true,
            unlockNavigation: false,
            cancellationToken);
    }

    private sealed record FrozenBand(string Code, string Name, int MinTotal, int MaxTotal);

    /// <summary>
    /// #227: the band name the edition froze, or NULL when the paper has no edition or the frozen
    /// grid has no row for this code. NULL is what sends the caller to the live grid, so a code that
    /// was genuinely never in the frozen table is not renamed into something it never was.
    /// </summary>
    private static string? MatchFrozenBandName(ExamEdition? edition, string? bandCode)
    {
        if (edition is null || bandCode is null || string.IsNullOrWhiteSpace(edition.BandsJson))
        {
            return null;
        }

        var bands = System.Text.Json.JsonSerializer.Deserialize<List<FrozenBand>>(edition.BandsJson);
        return bands?.FirstOrDefault(b => string.Equals(b.Code, bandCode, StringComparison.Ordinal))?.Name;
    }

    /// <summary>
    /// #158: an exam as a learner would sit it, with no attempt behind it. Nothing here writes: no
    /// attempt, no answer, no ledger row. Navigation is unlocked so an admin can jump to any
    /// question, and a draft exam is readable — the caller is what decides who may ask.
    /// </summary>
    public async Task<AttemptTakeView> GetExamPreviewTakeAsync(
        int examId,
        int? questionDisplayOrder,
        CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams
            .AsNoTracking()
            .Include(e => e.Cert)
            .SingleOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.ExamNotFound, $"Exam {examId} not found.");

        var now = DateTimeOffset.UtcNow;
        var preview = new ExamAttempt
        {
            AttemptId = 0,
            UserId = 0,
            ExamId = exam.ExamId,
            CertId = exam.CertId,
            Exam = exam,
            Cert = exam.Cert,
            Status = AttemptStatus.InProgress.ToCode(),
            StartedAt = now,
            ExpiresAt = now.AddMinutes(exam.DurationMinutes),
            ActiveUnitNumber = 1,
            PreflightVerifiedAt = now,
        };

        return await BuildAttemptTakeViewAsync(
            preview,
            questionDisplayOrder,
            persistPosition: false,
            unlockNavigation: true,
            cancellationToken);
    }

    /// <summary>
    /// The sitting projection shared by a real attempt and #158's preview. One owner means the
    /// admin approves exactly the view the learner gets; the two parameters are the only
    /// difference: an attempt may persist the audio-driven position, a preview may not write and
    /// does not gate navigation.
    /// </summary>
    private async Task<AttemptTakeView> BuildAttemptTakeViewAsync(
        ExamAttempt attempt,
        int? questionDisplayOrder,
        bool persistPosition,
        bool unlockNavigation,
        CancellationToken cancellationToken)
    {
        var questions = await LoadExamQuestionsAsync(attempt.ExamId, cancellationToken);
        int totalQuestions = questions.Count;
        if (totalQuestions == 0)
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Exam {attempt.ExamId} has no questions.");
        }

        // #186: where the pool has assembled this paper, the scored content comes from the version
        // the binding pinned. A paper with no binding reads its Questions rows exactly as before.
        var bindings = await LoadPaperBindingsAsync(attempt.ExamId, cancellationToken);
        var boundByQuestion = PlaceBindings(questions, bindings);

        var answersByQuestion = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.AttemptId)
            .ToDictionaryAsync(a => a.QuestionId, cancellationToken);
        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Where(r => r.AttemptId == attempt.AttemptId)
            .ToDictionaryAsync(r => r.QuestionId, cancellationToken);

        int answeredCount = questions.Count(q =>
            q.QuestionType == QuestionType.FreeResponse.ToCode()
                ? freeResponses.TryGetValue(q.QuestionId, out var response) && (!string.IsNullOrWhiteSpace(response.ResponseText) || !string.IsNullOrWhiteSpace(response.AudioPath))
                : answersByQuestion.TryGetValue(q.QuestionId, out var answer) && IsAnswered(answer));
        int flaggedCount = answersByQuestion.Values.Count(a => a.IsFlagged);

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

            bool phaseOrGroupLocked = !unlockNavigation && IsPhaseOrUnitOrGroupLocked(attempt, requestedQuestion);
            bool blockedBySystemPaced = !unlockNavigation
                && string.Equals(currentSavedQuestion.Section.NavigationMode, "SYSTEM_PACED_AUTO", StringComparison.OrdinalIgnoreCase)
                && requestedIndex != savedIndex;
            bool blockedByOneWayAudio = !unlockNavigation
                && string.Equals(currentSavedQuestion.Section.NavigationMode, "AUDIO_DRIVEN", StringComparison.OrdinalIgnoreCase)
                && requestedIndex < minAllowedAudioIndex;

            if (!phaseOrGroupLocked && !blockedBySystemPaced && !blockedByOneWayAudio)
            {
                targetIndex = requestedIndex;
                if (persistPosition
                    && attempt.Status == AttemptStatus.InProgress.ToCode()
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
            boundByQuestion.TryGetValue(q.QuestionId, out var bound);

            // #186: the pinned version's stem and options are the scored content; everything else —
            // type, word limits, timers, the section's chrome — is the paper's, not the item's, so it
            // still comes from the question row.
            var opts = bound is null
                ? q.QuestionOptionQuestionNavigations
                    .OrderBy(o => o.Label)
                    .Select(o => new AttemptOption(o.OptionId, o.Label, o.Text))
                    .ToList()
                : PoolOptions(bound.ItemVersionId, bound.Version);
            var stem = bound?.Version.Stem ?? q.Stem;
            var selectedOptionId = bound is null ? ans?.SelectedOptionId : SelectedPoolOptionId(bound.ItemVersionId, ans);
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
                stem,
                q.Group.ImagePath,
                q.Group.ImageAlt,
                audio?.Path,
                audio?.StartMs,
                audio?.EndMs,
                q.Group.Transcript,
                q.Group.Passage,
                opts,
                selectedOptionId,
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

        var currentQuestionInfo = allQuestions[targetIndex];

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);
        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);
        var isPhaseExpired = !unlockNavigation && phaseDeadline.HasValue && DateTimeOffset.UtcNow >= phaseDeadline.Value;

        var navigator = questions.Select((q, idx) =>
        {
            answersByQuestion.TryGetValue(q.QuestionId, out var answer);
            freeResponses.TryGetValue(q.QuestionId, out var response);
            var isLocked = !unlockNavigation && (isPhaseExpired
                || IsPhaseOrUnitOrGroupLocked(attempt, q)
                || (string.Equals(q.Section.NavigationMode, "SYSTEM_PACED_AUTO", StringComparison.OrdinalIgnoreCase)
                    && idx != targetIndex)
                || (string.Equals(q.Section.NavigationMode, "AUDIO_DRIVEN", StringComparison.OrdinalIgnoreCase)
                    && idx < GetFirstClusterIndex(questions, targetIndex)));

            return new NavigatorItem(
                q.QuestionId,
                idx + 1,
                q.SectionId,
                q.Section.Code,
                EnumCodes.Parse<Modality>(q.Section.Modality),
                q.QuestionType == QuestionType.FreeResponse.ToCode()
                    ? (!string.IsNullOrWhiteSpace(response?.ResponseText) || !string.IsNullOrWhiteSpace(response?.AudioPath))
                    : IsAnswered(answer),
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
                group.First().Section.Skill.DurationMinutes,
                group.First().Section.Skill.LockNavigationOneWay))
            .OrderBy(skill => skill.DisplayOrder)
            .ToList();

        // #227: an attempt on a published paper sits the edition's phase weights, not today's skill
        // rows. The fallback below is the legacy path for a paper with no edition, and it is chosen
        // by the edition's absence rather than by trying the live rows first - a paper that HAS an
        // edition must never fall back, or the pin would only hold until someone edited a skill.
        var frozenPhases = ParseFrozenPhases(await LoadEditionAsync(attempt.ExamId, cancellationToken));
        var phases = frozenPhases is null
            ? BuildAttemptPhases(
                phaseGroups,
                Math.Max(1, (int)(attempt.ExpiresAt - attempt.StartedAt).TotalSeconds))
            : BuildFrozenPhases(
                frozenPhases,
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

        var weights = PhaseWeights(skills.Select(skill => skill.DurationMinutes));
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
                durationSeconds,
                skill.LockNavigationOneWay));
            allocatedSeconds += durationSeconds;
        }

        return phases;
    }

    /// <summary>
    /// #227: the same allocation run over the FROZEN weights. It shares
    /// <see cref="PhaseWeights"/> with the live path on purpose: if the two computed their weights
    /// differently, "the same sitting with the same numbers" would be a claim rather than a fact.
    /// </summary>
    private static IReadOnlyList<AttemptPhaseInfo> BuildFrozenPhases(
        IReadOnlyList<FrozenPhaseWeight> frozen,
        int totalSeconds)
    {
        if (frozen.Count == 0)
        {
            return [];
        }

        var weights = PhaseWeights(frozen.Select(skill => skill.DurationMinutes));
        var totalWeight = weights.Sum();
        var allocatedSeconds = 0;
        var phases = new List<AttemptPhaseInfo>(frozen.Count);

        for (var index = 0; index < frozen.Count; index++)
        {
            var skill = frozen[index];
            var durationSeconds = index == frozen.Count - 1
                ? totalSeconds - allocatedSeconds
                : (int)Math.Floor(totalSeconds * weights[index] / totalWeight);
            phases.Add(new AttemptPhaseInfo(
                skill.SkillId,
                skill.Code,
                skill.Name,
                skill.DisplayOrder,
                allocatedSeconds,
                durationSeconds,
                skill.LockNavigationOneWay));
            allocatedSeconds += durationSeconds;
        }

        return phases;
    }

    /// <summary>
    /// The one weighting rule: a declared duration is its own weight, and a skill without one carries
    /// the average of those that have it. Extracted so the live and the frozen allocation cannot
    /// drift apart.
    /// </summary>
    private static List<double> PhaseWeights(IEnumerable<int?> durations)
    {
        var list = durations.ToList();
        var positive = list.Where(d => d is > 0).Select(d => (double)d!.Value).ToList();
        var fallback = positive.Count > 0 ? positive.Average() : 1d;
        return list.Select(d => d is int duration && duration > 0 ? (double)duration : fallback).ToList();
    }
}
