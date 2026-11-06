using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BusinessObjects.Entities;

/// <summary>
/// #199 / D0.7 / CNT-44: one item evaluated at one checkpoint of its administration count.
/// <para>
/// This row is <b>not</b> a counter. Every number on it was derived from the stored answers at the
/// moment it was written, and the schedule recomputes those answers rather than incrementing anything
/// here — so the row and a recount can never disagree, which is NFI-05's rule.
/// </para>
/// <para>
/// Its two jobs are idempotency (the key is (item, checkpoint), so evaluating the same item twice
/// cannot record the verdict twice) and evidence (a reviewer sees which condition fired and the four
/// numbers behind it). A nullable statistic means "undefined", never zero: a point-biserial
/// correlation over an item every learner got right does not exist.
/// </para>
/// </summary>
[Table("ItemTelemetryEvaluations")]
public partial class ItemTelemetryEvaluation
{
    public int ItemId { get; set; }

    /// <summary>30, 50, 100 or 250 — the sample size this row answers (CK_ItemTelemetry_Checkpoint).</summary>
    public int CheckpointSize { get; set; }

    /// <summary>The sample actually counted. It may exceed the checkpoint; it is never below 30.</summary>
    public int AdministrationCount { get; set; }

    /// <summary>How many of those administrations came from a learner at the item's own level.</summary>
    public int TargetBandCount { get; set; }

    /// <summary>Point-biserial r, or NULL when undefined.</summary>
    public decimal? Discrimination { get; set; }

    /// <summary>Target-band facility, or NULL when no target-band learner answered.</summary>
    public decimal? TargetFacility { get; set; }

    /// <summary>A productive task's mean AI band (0 = Pre-A1 … 6 = C2); NULL for an objective item.</summary>
    public decimal? MeanResponseBand { get; set; }

    public bool BreachDiscrimination { get; set; }

    public bool BreachFacility { get; set; }

    public bool BreachDistractorDominance { get; set; }

    public bool BreachProductiveDrift { get; set; }

    /// <summary>The verdict acted on. <c>CK_ItemTelemetry_Verdict</c> keeps it in step with the four bits.</summary>
    public bool Quarantined { get; set; }

    public DateTimeOffset EvaluatedAt { get; set; }

    [ForeignKey("ItemId")]
    public virtual Item Item { get; set; } = null!;
}
