using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #224 / #269: one bookmark, and it pins what the learner saw.
/// <para>
/// A link names the <see cref="ItemId"/> and the exact <see cref="ItemVersionId"/> that was on screen
/// when it was saved, so reopening it renders that version even after the item is corrected, retired,
/// or withdrawn as defective (D21).
/// </para>
/// <para>
/// <see cref="CertId"/> is the certificate the link was saved under. Items are certificate-agnostic
/// (D2), but a bookmark is learner state and the bank is read per active certificate - the same
/// arrangement a pool <see cref="Entities.PracticeSession"/> already uses.
/// </para>
/// </summary>
public partial class SavedQuestion
{
    /// <summary>
    /// The row's own identity, and the address the bank's review and remove routes use: an item id and
    /// an item-version id are two counters that can collide, so neither addresses a row alone.
    /// </summary>
    [Key]
    public int SavedQuestionId { get; set; }

    public int UserId { get; set; }

    /// <summary>#224: the pool item saved.</summary>
    public int ItemId { get; set; }

    /// <summary>#224: the version rendered when the link was saved.</summary>
    public int ItemVersionId { get; set; }

    /// <summary>
    /// #224: which certificate's bank this link lives in. Required, because an item is
    /// certificate-agnostic (D2) and has no section to climb to one.
    /// </summary>
    public int CertId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Source { get; set; } = null!;

    public DateTimeOffset SavedAt { get; set; }

    [ForeignKey("ItemVersionId, ItemId")]
    [InverseProperty("SavedQuestions")]
    public virtual ItemVersion ItemVersion { get; set; } = null!;

    [ForeignKey("CertId")]
    [InverseProperty("SavedQuestions")]
    public virtual Certificate Cert { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("SavedQuestions")]
    public virtual User User { get; set; } = null!;
}
