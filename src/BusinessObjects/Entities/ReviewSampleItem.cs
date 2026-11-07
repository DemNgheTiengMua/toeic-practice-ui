using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #206 / W-01: one drawn item of a stratum, and the two facts the audit is made of - the reviewer's
/// blind solve and the verdict on it.
/// <para>
/// <b>The order is the rule.</b> <see cref="SolvedAt"/> is recorded before <see cref="KeyRevealedAt"/>
/// can be set, and the key is returned by exactly one read that refuses until a solve exists. Storing
/// both timestamps rather than a boolean is what makes "the key was visible when they solved"
/// answerable after the fact: the screen's enforcement is an ordering, and an ordering is only
/// checkable if both ends are written down.
/// </para>
/// <para>
/// <see cref="ItemVersionId"/> is pinned because a new version is a new key. A verdict about version 1
/// says nothing about version 2, and a sample that followed the newest version would judge a key
/// nobody solved.
/// </para>
/// </summary>
public partial class ReviewSampleItem
{
    [Key]
    public int SampleItemId { get; set; }

    public int StratumId { get; set; }

    /// <summary>Carried so the composite foreign key pins this item to its stratum's own batch.</summary>
    public int BatchId { get; set; }

    public int ItemId { get; set; }

    public int ItemVersionId { get; set; }

    /// <summary>The reviewer's own answer, A-D, recorded before the key is read.</summary>
    [StringLength(1)]
    [Unicode(false)]
    public string? SolvedLabel { get; set; }

    public DateTimeOffset? SolvedAt { get; set; }

    public int? SolvedBy { get; set; }

    /// <summary>When the authored key was returned. Set by the key read, which refuses until a solve exists.</summary>
    public DateTimeOffset? KeyRevealedAt { get; set; }

    /// <summary><c>correct</c>, <c>key_error</c> or <c>non_key_defect</c>.</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string? Verdict { get; set; }

    public DateTimeOffset? VerdictAt { get; set; }

    public int? VerdictBy { get; set; }

    [ForeignKey("StratumId")]
    [InverseProperty("SampleItems")]
    public virtual ReviewStratum Stratum { get; set; } = null!;

    [ForeignKey("ItemId")]
    [InverseProperty("ReviewSampleItems")]
    public virtual Item Item { get; set; } = null!;

    [ForeignKey("ItemVersionId")]
    [InverseProperty("ReviewSampleItems")]
    public virtual ItemVersion ItemVersion { get; set; } = null!;
}
