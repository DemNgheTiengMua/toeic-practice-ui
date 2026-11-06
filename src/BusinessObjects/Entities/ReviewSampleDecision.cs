using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #201 / ADM-02: one decision a staff account made about a review sample. The row exists because the
/// no-self-approval rule is about WHO decided - a rule that only stored "this batch was approved"
/// could not tell an honest approval from a self-approval.
/// <para>
/// <c>trg_ReviewSampleDecisions_NoSelfApproval</c> refuses a row whose actor uploaded or last modified
/// the batch, or whose actor wrote the item's newest version. It also refuses DELETE outright: a
/// decision is the record that an approval happened, and deleting it is the cheapest way to re-make it
/// by another hand.
/// </para>
/// <para>
/// #206 owns the stratified sample, the blind solve and the quarantine. This row is only the decision
/// and the account, because that is the fact ADM-02 reads.
/// </para>
/// </summary>
[Index("BatchId", "ItemId", Name = "IX_RSD_Batch_Item")]
public partial class ReviewSampleDecision
{
    [Key]
    public int DecisionId { get; set; }

    public int BatchId { get; set; }

    public int ItemId { get; set; }

    public int ActorUserId { get; set; }

    /// <summary><c>claim</c>, <c>adjudicate</c> or <c>clear</c> - ADM-02's own three verbs.</summary>
    [StringLength(12)]
    [Unicode(false)]
    public string Decision { get; set; } = null!;

    public DateTimeOffset DecidedAt { get; set; }
}
