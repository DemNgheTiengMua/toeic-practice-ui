using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
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
}
