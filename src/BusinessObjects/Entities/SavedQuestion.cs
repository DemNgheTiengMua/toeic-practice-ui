using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #224: one bookmark, in one of two shapes, and it pins what the learner saw.
/// <para>
/// A pool link names the <see cref="ItemId"/> and the exact <see cref="ItemVersionId"/> that was on
/// screen when it was saved, so reopening it renders that version even after the item is corrected,
/// retired, or withdrawn as defective (D21). A legacy link names its <see cref="QuestionId"/>, because
/// the exam-owned row it points at still exists until #231; <c>CK_SavedQuestions_Shape</c> refuses a
/// row that is half of either. D23 drops legacy links rather than re-pointing them.
/// </para>
/// <para>
/// <see cref="CertId"/> is the certificate the link was saved under. Items are certificate-agnostic
/// (D2), but a bookmark is learner state and the bank is read per active certificate — the same
/// arrangement a pool <c>PracticeSession</c> already uses.
/// </para>
/// </summary>
public partial class SavedQuestion
{
    /// <summary>
    /// The row's own identity, and the address the bank's review and remove routes use: a question id
    /// and an item-version id are two counters that can collide, so neither addresses a row alone.
    /// </summary>
    [Key]
    public int SavedQuestionId { get; set; }

    public int UserId { get; set; }

    /// <summary>NULL for a pool link. Retired with <c>Questions</c> at #231.</summary>
    public int? QuestionId { get; set; }

    /// <summary>#224: the pool item saved. NULL for a legacy link.</summary>
    public int? ItemId { get; set; }

    /// <summary>#224: the version rendered when the link was saved. NULL for a legacy link.</summary>
    public int? ItemVersionId { get; set; }

    /// <summary>
    /// #224: which certificate's bank this link lives in. Required for a pool link, because an item is
    /// certificate-agnostic (D2) and has no section to climb to one; NULL for a legacy link, whose
    /// question already carries a certificate through its section.
    /// </summary>
    public int? CertId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Source { get; set; } = null!;

    public DateTimeOffset SavedAt { get; set; }

    [ForeignKey("QuestionId")]
    [InverseProperty("SavedQuestions")]
    public virtual Question? Question { get; set; }

    [ForeignKey("ItemVersionId, ItemId")]
    [InverseProperty("SavedQuestions")]
    public virtual ItemVersion? ItemVersion { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("SavedQuestions")]
    public virtual Certificate? Cert { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("SavedQuestions")]
    public virtual User User { get; set; } = null!;
}
