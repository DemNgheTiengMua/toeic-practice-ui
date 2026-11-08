using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #184: one assessment unit, pool-owned and addressed by skill, level and topic. It carries no
/// ExamId, no SectionId and no CertId - an item is not written "for TOEIC". A certificate declares
/// which skills it examines and draws from those skills' pools.
/// <para>
/// The skill axis is <see cref="Modality"/> rather than a <c>SkillId</c>: CONTEXT.md defines a Skill
/// as exactly one modality, and the certificate-scoped <c>Skills</c> table cannot be referenced from a
/// table that has to exist with no certificate. A composite foreign key pins the item's modality to
/// its topic's own, so a listening item cannot sit on a reading topic.
/// </para>
/// <para>
/// Everything scorable lives in <see cref="ItemVersion"/>, which is copy-on-write. This row is the
/// stable identity a past attempt can point at.
/// </para>
/// </summary>
public partial class Item
{
    [Key]
    public int ItemId { get; set; }

    /// <summary>NULL is "authored before its stimulus is attached".</summary>
    public int? StimulusId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    public int TopicId { get; set; }

    /// <summary>Authored, so the reportable floor (<c>pre_a1</c>) is refused.</summary>
    public CefrLevel CefrLevel { get; set; }

    /// <summary>One of the fourteen D0.4 types, constrained to this item's own modality.</summary>
    [StringLength(40)]
    [Unicode(false)]
    public string ItemType { get; set; } = null!;

    /// <summary>Whether placement may draw this item. Placement-eligible stock is a third of the pool.</summary>
    public bool PlacementEligible { get; set; }

    /// <summary>Production or demo. Demo defaults, because an unreviewed item must not reach a paid paper.</summary>
    [StringLength(12)]
    [Unicode(false)]
    public string ContentTier { get; set; } = "demo";

    /// <summary>draft, validated, active, quarantined, retired, retired_defective.</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string LifecycleStatus { get; set; } = "draft";

    /// <summary>The upload batch this item arrived in; NULL for hand-authored items.</summary>
    [StringLength(60)]
    public string? BatchTag { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// #201 / ADM-03 / #255: the lifecycle is guarded, so a write to this table declares who made it.
    /// NULL means a seed, a fixture or a migration, which names no account. Stamped by
    /// <c>StaffAccountStampInterceptor</c>.
    /// <para>
    /// #255: this column was in the schema and in the interceptor's guarded-type list but had no
    /// entity property, so the stamp threw for every <see cref="Item"/> write that had an acting
    /// account, and <c>trg_Items_TriageCannotWrite</c> refused the promotion on a strict catalog for
    /// declaring nobody. The lifecycle transition depends on this property existing.
    /// </para>
    /// </summary>
    public int? LastWrittenBy { get; set; }

    [ForeignKey("StimulusId")]
    [InverseProperty("Items")]
    public virtual Stimulus? Stimulus { get; set; }

    [ForeignKey("TopicId")]
    [InverseProperty("Items")]
    public virtual Topic Topic { get; set; } = null!;

    [InverseProperty("Item")]
    public virtual ICollection<ItemVersion> ItemVersions { get; set; } = new List<ItemVersion>();

    /// <summary>#185: the papers that bind this item. A binding pins a version, not the item.</summary>
    [InverseProperty("Item")]
    public virtual ICollection<ExamItem> ExamItems { get; set; } = new List<ExamItem>();

    /// <summary>#206: the audit samples that drew this item.</summary>
    [InverseProperty("Item")]
    public virtual ICollection<ReviewSampleItem> ReviewSampleItems { get; set; } = new List<ReviewSampleItem>();
}
