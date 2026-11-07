using System;

namespace BusinessObjects;

/// <summary>
/// #213 / story 55: one skill of a finished placement, as the result screen shows it.
/// <para>
/// <b>This is the per-skill grain, not a page-level flag.</b> A sitting can measure one skill and carry
/// another over, so "some of this result is reused" is the wrong statement to make: the learner has to
/// know WHICH number is old. <see cref="ReusedFromPrior"/> and <see cref="MeasuredAt"/> therefore belong
/// to the row, beside the level they describe.
/// </para>
/// <para>
/// <b><see cref="MeasuredAt"/> is the stored measurement, not the sitting's date.</b> For a carried
/// skill it is the earlier sitting's date, copied by the writer and never re-stamped; the page shows
/// the two dates with different words so a reader cannot confuse them.
/// </para>
/// </summary>
/// <param name="SkillId">The certificate's skill.</param>
/// <param name="SkillCode">Its code, so a sign can name it without a second lookup.</param>
/// <param name="SkillName">Its display name.</param>
/// <param name="Status">Which of the five outcomes this skill had.</param>
/// <param name="Level">The measured level; null unless the status is a measurement.</param>
/// <param name="PositionInBand">0-100 inside that level. A position, never a percentage.</param>
/// <param name="MeasuredAt">When this result was measured — the original sitting, for a carried skill.</param>
/// <param name="ReusedFromPrior">True when this result was carried over rather than measured here.</param>
/// <param name="FailureReason">Why it was not measured, when that is the status.</param>
public sealed record PlacementSkillResultInfo(
    int SkillId,
    string SkillCode,
    string SkillName,
    PlacementResultStatus Status,
    CefrLevel? Level,
    int? PositionInBand,
    DateTimeOffset? MeasuredAt,
    bool ReusedFromPrior,
    string? FailureReason)
{
    /// <summary>One of the two statuses that count as a measurement (PLC-19, PLC-33).</summary>
    public bool IsMeasured =>
        Status is PlacementResultStatus.Measured or PlacementResultStatus.EstimatedFromOneTask;

    /// <summary>`B2 (78/100)` — a level and a position, never a band and never a bare percentage.</summary>
    public string LevelAndPosition =>
        Level is { } level && PositionInBand is { } position
            ? $"{level.Code} ({position}/100)"
            : "Not measured";
}
