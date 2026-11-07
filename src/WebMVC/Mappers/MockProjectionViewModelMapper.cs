using Contracts;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Mappers;

/// <summary>
/// #222 / PRN-03: prepares the projected-level placard. This is where the product's two quantities are
/// kept apart on the way to the page: the certificate score is a number on the certificate's own scale,
/// the projected level is a per-skill CEFR level read from the certificate's cut-score table, and each
/// is named as what it is.
/// <para>
/// <b>Nothing here derives a level.</b> A level arrives as <see cref="MockSkillProjectionDto.AwardedLevelCode"/>
/// or it does not arrive at all; the only thing this file does with the code is hand it to
/// <see cref="CefrLevelLabels"/>. A band code is never looked up as a level, and an unmapped code
/// renders as <c>Level not reported</c> rather than as itself.
/// </para>
/// </summary>
internal static class MockProjectionViewModelMapper
{
    /// <summary>
    /// The placard, or <see cref="MockProjectionViewModel.Available"/> false when the projection could
    /// not be fetched at all (the result page still renders its certificate score without it).
    /// </summary>
    public static MockProjectionViewModel Map(
        MockResultProjectionDto? projection,
        IReadOnlyList<AttemptSkillScoreDto> skillScores,
        int scoreDisplayFactor,
        ILogger logger)
    {
        if (projection is null)
        {
            return new MockProjectionViewModel { Available = false };
        }

        var nameBySkill = skillScores.ToDictionary(s => s.SkillId, s => s.SkillName);

        var rows = projection.Skills.Select(skill => Row(skill, nameBySkill, scoreDisplayFactor, logger)).ToList();

        var weakest = projection.WeakestProjectedSkillId is int limitingId
            && projection.WeakestProjectedLevelCode is { } limitingCode
                ? $"{nameBySkill.GetValueOrDefault(limitingId, "Skill")} · {CefrLevelLabels.LabelForCode(limitingCode, logger)}"
                : null;

        var differing = projection.Skills
            .Where(s => projection.DifferingSkillIds.Contains(s.SkillId))
            .Select(s => $"{nameBySkill.GetValueOrDefault(s.SkillId, "Skill")} → {CefrLevelLabels.LabelForCode(s.AwardedLevelCode, logger)}")
            .ToList();

        return new MockProjectionViewModel
        {
            Available = true,
            Historical = projection.Historical,
            Skills = rows,
            WeakestProjectedText = weakest,
            CannotJudgeText = projection.Historical
                ? "This attempt was sat before this certificate published a CEFR cut-score table. Its score and band are shown as recorded; no CEFR level is projected for it."
                : projection.CannotJudgeReason is { } reason
                    ? ReasonText(reason)
                    : null,
            StoredStateText = StoredStateText(projection, logger),
            SyncOffered = projection.SyncOffered && differing.Count > 0,
            SyncSummaryText = differing.Count == 0 ? string.Empty : string.Join(", ", differing),
            ScopeNote = projection.Historical
                ? string.Empty
                : "The projected level comes from this one paper, read against the certificate's per-skill cut scores. It is not your overall standing until you choose to sync it."
        };
    }

    private static MockSkillProjectionViewModel Row(
        MockSkillProjectionDto skill,
        IReadOnlyDictionary<int, string> nameBySkill,
        int scoreDisplayFactor,
        ILogger logger)
    {
        var name = nameBySkill.GetValueOrDefault(skill.SkillId, "Skill");
        var score = ScoreDisplay.Format(skill.ScaledScore, scoreDisplayFactor);

        return skill.Outcome switch
        {
            MockProjectionOutcomeDto.Projected when skill.AwardedLevelCode is { } code =>
                new(name, score, CefrLevelLabels.LabelForCode(code, logger), "Projected", "badge--info"),

            // Scored, and the certificate does not certify a level there. Saying "Below A1" would
            // invent the floor; saying nothing about the score would deny the number.
            MockProjectionOutcomeDto.Uncertified =>
                new(name, score, null, "Scored, no level awarded", "badge--muted"),

            MockProjectionOutcomeDto.DisplayCeiling =>
                new(name, score, null, "Above the highest level this certificate expresses", "badge--warning"),

            _ => new(
                name,
                score,
                null,
                skill.CannotJudgeReason is { } reason ? ReasonText(reason) : "No projected level",
                "badge--muted"),
        };
    }

    /// <summary>
    /// The projector's own reason, turned into a sentence. The codes are the ones
    /// <c>CefrProjection</c> publishes, so a new reason reaches a screen as the fallback rather than as
    /// an underscore.
    /// </summary>
    private static string ReasonText(string reason) => reason switch
    {
        "no_cut_score_row_for_level" =>
            "This score falls between two bands of the certificate's cut-score table, so no level is projected.",
        "no_cut_score_table" =>
            "This certificate has not published a CEFR cut-score table, so no level is projected.",
        "unknown_cut_score_table_version" =>
            "The cut-score version recorded for this attempt is not published, so no level is projected.",
        "bands_carry_no_study_hours" =>
            "This certificate's bands carry no study-hour evidence, so a level is not projected rather than guessed.",
        _ => "No projected level was available for this score.",
    };

    private static string StoredStateText(MockResultProjectionDto projection, ILogger logger)
    {
        if (projection.HasNoStoredMeasurement)
        {
            return "You have no measured level yet, so this paper is the first thing that would set one.";
        }

        return projection.StoredOverallLevelCode is { } code
            ? $"Your stored level is {CefrLevelLabels.LabelForCode(code, logger)}"
                + (projection.StoredLimitingSkillCode is { } limiting ? $" (lowest: {limiting})" : "")
                + "."
            : "Your stored level has nothing measured yet.";
    }
}
