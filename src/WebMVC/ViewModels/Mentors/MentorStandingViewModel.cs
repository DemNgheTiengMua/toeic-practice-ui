using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #223 / D21: the standing block a mentor booking shows. This type exists so the three absences are
/// three different sentences rather than three nulls that render as the same dash.
/// <para>
/// <b>"Not measured" and "not examined" are not the same state and must not share a glyph.</b> The
/// first is the learner's: the certificate tests this skill and nothing has measured it yet, so a
/// placement fills it. The second is the certificate's: no amount of sitting will ever fill it. And a
/// learner with nothing measured at all is a third: the block says a placement is owed instead of
/// showing a table of four absences.
/// </para>
/// <para>
/// <b>The level is printed as a level.</b> It arrives already labelled from
/// <see cref="CefrLevelLabels"/>; nothing here looks a band up, and <c>BandCode</c> is not read at all.
/// Numbers are rendered in the mono face by the view, per the design system.
/// </para>
/// </summary>
public sealed record MentorStandingViewModel(MentorLearnerStandingDto Standing)
{
    /// <summary>Nothing measured anywhere: one sentence and no table.</summary>
    public bool OwesPlacement => Standing.HasNoMeasurement;
    public string OverallText => Standing.OverallLevelCode is { } code
        ? CefrLevelLabels.LabelForCode(code, NullLog)
        : "No overall yet";

    /// <summary>The overall without its limiting skill lets a mentor read a number and guess. Both, or neither.</summary>
    public string? LimitingSkillText => Standing.LimitingSkillCode is { } code
        ? $"Weakest measured skill: {code}"
        : null;

    public IReadOnlyList<MentorStandingRowViewModel> Rows => Standing.Skills
        .Select(skill => new MentorStandingRowViewModel(skill))
        .ToList();

    /// <summary>A logger the label registry can log an unknown code against; a view has no logger of
    /// its own and the fallback text is what matters here.</summary>
    private static readonly Microsoft.Extensions.Logging.ILogger NullLog =
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}

/// <summary>One row. <see cref="State"/> is the word the row reads, and it is never shared between the
/// two absences.</summary>
public sealed record MentorStandingRowViewModel(MentorSkillStandingDto Skill)
{
    public string Modality => ModalityLabels.LabelForCode(Skill.Modality, NullLog);

    public string SkillName => Skill.SkillName ?? Modality;

    public string State => Skill.Status switch
    {
        MentorSkillStandingStatus.Measured => "Measured",
        MentorSkillStandingStatus.NotMeasured => "Not measured yet",
        MentorSkillStandingStatus.NotExamined => "Not examined by this certificate",
        _ => "Not reported",
    };

    /// <summary>The level and its position, as a position and never a percentage. Both are measured
    /// facts, so a row the certificate does not examine prints neither, even if a position arrived.</summary>
    public string? Level => Skill.Status == MentorSkillStandingStatus.Measured && Skill.LevelCode is { } code
        ? CefrLevelLabels.LabelForCode(code, NullLog)
        : null;

    public string? Position => Skill.Status == MentorSkillStandingStatus.Measured && Skill.PositionInBand is { } position
        ? $"{position}/100 in level"
        : null;

    /// <summary>A stale result is marked, not hidden: D18's ReuseWindow has closed on it.</summary>
    public bool IsStale => Skill.IsStale;

    /// <summary>Only a measured row can be wrong about a learner; the other two are about the paper.</summary>
    public string CssClass => Skill.Status switch
    {
        MentorSkillStandingStatus.Measured => "mentor-standing__row mentor-standing__row--measured",
        MentorSkillStandingStatus.NotMeasured => "mentor-standing__row mentor-standing__row--unmeasured",
        _ => "mentor-standing__row mentor-standing__row--not-examined",
    };

    private static readonly Microsoft.Extensions.Logging.ILogger NullLog =
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
