using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("UserId", "CreatedAt", Name = "IX_Orders_User")]
[Index("OrderId", "UserId", Name = "UQ_Orders_Id_User", IsUnique = true)]
[Index("OrderCode", Name = "UQ_Orders_OrderCode", IsUnique = true)]
[Index("PaymentNo", Name = "UQ_Orders_PaymentNo", IsUnique = true)]
public partial class Order
{
    [Key]
    public int OrderId { get; set; }

    public int UserId { get; set; }

    public int? PackageId { get; set; }

    public int Credits { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column(TypeName = "decimal(12, 2)")]
    public decimal Amount { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string OrderCode { get; set; } = null!;

    public long PaymentNo { get; set; }

    [StringLength(8)]
    [Unicode(false)]
    public string Gateway { get; set; } = null!;

    [StringLength(64)]
    [Unicode(false)]
    public string? GatewayTxnRef { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string? GatewayErrorCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    [InverseProperty("Order")]
    public virtual ICollection<Complaint> Complaints { get; set; } = new List<Complaint>();

    [InverseProperty("Order")]
    public virtual ICollection<CreditTransaction> CreditTransactions { get; set; } = new List<CreditTransaction>();

    [ForeignKey("PackageId")]
    [InverseProperty("Orders")]
    public virtual Package? Package { get; set; }

    [InverseProperty("Order")]
    public virtual ICollection<PaymentEvent> PaymentEvents { get; set; } = new List<PaymentEvent>();

    /// <summary>#280: refunds recorded against this order - the order link the ledger cannot carry.</summary>
    [InverseProperty("Order")]
    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();

    [ForeignKey("UserId")]
    [InverseProperty("Order")]
    public virtual User User { get; set; } = null!;
}
