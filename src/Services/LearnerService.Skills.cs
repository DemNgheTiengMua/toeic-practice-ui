using BusinessObjects;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearnerService
{
    private const int WeakestTopicsPerSkill = 3;

    /// <summary>#159: one card per skill of the active certificate, in the certificate's own order.
    /// <para>
    /// The list comes from <c>Cert.Skills</c>, which is #149's rule applied here: a certificate that
    /// names its skills Comprehension and Expression gets those two cards. A fixed Listening/Reading
    /// pair used to render two empty cards under headings the certificate does not use.
    /// </para>
    /// Accuracy is read from <c>vw_UserTopicAccuracy</c> - the same view the weakness analysis page
    /// uses - so the card and that page cannot disagree. #271 moved it off the retired category axis;
    /// a card therefore names a topic at a level, and a learner whose answers are all legacy has no
    /// cell to show rather than a 0%.
    /// </summary>
    private async Task<IReadOnlyList<SkillCard>> BuildSkillCardsAsync(
        int userId,
        int certId,
        LearnerStanding? standing,
        CancellationToken cancellationToken)
    {
        var measured = standing?.Skills.ToDictionary(s => s.SkillId) ?? [];
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

        // #271: a cell belongs to a skill through its modality, which CONTEXT.md makes one-to-one with
        // a skill on a certificate. Two skills sharing a modality is not a shape the product has; if it
        // ever became one the first skill takes the cell, so one answer is never counted on two cards.
        var skillByModality = new Dictionary<Modality, int>();
        foreach (var skill in skills)
        {
            skillByModality.TryAdd(EnumCodes.Parse<Modality>(skill.Modality), skill.SkillId);
        }

        var cells = await TopicAccuracyReader.ReadAsync(db, userId, certId, cancellationToken);

        var weakestBySkill = new Dictionary<int, List<SkillCategoryAccuracy>>();
        foreach (var cell in cells)
        {
            if (!skillByModality.TryGetValue(cell.Modality, out var skillId))
            {
                continue;
            }

            if (!weakestBySkill.TryGetValue(skillId, out var list))
            {
                list = [];
                weakestBySkill[skillId] = list;
            }

            // The percentage is the view's. Dividing Correct by Answered here would be a second
            // definition of the same figure - the defect D3 forbids.
            list.Add(new SkillCategoryAccuracy(
                cell.TopicCode, cell.TopicName, cell.AccuracyPercent, cell.Answered, cell.CefrLevel.Code));
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
                        .ThenBy(c => c.LevelCode, StringComparer.Ordinal)
                        .Take(WeakestTopicsPerSkill)
                        .ToList()
                    : [],
                factor,
                measured.TryGetValue(s.SkillId, out var state)
                    ? new SkillStandingLevel(state.Level, state.PositionInBand, state.MeasuredAt, state.IsStale)
                    : null))
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
