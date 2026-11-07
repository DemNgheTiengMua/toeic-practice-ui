
namespace Contracts;

/// <summary>
/// #208: one cell of the 28-cell grid, as the studio posts it. <paramref name="Band"/> is the rubric's
/// own CEFR band (0 = Pre-A1 ... 6 = C2) - see <see cref="RubricBands"/> for why that is not a
/// certificate's <c>ScoreBands</c> code.
/// <para>
/// <b>Two columns, never one.</b> <paramref name="Descriptor"/> is what the band looks like;
/// <paramref name="BinaryGates"/> is what must be true before the band is reached at all. A screen that
/// posts the same text for both is a grid that carries one fact twice, and the grader is then told
/// there are two thresholds where there is one.
/// </para>
/// </summary>
public sealed record RubricCellDto(byte Band, string Descriptor, string BinaryGates);

/// <summary>#208: one calibrated anchor at band 2, 3 or 4, attached from the studio.</summary>
public sealed record RubricAnchorDto(byte Band, string ResponseText);

/// <summary>
/// #208: one criterion of a rubric, with its grid and its anchors. <see cref="CriterionId"/> is 0 for a
/// criterion the screen will create; the studio is opened against a skill, not against a criterion.
/// </summary>
public sealed record RubricCriterionDto(
    int CriterionId,
    string Code,
    string Name,
    decimal MaxScore,
    decimal Weight,
    int DisplayOrder,
    IReadOnlyList<RubricCellDto> Cells,
    IReadOnlyList<RubricAnchorDto> Anchors)
{
    /// <summary>Bands 0-6 with no cell. The lock refuses a rubric with a hole; the screen paints it.</summary>
    public IReadOnlyList<byte> MissingBands =>
        RubricBands.All.Where(band => Cells.All(cell => cell.Band != band)).ToList();

    /// <summary>Bands 2, 3 or 4 carrying no anchor.</summary>
    public IReadOnlyList<byte> MissingAnchorBands =>
        RubricBands.AnchorBands.Where(band => Anchors.All(anchor => anchor.Band != band)).ToList();
}

/// <summary>
/// #208: the whole studio for one skill. <see cref="IsLocked"/> and <see cref="LockedAt"/> are the
/// lock row; <see cref="Ladder"/> is null when the ladder has never run, which is the state the lock
/// refuses.
/// </summary>
public sealed record RubricStudioDto(
    int CertId,
    string CertCode,
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    bool IsLocked,
    DateTimeOffset? LockedAt,
    IReadOnlyList<RubricCriterionDto> Criteria,
    RubricLadderDto? Ladder);

/// <summary>
/// #208: a ladder verdict as the screen shows it. <see cref="Outcome"/> is the schema's own code
/// (<c>accepted</c>, <c>not_monotonic</c>, <c>separation</c>, <c>trap_ceiling</c>,
/// <c>not_plotted</c>); <see cref="ResponsibleCriteria"/> names the criteria an author has to rewrite.
/// </summary>
public sealed record RubricLadderDto(
    string Outcome,
    bool Accepted,
    string? Reason,
    IReadOnlyList<string> ResponsibleCriteria,
    IReadOnlyList<RubricLadderPointDto> Points,
    DateTimeOffset? RanAt);

/// <summary>One plotted point: band, composite on the 0-6 axis, and whether it is the verbose trap.</summary>
public sealed record RubricLadderPointDto(byte Band, decimal Composite, bool IsVerboseTrap);

/// <summary>
/// #208: the ladder's input - seven responses. <see cref="ResponseText"/> is a whole answer, because a
/// benchmark that fits in a sentence measures nothing a band could separate on.
/// </summary>
public sealed record RubricLadderResponseDto(byte Band, string ResponseText, bool IsVerboseTrap);

/// <summary>#208: the request that runs the ladder for one skill.</summary>
public sealed record RunRubricLadderRequest(int CertId, int SkillId, IReadOnlyList<RubricLadderResponseDto> Responses);

/// <summary>#208: the save. Every criterion's grid and anchors travel together, so a rubric cannot half-exist.</summary>
public sealed record SaveRubricRequest(int CertId, int SkillId, IReadOnlyList<RubricCriterionDto> Criteria);

/// <summary>
/// #208: the ladder outcome as the schema's <c>CK_GRL_LadderOutcome</c> spells it, in the layer both the
/// service and the screen can see. One vocabulary: the trigger reads these strings, the service writes
/// them and the page paints them, so a code added on one side cannot silently miss the others.
/// </summary>
public static class RubricLadderOutcomes
{
    public const string Accepted = "accepted";
    public const string NotMonotonic = "not_monotonic";
    public const string Separation = "separation";
    public const string TrapCeiling = "trap_ceiling";
    public const string NotPlotted = "not_plotted";

    /// <summary>
    /// What the author reads for a verdict. Three distinct sentences for the three failures, because
    /// they are three different rewrites - move a descriptor, widen it, cap the length reward - and the
    /// lock trigger has one sentence per code for the same reason.
    /// </summary>
    public static string Label(string outcome) => outcome switch
    {
        Accepted => "Calibrated: every adjacent band separated and the trap stayed under its ceiling.",
        NotMonotonic => "Not monotonic: a higher band scored no better than the one below it, so that segment is flat.",
        Separation => "Bands too close: adjacent bands are nearer than the ladder's minimum separation.",
        TrapCeiling => "Verbose trap above its ceiling: the rubric rewards length it should not reward.",
        _ => "Not plotted: the ladder has not run against this rubric, so nothing is known about its separation.",
    };

    /// <summary>The gate four rule code behind a verdict, or null when there is no failure to name.</summary>
    public static string? RuleCode(string outcome) => outcome switch
    {
        NotMonotonic => "gate4.ladder.monotonic",
        Separation => "gate4.ladder.separation",
        TrapCeiling => "gate4.ladder.trap",
        NotPlotted => "gate4.ladder.not_plotted",
        _ => null,
    };

    /// <summary>
    /// A code the schema's CHECK accepts, or <see cref="NotPlotted"/>. A lock row written through a path
    /// that dropped the outcome must not lock, and "unknown" is a state nobody calibrated.
    /// </summary>
    public static string Normalize(string? outcome) => outcome switch
    {
        Accepted => Accepted,
        NotMonotonic => NotMonotonic,
        Separation => Separation,
        TrapCeiling => TrapCeiling,
        _ => NotPlotted,
    };
}

/// <summary>
/// #208: the bands a rubric's cells are keyed by, in the layer the screen can see.

/// <para>
/// <b>A rubric band is not a certificate's score band.</b> The integer 0-6 here is the CEFR ladder the
/// rubric's descriptors are written on (D0.6), and it happens to spell the same letters A1..C2. A
/// certificate's <c>ScoreBands</c> row is a scored RANGE (TOEIC 785-940) with its own code and no
/// crosswalk to any ability level (PRN-03, AGENTS.md §9). Nothing on this screen reads a ScoreBand.
/// </para>
/// </summary>
public static class RubricBands
{
    /// <summary>0-6, the seven a criterion must describe before its rubric can lock.</summary>
    public static IReadOnlyList<byte> All { get; } = [0, 1, 2, 3, 4, 5, 6];

    /// <summary>The three that carry a calibrated benchmark response (D0.6 mechanism 5).</summary>
    public static IReadOnlyList<byte> AnchorBands { get; } = [2, 3, 4];

    /// <summary>
    /// What a band reads as on the grid's header. Band 0 is the non-scorable floor, not "A0": the
    /// schema's comment says Pre-A1 and the product has no A0.
    /// </summary>
    public static string Label(byte band) => band switch
    {
        0 => "Pre-A1",
        1 => "A1",
        2 => "A2",
        3 => "B1",
        4 => "B2",
        5 => "C1",
        _ => "C2",
    };
}
