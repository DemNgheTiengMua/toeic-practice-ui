using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #212 / PLC-33: one skill's result in one placement sitting, derived from the attempt by exactly one
/// writer (<see cref="DerivedFactWriters.PlacementSkillResult"/>).
/// <para>
/// <b>The status is the whole point.</b> A nullable <see cref="CefrLevel"/> alone could not distinguish
/// "skipped", "timed out" and "still grading"; <see cref="ResultStatus"/> does, and
/// <c>CK_PSR_StatusCarriesItsShape</c> ties the measurement columns to it so a row cannot be half of
/// two outcomes at once.
/// </para>
/// <para>
/// <b>The cut-score version is stored beside the projection, not looked up at read.</b> #222 had to
/// resolve a version at read time because <c>ExamAttempts</c> carries none, and recorded that as a
/// reproducibility risk. LVL-13 asks a recorded projection to name the table it came from, so
/// <see cref="ProjectedScore"/>, <see cref="ProjectedBandCode"/> and
/// <see cref="CutScoreTableVersion"/> are a triple or nothing.
/// </para>
/// <para>
/// <b>There is no overall here.</b> It is MIN over the measured rows, computed at read (D15). A stored
/// overall would be a second writer of a fact the rows already state.
/// </para>
/// </summary>
public partial class PlacementSkillResult
{
    public int PlacementId { get; set; }

    public int SkillId { get; set; }

    /// <summary>Carried down from the placement, so the skill must belong to that same certificate.</summary>
    public int CertId { get; set; }

    /// <summary>Which of the five outcomes this skill had. The grace note the null level never could be.</summary>
    [MaxLength(24)]
    [Unicode(false)]
    public PlacementResultStatus ResultStatus { get; set; }

    /// <summary>Null unless the status is a measurement. See <see cref="ResultStatus"/>.</summary>
    [MaxLength(10)]
    public CefrLevel? CefrLevel { get; set; }

    /// <summary>0-100 inside the band; null unless measured.</summary>
    public int? PositionInBand { get; set; }

    /// <summary>The measured axis coordinate the level and position were projected from: 1.00 to 6.99.</summary>
    [Column(TypeName = "decimal(3, 2)")]
    public decimal? ContinuousCoordinate { get; set; }

    /// <summary>The certificate's own band for this skill's coordinate, read from the cut-score table.</summary>
    [MaxLength(20)]
    [Unicode(false)]
    public string? ProjectedBandCode { get; set; }

    /// <summary>The score on the certificate's scale, read from the cut-score table. Never computed here.</summary>
    public int? ProjectedScore { get; set; }

    /// <summary>WHICH cut-score version produced the two values above. Stored, because LVL-13 requires it.</summary>
    public int? CutScoreTableVersion { get; set; }

    /// <summary>
    /// PLC-35: one graded task, not two. Deliberately redundant with
    /// <see cref="PlacementResultStatus.EstimatedFromOneTask"/> — the CHECK pins them to each other so
    /// they cannot disagree, and a screen reading a bool does not have to know the enum's values.
    /// </summary>
    public bool IsEstimatedFromOneTask { get; set; }

    /// <summary>PLC-28: the awarded level was truncated to the highest contiguous passing run.</summary>
    public bool IsTruncated { get; set; }

    /// <summary>
    /// #213 / D18: this row was carried over from an earlier sitting rather than measured in this one.
    /// <see cref="MeasuredAt"/> and <see cref="ExpiresAt"/> are the EARLIER measurement's, copied
    /// verbatim - a reused result is not re-stamped, or the badge would describe today instead of the
    /// day the learner actually sat the skill.
    /// </summary>
    public bool ReusedFromPrior { get; set; }

    /// <summary>One of <see cref="PlacementFailureReason"/>, exactly when the status is not_measured.</summary>
    [MaxLength(24)]
    [Unicode(false)]
    public string? FailureReason { get; set; }

    /// <summary>
    /// #219 / D5: what this sitting SAW per topic, as JSON ΓÇö <c>topicId, evaluatedBand, itemsSeen,
    /// itemsCorrect, routingSignal</c> (<c>weak</c> or <c>on_level</c>).
    /// <para>
    /// <b>This is a sequencing hint, never mastery.</b> A topic touched by one to three items cannot
    /// support a mastery judgement, which needs twelve; the whole point of keeping it in its own column
    /// on its own row is that the mastery path reads none of it. Null when the sitting saw nothing on
    /// this skill ΓÇö an empty array is refused, because it claims topics while naming none.
    /// </para>
    /// </summary>
    public string? TopicPriorsJson { get; set; }

    /// <summary>
    /// #219 / D5: the productive half's rubric profile ΓÇö the criterion scores and the weakest one, so
    /// the path has a starting point without recomputing it. Null for an objective skill.
    /// </summary>
    public string? CriterionProfileJson { get; set; }

    public DateTimeOffset? MeasuredAt { get; set; }

    /// <summary>Always <see cref="MeasuredAt"/> + 90 days (D18), pinned by CHECK.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>#191: which service declared this write; required by the trigger, not by the column.</summary>
    [MaxLength(64)]
    [Unicode(false)]
    public string? WrittenByService { get; set; }

    /// <summary>#191: monotonic ordering token, so a stale callback cannot overwrite a newer result.</summary>
    public int ProjectionVersion { get; set; }

    [ForeignKey("PlacementId, CertId")]
    [InverseProperty("SkillResults")]
    public virtual PlacementTest Placement { get; set; } = null!;

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("PlacementSkillResults")]
    public virtual Skill SkillNavigation { get; set; } = null!;
}
