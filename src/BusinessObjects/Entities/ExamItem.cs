using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #185: the assembly binding. One row per item an exam puts on a paper: which pinned version, in
/// which section, at which position, in which unit. This is the only relationship between a paper
/// and the warehouse — an exam does not own items, it names versions of them.
/// <para>
/// <see cref="CertId"/> and <see cref="Modality"/> are carried here on purpose, and both hold one
/// value that two composite foreign keys must agree on: the exam's certificate and the section's
/// must be the same, and the section's skill and the item's must be the same. That is why
/// certificate isolation and the skill rule cost no trigger — a mismatched pair has no target row.
/// </para>
/// <para>
/// The binding points at <see cref="ItemVersionId"/>, never at the stem. Copy-on-write (CNT-43)
/// means a re-versioned item leaves this row, and therefore a published paper's rendering,
/// untouched.
/// </para>
/// <para>
/// INV-6 (a production exam binds no demo-tier item) is the one rule no foreign key can express, and
/// <c>trg_ExamItems_ProductionBindsProduction</c> is its first enforcement anywhere. It is declared
/// below via <c>HasTrigger</c>; a table with a trigger and no declaration fails every save on the
/// OUTPUT clause.
/// </para>
/// </summary>
[Index("ExamId", "SectionId", "DisplayOrder", Name = "UQ_ExamItems_Exam_Section_Order", IsUnique = true)]
public partial class ExamItem
{
    [Key]
    public int ExamItemId { get; set; }

    public int ExamId { get; set; }

    public int SectionId { get; set; }

    public int CertId { get; set; }

    /// <summary>The pinned version. A later version of the same item does not change this row.</summary>
    public int ItemVersionId { get; set; }

    /// <summary>Carried down so the version can be pinned to this item by a composite key.</summary>
    public int ItemId { get; set; }

    /// <summary>The section's skill. Pinned to both the section's and the item's own by foreign key.</summary>
    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    /// <summary>The paper's position, 1-based within its section.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>D9: which unit of a multistage sitting addresses this item.</summary>
    public int UnitNumber { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("ExamId, CertId")]
    [InverseProperty("ExamItems")]
    public virtual Exam Exam { get; set; } = null!;

    [ForeignKey("SectionId, CertId")]
    [InverseProperty("ExamItems")]
    public virtual Section Section { get; set; } = null!;

    [ForeignKey("ItemId, Modality")]
    [InverseProperty("ExamItems")]
    public virtual Item Item { get; set; } = null!;

    [ForeignKey("ItemVersionId, ItemId")]
    [InverseProperty("ExamItems")]
    public virtual ItemVersion ItemVersion { get; set; } = null!;

    /// <summary>
    /// #201 / ADM-03: exam assembly is structure, so triage cannot write it. NULL = seed, fixture.
    /// </summary>
    public int? LastWrittenBy { get; set; }
}
