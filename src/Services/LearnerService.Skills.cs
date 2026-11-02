using BusinessObjects;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearnerService
{
    private const int WeakestCategoriesPerSkill = 3;

    /// <summary>#159: one card per skill of the active certificate, in the certificate's own order.
    /// <para>
    /// The list comes from <c>Cert.Skills</c>, which is #149's rule applied here: a certificate that
    /// names its skills Comprehension and Expression gets those two cards. A fixed Listening/Reading
    /// pair used to render two empty cards under headings the certificate does not use.
    /// </para>
    /// Accuracy is read from <c>vw_UserCategoryAccuracy</c> - the same view the weakness analysis page
    /// uses - so the card and that page cannot disagree.
    /// </summary>
    private async Task<IReadOnlyList<SkillCard>> BuildSkillCardsAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken)
    {
        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SkillId)
            .Select(s => new { s.SkillId, s.Code, s.Name, s.Modality, s.MaxScaledScore })
            .ToListAsync(cancellationToken);

        // #159 review: a stored score is not the number the learner reads. A certificate that averages
        // to a half step keeps marks in tenths (IELTS 8.5 stores 85), so the card has to say "85 / 90"
        // only after dividing - which is what ScoreDisplay.Format does everywhere else.
        var factor = await ScoreDisplayFactorAsync(certId, cancellationToken);

        if (skills.Count == 0)
        {
            return [];
        }

        // The latest graded score per skill, in the certificate's own units.
        var latestBySkill = (await db.AttemptSkillScores
                .AsNoTracking()
                .Where(s => s.CertId == certId
                    && s.Attempt.UserId == userId
                    && s.Attempt.Status == AttemptStatus.Graded.ToCode())
                .OrderByDescending(s => s.Attempt.SubmittedAt ?? s.Attempt.StartedAt)
                .ThenByDescending(s => s.AttemptId)
                .Select(s => new { s.SkillId, s.ScaledScore })
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.SkillId)
            .ToDictionary(g => g.Key, g => g.First().ScaledScore);

        var categories = await db.QuestionCategories
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new { c.CategoryId, c.Code, c.Name })
            .ToDictionaryAsync(c => c.CategoryId, cancellationToken);

        // A category answered through a section of a skill belongs to that skill. A category drilled
        // from two skills is claimed by the first, so one answer is never counted on two cards.
        var skillByCategory = categories.Count == 0
            ? new Dictionary<int, int>()
            : (await db.Questions
                .AsNoTracking()
                .Where(q => q.CategoryId != null && q.Section.CertId == certId)
                .Select(q => new { CategoryId = q.CategoryId!.Value, q.SkillId })
                .Distinct()
                .ToListAsync(cancellationToken))
                .GroupBy(c => c.CategoryId)
                .ToDictionary(g => g.Key, g => g.Min(x => x.SkillId));

        var accuracies = categories.Count == 0
            ? []
            : await db.vw_UserCategoryAccuracies
                .AsNoTracking()
                .Where(v => v.UserId == userId
                    && v.CategoryId != null
                    && (v.Answered ?? 0) > 0
                    && categories.Keys.Contains(v.CategoryId.Value))
                .ToListAsync(cancellationToken);

        var weakestBySkill = new Dictionary<int, List<SkillCategoryAccuracy>>();
        foreach (var row in accuracies)
        {
            var categoryId = row.CategoryId!.Value;
            if (!categories.TryGetValue(categoryId, out var category)
                || !skillByCategory.TryGetValue(categoryId, out var skillId))
            {
                continue;
            }

            var answered = row.Answered ?? 0;
            var accuracy = (int)Math.Round((double)(row.Correct ?? 0) / answered * 100);

            if (!weakestBySkill.TryGetValue(skillId, out var list))
            {
                list = [];
                weakestBySkill[skillId] = list;
            }

            list.Add(new SkillCategoryAccuracy(category.Code, category.Name, accuracy, answered));
        }

        return skills.Select(s => new SkillCard(
                s.SkillId,
                s.Code,
                s.Name,
                EnumCodes.Parse<Modality>(s.Modality),
                latestBySkill.TryGetValue(s.SkillId, out var score) ? score : null,
                s.MaxScaledScore,
                weakestBySkill.TryGetValue(s.SkillId, out var weakest)
                    ? weakest
                        .OrderBy(c => c.AccuracyPercent)
                        .ThenByDescending(c => c.Answered)
                        .ThenBy(c => c.Name, StringComparer.Ordinal)
                        .Take(WeakestCategoriesPerSkill)
                        .ToList()
                    : [],
                factor))
            .ToList();
    }

    /// <summary>#159: how many stored units make one reported point for this certificate. One query,
    /// shared by the goal block and the skill cards, so neither can print a tenths score whole.</summary>
    private async Task<int> ScoreDisplayFactorAsync(int certId, CancellationToken cancellationToken)
    {
        var rule = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => c.ScoreAggregationRule)
            .FirstOrDefaultAsync(cancellationToken);

        return ScoreDisplay.FactorFor(rule);
    }
}
