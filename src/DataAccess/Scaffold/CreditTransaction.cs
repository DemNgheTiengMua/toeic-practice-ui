using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("UserId", "CreatedAt", Name = "IX_CreditTxn_User")]
[Index("TxnId", "UserId", Name = "UQ_CreditTxn_Id_User", IsUnique = true)]
public partial class CreditTransaction
{
    [Key]
    public int TxnId { get; set; }

    public int UserId { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string Reason { get; set; } = null!;

    public int Delta { get; set; }

    public int? OrderId { get; set; }

    public int? AttemptId { get; set; }

    public int? BookingId { get; set; }

    public int? LearnerPathId { get; set; }

    public int? AdminUserId { get; set; }

    [StringLength(400)]
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("AdminUserId")]
    [InverseProperty("CreditTransactionAdminUsers")]
    public virtual User? AdminUser { get; set; }

    [ForeignKey("BookingId, UserId")]
    [InverseProperty("CreditTransactions")]
    public virtual Booking? Booking { get; set; }

    [InverseProperty("CreditTransaction")]
    public virtual ICollection<Complaint> Complaints { get; set; } = new List<Complaint>();

    [ForeignKey("AttemptId, UserId")]
    [InverseProperty("CreditTransactions")]
    public virtual ExamAttempt? ExamAttempt { get; set; }

    [ForeignKey("LearnerPathId, UserId")]
    [InverseProperty("CreditTransactions")]
    public virtual LearnerPath? LearnerPath { get; set; }

    [ForeignKey("OrderId, UserId")]
    [InverseProperty("CreditTransactions")]
    public virtual Order? Order { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("CreditTransactionUsers")]
    public virtual User User { get; set; } = null!;
}
