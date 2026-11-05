using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #184: everything that can be scored - stem, four options, key, explanation, evidence span and the
/// reference a spoken answer is judged against. Copy-on-write (CNT-43): a scored change inserts a new
/// row, so an attempt that pinned version 3 renders version 3 forever even after the item is corrected
/// or retired.
/// <para>
/// Only <see cref="Explanation"/> may be edited in place, and only for a typo. Every other column is
/// immovable after insert - enforced by <c>trg_ItemVersions_ScoredFieldsImmutable</c>, because a
/// convention in a service does not survive its second caller. That trigger is declared in
/// <c>CompanionDbContext.Authoring.cs</c> via <c>HasTrigger</c>; a table with a trigger and no
/// declaration fails every save on the OUTPUT clause.
/// </para>
/// </summary>
[Index("ItemId", "VersionNumber", Name = "UQ_ItemVersions_Item_Number", IsUnique = true)]
public partial class ItemVersion
{
    [Key]
    public int ItemVersionId { get; set; }

    public int ItemId { get; set; }

    public int VersionNumber { get; set; }

    public string Stem { get; set; } = null!;

    public string? OptionA { get; set; }

    public string? OptionB { get; set; }

    public string? OptionC { get; set; }

    public string? OptionD { get; set; }

    /// <summary>A-D, or NULL for a productive task that has no options at all.</summary>
    [StringLength(1)]
    [Unicode(false)]
    public string? KeyLabel { get; set; }

    /// <summary>The one field a typo fix may change in place.</summary>
    public string? Explanation { get; set; }

    public int? EvidenceStart { get; set; }

    public int? EvidenceEnd { get; set; }

    /// <summary><c>char</c> for text, <c>sec</c> for audio.</summary>
    [StringLength(8)]
    [Unicode(false)]
    public string? EvidenceUnit { get; set; }

    /// <summary>The verbatim words that answer the item, so review can jump to them.</summary>
    public string? EvidenceQuote { get; set; }

    [StringLength(1000)]
    public string? ReferenceText { get; set; }

    /// <summary><c>script</c> (read aloud) or <c>image_desc</c> (describes the picture).</summary>
    [StringLength(12)]
    [Unicode(false)]
    public string? ReferenceKind { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("ItemId")]
    [InverseProperty("ItemVersions")]
    public virtual Item Item { get; set; } = null!;
}
