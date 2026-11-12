using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #280: a refund is a first-class record with two methods.
/// <para>
/// The method names which column the value lives in, and the schema refuses a row that disagrees with
/// itself (<c>CK_Refunds_MethodOwnsAmount</c>): a money refund carries <see cref="Amount"/> and no
/// <see cref="CreditCount"/>, a credit refund the reverse. That is enforced in the database rather than
/// in the service because a service that forgot the second half would leave a row saying "credit" beside
/// a cash figure - a shape accounting cannot read and nobody can repair after the fact.
/// </para>
/// </summary>
[Index("Status", "CreatedAt", Name = "IX_Refunds_Status")]
[Index("UserId", Name = "IX_Refunds_User")]
[Index("RefundId", "UserId", Name = "UQ_Refunds_Id_User", IsUnique = true)]
public partial class Refund
{
    [Key]
    public int RefundId { get; set; }

    public int UserId { get; set; }

    /// <summary>Optional: a refund can compensate a failure that produced no complaint.</summary>
    public int? ComplaintId { get; set; }

    /// <summary>Optional: a refund with no order behind it is capped by a configured ceiling instead.</summary>
    public int? OrderId { get; set; }

    /// <summary><c>money</c> or <c>credit</c>. Owns which value column is set.</summary>
    [StringLength(6)]
    [Unicode(false)]
    public string Method { get; set; } = null!;

    /// <summary>Mandatory on every refund: no payment leaves without a recorded justification.</summary>
    [StringLength(400)]
    public string Reason { get; set; } = null!;

    /// <summary><c>requested</c>, <c>approved</c>, <c>settled</c> or <c>rejected</c>.</summary>
    [StringLength(10)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    /// <summary>
    /// The money a <c>money</c> refund returns. <c>DECIMAL(12,2)</c> is <c>Orders.Amount</c>'s exact type
    /// so net revenue can subtract one from the other without a conversion that rounds.
    /// </summary>
    [Column(TypeName = "decimal(12, 2)")]
    public decimal? Amount { get; set; }

    /// <summary>The credits a <c>credit</c> refund granted. Set only when <see cref="Method"/> is credit.</summary>
    public int? CreditCount { get; set; }

    public int RequestedByUserId { get; set; }

    public int? DecidedByUserId { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public int? SettledByUserId { get; set; }

    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>The <c>refund_credit</c> ledger row a credit refund posted.</summary>
    public int? CreditTxnId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("ComplaintId, UserId")]
    [InverseProperty("Refunds")]
    public virtual Complaint? Complaint { get; set; }

    [ForeignKey("CreditTxnId, UserId")]
    [InverseProperty("Refunds")]
    public virtual CreditTransaction? CreditTransaction { get; set; }

    [ForeignKey("DecidedByUserId")]
    [InverseProperty("RefundDecidedByUsers")]
    public virtual User? DecidedByUser { get; set; }

    [ForeignKey("OrderId, UserId")]
    [InverseProperty("Refunds")]
    public virtual Order? Order { get; set; }

    [ForeignKey("RequestedByUserId")]
    [InverseProperty("RefundRequestedByUsers")]
    public virtual User RequestedByUser { get; set; } = null!;

    [ForeignKey("SettledByUserId")]
    [InverseProperty("RefundSettledByUsers")]
    public virtual User? SettledByUser { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("RefundUsers")]
    public virtual User User { get; set; } = null!;
}
