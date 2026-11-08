using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BusinessObjects;

namespace Services;

/// <summary>
/// #244 / D21: one row of the payload an insight narrates. A coordinate is a level, a position inside
/// it and the continuous value the two were projected from; <see cref="Level"/> is null when nothing
/// measured this skill.
/// <para>
/// <b>An unmeasured skill carries no level, never A1.</b> #211 keeps it absent from
/// <c>LearnerStanding.Skills</c>; here it is present as a row with a null level so the narrative can be
/// refused when it names it, and the prompt can say "do not name a level for this skill".
/// </para>
/// </summary>
public sealed record InsightSkillFact(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    CefrLevel? Level,
    int? PositionInBand,
    decimal? ContinuousCoordinate);

/// <summary>
/// #244 / D21 / NFI-10: the structured payload an insight is allowed to narrate.
/// <para>
/// <b>It carries no verdict and no computed figure.</b> Every member is a stored value: the per-skill
/// coordinates and levels from <see cref="LearnerStanding"/> (#211), the overall and its limiting skill
/// from the same read, and the target band and feasibility verdict from <c>TargetBands</c>. The insight
/// narrates these; it never derives a fifth.
/// </para>
/// <para>
/// <b>The target is a band, not a level</b> (PRN-03): <see cref="TargetBandCode"/> is a score range on
/// the certificate's own scale, and nothing here converts it to CEFR.
/// </para>
/// </summary>
public sealed record InsightFacts(
    string CertCode,
    IReadOnlyList<InsightSkillFact> Skills,
    CefrLevel? Overall,
    string? LimitingSkillCode,
    string? TargetBandCode,
    string? TargetBandName,
    string? FeasibilityVerdict)
{
    /// <summary>True when no skill has been measured, so the payload owes the learner a placement.</summary>
    public bool HasNoMeasurement => Skills.All(s => s.Level is null);

    /// <summary>
    /// The payload as both the prompt and the narrative guard read it. One rendering, so a number the
    /// model is shown and a number it is allowed to repeat cannot drift apart.
    /// </summary>
    public string Describe()
    {
        var lines = new List<string> { $"Certificate: {CertCode}" };
        foreach (var skill in Skills)
        {
            lines.Add(skill.Level is { } level
                ? $"{skill.SkillName} ({skill.SkillCode}, {skill.Modality}): level {level.Code}, " +
                  $"{skill.PositionInBand}/100 in band, coordinate " +
                  (skill.ContinuousCoordinate ?? 0m).ToString("0.00", CultureInfo.InvariantCulture)
                : $"{skill.SkillName} ({skill.SkillCode}, {skill.Modality}): not measured, no level");
        }

        lines.Add(Overall is { } overall ? $"Overall (weakest measured skill): {overall.Code}" : "Overall: none");
        lines.Add($"Limiting skill: {LimitingSkillCode ?? "none"}");
        lines.Add(TargetBandCode is null
            ? "Target band: none set"
            : $"Target band: {TargetBandCode} ({TargetBandName ?? TargetBandCode})");
        lines.Add($"Feasibility: {FeasibilityVerdict ?? "cannot judge"}");
        return string.Join(Environment.NewLine, lines);
    }
}
