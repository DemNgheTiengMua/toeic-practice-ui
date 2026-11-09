using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
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

        // #257: the display factor divides the stored total for presentation, so it has to be the rule
        // the attempt was scored under. Reading the live certificate column meant a re-standardisation
        // after the sitting printed a sum-based total divided by 10, a wrong number on a page about a
        // real result rather than a stale convention.
        var displayRule = await LoadAggregationRuleAsync(attempt.ExamId, attempt.CertId, cancellationToken);

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
            gradingStates.Count(status => status == AiGradingStatus.Failed.ToCode()),
            DisplayFactor(displayRule));
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

        // #186: the version each answer pinned is what review renders. Not the item's current version,
        // and not the paper's binding as it stands today: a later correction creates version 2 and a
        // later assembly edit can re-point the binding, and neither may move a finished attempt.
        //
        // The answer's own pin decides, and nothing else does. An answer with no pin is one recorded
        // before the pool reached this paper, so the learner answered from the question row and the
        // review must render that row — falling back to today's binding would show options the learner
        // was never offered, and lose the one they actually picked.
        var pinnedVersionIds = answers.Values
            .Where(a => a.ItemVersionId.HasValue)
            .Select(a => a.ItemVersionId!.Value)
            .Distinct()
            .ToList();
        var pinnedVersions = pinnedVersionIds.Count == 0
            ? new Dictionary<int, ItemVersion>()
            : (await db.ItemVersions.AsNoTracking()
                .Where(v => pinnedVersionIds.Contains(v.ItemVersionId))
                .ToListAsync(cancellationToken))
                .ToDictionary(v => v.ItemVersionId);

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

        // #227: the band the review prints is the NAME, and a name is exactly what a later
        // re-standardisation rewrites. The attempt stores the code, so the name comes from the
        // edition's frozen grid. Falling back to today's `ScoreBands` is for a paper with no edition
        // only - a paper that has one must not read a grid nobody was measured against.
        var frozenBandName = MatchFrozenBandName(
            await LoadEditionAsync(attempt.ExamId, cancellationToken),
            attempt.BandCode);

        var sectionMasterAudio = await LoadSectionMasterAudioAsync(attempt.ExamId, cancellationToken);
        var reviewQuestions = new List<AttemptReviewQuestion>();
        var navigator = new List<ReviewNavigatorItem>();

        for (int idx = 0; idx < questions.Count; idx++)
        {
            var q = questions[idx];
            int displayOrder = idx + 1;

            bool hasAnswer = answers.TryGetValue(q.QuestionId, out var ans);

            // #186: the pinned version wins. A reviewed attempt renders the stem, options and
            // explanation the learner actually sat; an answer with no pin was answered from the
            // question row, so that is what it renders.
            var pinnedVersion = ans?.ItemVersionId is int pinnedVersionId
                ? pinnedVersions.GetValueOrDefault(pinnedVersionId)
                : null;

            var options = pinnedVersion is null
                ? q.QuestionOptionQuestionNavigations
                    .OrderBy(o => o.Label)
                    .Select(o => new AttemptReviewOption(o.OptionId, o.Label, o.Text, o.IsCorrect, o.Rationale))
                    .ToList()
                : PoolReviewOptions(pinnedVersion.ItemVersionId, pinnedVersion);

            var stem = pinnedVersion?.Stem ?? q.Stem;
            var explanation = pinnedVersion?.Explanation ?? q.Explanation;
            int? selectedOptionId = pinnedVersion is null
                ? ans?.SelectedOptionId
                : SelectedPoolOptionId(pinnedVersion.ItemVersionId, ans);
            bool isFlagged = ans?.IsFlagged ?? false;
            // #253: answered is "carries content", not "chose an option". A productive answer has no
            // option id at all, so reading only selectedOptionId reported a graded writing answer as
            // unanswered and, because isCorrect is derived from this flag, hid a grade that exists.
            bool isAnswered = ans is not null
                && AnswerContent.IsAnswered(
                    ans.SelectedOptionId, ans.SelectedLabel, ans.ResponseText, ans.SelectedOptionIdsJson);
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
                stem,
                q.Group.ImagePath,
                q.Group.ImageAlt,
                audio?.Path,
                audio?.StartMs,
                audio?.EndMs,
                q.Group.Transcript,
                q.Group.Passage,
                explanation,
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
        int answeredCount = questions.Count(q => answers.TryGetValue(q.QuestionId, out var a) && IsAnswered(a));
        int correctCount = questions.Count(q => answers.TryGetValue(q.QuestionId, out var a) && a.IsCorrect == true);
        int incorrectCount = questions.Count(q => answers.TryGetValue(q.QuestionId, out var a) && IsAnswered(a) && a.IsCorrect == false);
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
            frozenBandName ?? band?.Name ?? attempt.BandCode,
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
}
