using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #241 / D21a block 3: per-skill standing as a table whose COLUMN COUNT is the certificate's skill
/// count. Four skills is four columns; two skills is two columns and never a four-column grid with
/// two empty cells.
/// <para>
/// <b>The shape is a table, not a card grid.</b> A card grid with <c>auto-fit</c> puts two cards in
/// a four-track template and looks complete while being wrong; a table's header is the certificate's
/// own list, so a missing skill is a missing column and nothing is left to fill.
/// </para>
/// <para>
/// <b>Absence is a sentence.</b> <see cref="SkillColumn"/> carries the level and position when
/// something measured it, and <see cref="SkillColumn.State"/> says "not measured" when nothing did.
/// It never falls back to A1, and it is never a grey dash: #211 and LRN-18 both refuse a fabricated
/// bottom band, and grey reads as the learner's fault.
/// </para>
/// </summary>
public sealed class SkillStandingTable
{
    private SkillStandingTable(IReadOnlyList<SkillColumn> columns)
    {
        Columns = columns;
    }

    public IReadOnlyList<SkillColumn> Columns { get; }

    /// <summary>The certificate's own skill count - the column count, not a fixed four.</summary>
    public int ColumnCount => Columns.Count;

    public bool IsEmpty => Columns.Count == 0;

    public static SkillStandingTable From(IReadOnlyList<SkillCardDto> skills) =>
        new(skills.Select(SkillColumn.From).ToList());
}

/// <summary>One examined skill, as one column of block 3.</summary>
public sealed class SkillColumn
{
    private SkillColumn(
        int skillId, string code, string name, string? level, int? position,
        DateTimeOffset? measuredAt, bool isStale, bool isMeasured)
    {
        SkillId = skillId;
        Code = code;
        Name = name;
        Level = level;
        Position = position;
        MeasuredAt = measuredAt;
        IsStale = isStale;
        IsMeasured = isMeasured;
    }

    public int SkillId { get; }
    public string Code { get; }
    public string Name { get; }

    /// <summary>"B2", or null when this skill has no measurement.</summary>
    public string? Level { get; }

    /// <summary>0-100 inside <see cref="Level"/>. A position, never a percentage.</summary>
    public int? Position { get; }

    public DateTimeOffset? MeasuredAt { get; }
    public bool IsStale { get; }

    public bool IsMeasured { get; }

    /// <summary>
    /// The state, in words. Three different absences are three different sentences elsewhere in the
    /// product; here there is only one, because a skill the certificate does not examine has no column
    /// at all - so the only thing this cell can mean is "nothing has measured this skill yet".
    /// </summary>
    public string State => IsMeasured ? (IsStale ? "measured, needs re-measuring" : "measured") : "not measured yet";

    /// <summary>The position as a position: "78/100", never "78%". A bare percentage reads as collapse
    /// when a band is crossed, because the number legitimately resets downward (LRN-19).</summary>
    public string PositionText => Position is { } value ? $"{value}/100" : "—";

    /// <summary>The measurement date in the learner's face, so an old result cannot masquerade as
    /// today's. Formatting is the view's job; this is the raw instant.</summary>
    public bool HasMeasurementDate => MeasuredAt is not null;

    /// <summary>The row's marker class. A measured row is plain; an unmeasured one is attention, not
    /// refusal and not grey.</summary>
    public string RowClass => IsMeasured ? "skill-standing" : "skill-standing skill-standing--unmeasured";

    public static SkillColumn From(SkillCardDto skill) =>
        new(skill.SkillId, skill.Code, skill.Name,
            // The API sends the level code as #211 stored it, or null. Nothing here derives one.
            skill.LevelCode,
            skill.PositionInBand,
            skill.MeasuredAt,
            skill.IsStale,
            isMeasured: skill.LevelCode is not null);
}
