using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearnerService
{
    private async Task<RubricHighlight?> BuildRubricHighlightAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken)
    {
        var speakingCode = ExamPurpose.SpeakingPractice.ToCode();
        var writingCode = ExamPurpose.WritingPractice.ToCode();
        var gradedCode = AttemptStatus.Graded.ToCode();

        var latestAttempt = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId
                && a.CertId == certId
                && a.Status == gradedCode
                && (a.Exam.Purpose == speakingCode || a.Exam.Purpose == writingCode)
                && a.FreeResponseAttempts.Any(fr => fr.AiGradingFreeResponse != null && fr.AiGradingFreeResponse.AiGradingScores.Any()))
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .ThenByDescending(a => a.AttemptId)
            .Select(a => new
            {
                a.AttemptId,
                a.Exam.Purpose,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latestAttempt is null)
        {
            return null;
        }

        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Where(fr => fr.AttemptId == latestAttempt.AttemptId)
            .Include(fr => fr.Question)
            .Include(fr => fr.AiGradingFreeResponse)
                .ThenInclude(g => g!.AiGradingScores)
                    .ThenInclude(s => s.GradingCriterion)
                        .ThenInclude(c => c.Skill)
            .ToListAsync(cancellationToken);

        var sectionCriteria = await db.SectionCriteria
            .AsNoTracking()
            .Where(sc => sc.Section.CertId == certId)
            .ToListAsync(cancellationToken);

        var candidates = freeResponses
            .Where(fr => fr.AiGradingFreeResponse?.AiGradingScores is { Count: > 0 })
            .SelectMany(fr => fr.AiGradingFreeResponse!.AiGradingScores.Select(s =>
            {
                var overrideMax = sectionCriteria
                    .FirstOrDefault(sc => sc.SectionId == fr.Question.SectionId && sc.CriterionId == s.CriterionId)
                    ?.MaxScore;
                var maxScore = overrideMax ?? s.GradingCriterion.MaxScore;
                var ratio = maxScore > 0 ? s.Score / maxScore : s.Score;
                return new
                {
                    SkillName = s.GradingCriterion.Skill.Name,
                    CriterionName = s.GradingCriterion.Name,
                    s.Score,
                    MaxScore = maxScore,
                    Ratio = ratio,
                    s.Comment,
                    s.GradingCriterion.DisplayOrder,
                    QuestionDisplayOrder = fr.Question.DisplayOrder,
                };
            }))
            .OrderBy(c => c.Ratio)
            .ThenBy(c => c.Score)
            .ThenBy(c => c.DisplayOrder)
            .ThenBy(c => c.QuestionDisplayOrder)
            .ToList();

        var lowest = candidates.FirstOrDefault();
        if (lowest is null)
        {
            return null;
        }

        return new RubricHighlight(
            SkillName: lowest.SkillName,
            CriterionName: lowest.CriterionName,
            Score: lowest.Score,
            MaxScore: lowest.MaxScore,
            Comment: lowest.Comment,
            AttemptId: latestAttempt.AttemptId,
            Purpose: EnumCodes.Parse<ExamPurpose>(latestAttempt.Purpose));
    }
}
