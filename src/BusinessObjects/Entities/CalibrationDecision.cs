using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #156: what the learner answered when the system told them their practice accuracy does not match
/// their placement band. The row exists only because the learner chose — a <c>deferred</c> row is the
/// record of "leave my path alone", so the proposal is not shown again on the same evidence.
/// </summary>
[Index("UserId", "CertId", "DecidedAt", Name = "IX_Calibration_User")]
public partial class CalibrationDecision
{
    [Key]
    public int DecisionId { get; set; }

    public int UserId { get; set; }

    public int CertId { get; set; }

    /// <summary>The band the placement (or last graded mock) put the learner in.</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string PlacedBandCode { get; set; } = null!;

    /// <summary>The band the evidence supports. Always below <see cref="PlacedBandCode"/>.</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string ProposedBandCode { get; set; } = null!;

    /// <summary>Valid answers behind the proposal — three-second guesses already excluded.</summary>
    public int SampleSize { get; set; }

    [Column(TypeName = "decimal(5, 2)")]
    public decimal ObservedAccuracyPct { get; set; }

    /// <summary>"deferred" (path untouched) or "adjusted" (the path was lowered).</summary>
    [StringLength(10)]
    [Unicode(false)]
    public string Decision { get; set; } = null!;

    public DateTimeOffset DecidedAt { get; set; }

    [ForeignKey("CertId, PlacedBandCode")]
    [InverseProperty("CalibrationPlacedDecisions")]
    public virtual ScoreBand PlacedBand { get; set; } = null!;

    [ForeignKey("CertId, ProposedBandCode")]
    [InverseProperty("CalibrationProposedDecisions")]
    public virtual ScoreBand ProposedBand { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("CalibrationDecisions")]
    public virtual User User { get; set; } = null!;
}

/// <summary>#156: the two things a learner can say to a calibration proposal.</summary>
public static class CalibrationDecisionKind
{
    public const string Deferred = "deferred";
    public const string Adjusted = "adjusted";
}
