using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #206 / S-03: one cell of one batch, audited as a unit.
/// <para>
/// A stratum is <c>(BatchId, TopicId, CefrLevel)</c> - the cell <c>IX_Items_Cell</c> indexes, scoped to
/// the upload it arrived in. The scope is the batch rather than the cell globally because a cell spans
/// every file that ever filled it: a decision taken over stock nobody in this file uploaded is a
/// decision nobody made. It also makes "one stratum never straddles two certificate banks" structural
/// rather than a rule to remember - a batch names at most one certificate.
/// </para>
/// <para>
/// <b>Why the sample is stratified and not random.</b> A random 10% of a batch can miss a C1 or C2 cell
/// entirely, because those cells hold few items by construction. So the draw takes a floor of one item
/// from every cell first and spreads the rest by size, which is what makes "an edge band cannot be
/// missed" a property of the query instead of a hope.
/// </para>
/// <para>
/// <b>Promotion is the whole stratum.</b> Approving promotes the sampled 10% <em>and</em> the
/// unsampled remainder: the sample is how the batch is judged, not the set that gets published.
/// Publishing only what was reviewed stalls the pool at 10% of target volume.
/// </para>
/// </summary>
public partial class ReviewStratum
{
    [Key]
    public int StratumId { get; set; }

    public int BatchId { get; set; }

    public int TopicId { get; set; }

    public CefrLevel CefrLevel { get; set; }

    /// <summary>How many staged items the cell held when the sample was drawn. Frozen: a later record does not enlarge it.</summary>
    public int ItemCount { get; set; }

    /// <summary>The number drawn. At least one, always - a floor of zero loses a one-item C1 or C2 cell.</summary>
    public int SampleSize { get; set; }

    /// <summary><c>pending</c>, <c>approved</c> or <c>quarantined</c>.</summary>
    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = "pending";

    /// <summary><c>key_error</c> or <c>non_key_defect_rate</c>; null unless the stratum is quarantined.</summary>
    [StringLength(24)]
    [Unicode(false)]
    public string? QuarantineReason { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>
    /// #191 / D14: the decision is derived from the sample verdicts, so exactly one service writes it.
    /// NULL while <see cref="Status"/> is <c>pending</c>, because drawing a sample derives nothing.
    /// </summary>
    [StringLength(64)]
    [Unicode(false)]
    public string? WrittenByService { get; set; }

    public int ProjectionVersion { get; set; }

    [ForeignKey("BatchId")]
    [InverseProperty("Strata")]
    public virtual ImportBatch Batch { get; set; } = null!;

    [ForeignKey("TopicId")]
    [InverseProperty("ReviewStrata")]
    public virtual Topic Topic { get; set; } = null!;

    [InverseProperty("Stratum")]
    public virtual ICollection<ReviewSampleItem> SampleItems { get; set; } = new List<ReviewSampleItem>();
}
