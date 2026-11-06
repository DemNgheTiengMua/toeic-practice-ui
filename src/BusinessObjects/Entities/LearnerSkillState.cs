using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #211 / D15: one learner's state in one skill. The single row every screen reads to answer "what is
/// my level" — Dashboard, path header, goal feasibility, AI insight, weakness analysis, calibration and
/// mentor booking all read this, so two screens cannot disagree on the same day.
/// <para>
/// <b>There is no overall here, and its absence is the design.</b> The overall is the weakest measured
/// skill, computed at read and tethered to the skill that produced it (TRN-13 identity 2). A stored
/// overall is a second writer of one fact: it can disagree with the per-skill rows, and then the screen
/// that reads it disagrees with the screen that computes it. A stored overall cannot be kept honest.
/// </para>
/// <para>
/// <b>Expiry is stored beside the measurement, and pinned to it.</b> D18 allows a result to be reused
/// for 90 calendar days, and <see cref="ExpiresAt"/> is <see cref="MeasuredAt"/> + 90 by CHECK. Storing
/// it rather than deriving it at read means a later change to the window does not re-date a measurement
/// taken under the old rule; pinning it to the measurement means the two can never disagree.
/// </para>
/// </summary>
[Index("UserId", "CertId", Name = "IX_LSS_UserCert")]
public partial class LearnerSkillState
{
    public int UserId { get; set; }

    public int CertId { get; set; }

    public int SkillId { get; set; }

    /// <summary>A result may report the pre-A1 floor; see <see cref="CefrLevel.Reportable"/>.</summary>
    [MaxLength(10)]
    public CefrLevel CefrLevel { get; set; }

    /// <summary>0-100 inside the band. A position, never a percentage: crossing a band resets it.</summary>
    public int PositionInBand { get; set; }

    /// <summary>The measured coordinate the level and position were projected from: 1.00 (A1) to 6.99.</summary>
    [Column(TypeName = "decimal(3, 2)")]
    public decimal ContinuousCoordinate { get; set; }

    /// <summary>What measured it: <c>placement</c>, <c>mock_section</c> or <c>calibration</c>.</summary>
    [MaxLength(16)]
    [Unicode(false)]
    public string Source { get; set; } = null!;

    public DateTimeOffset MeasuredAt { get; set; }

    /// <summary>Always <see cref="MeasuredAt"/> + 90 days; D18's reuse window.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// #191: which service declared this write. Nullable for the same reason it is on the three tables
    /// that predate per-write enforcement: the trigger, not the column, is what refuses a write that
    /// declares nothing.
    /// </summary>
    [MaxLength(64)]
    [Unicode(false)]
    public string? WrittenByService { get; set; }

    /// <summary>#191: monotonic ordering token, so a stale callback cannot overwrite a newer state.</summary>
    public int ProjectionVersion { get; set; }

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("LearnerSkillStates")]
    public virtual Skill SkillNavigation { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("LearnerSkillStates")]
    public virtual User User { get; set; } = null!;
}

/// <summary>
/// #211: the three events that measure a skill. Closed, because a fourth value would be a measurement
/// nobody designed, and a typo would read as "this row came from something".
/// </summary>
public static class LearnerSkillStateSource
{
    /// <summary>A placement sitting's per-skill result.</summary>
    public const string Placement = "placement";

    /// <summary>A path-scheduled mock-section calibration sitting.</summary>
    public const string MockSection = "mock_section";

    /// <summary>A calibration adjustment the learner confirmed (#156).</summary>
    public const string Calibration = "calibration";
}
