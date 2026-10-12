using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("Status", "CreatedAt", Name = "IX_Complaints_Status")]
[Index("UserId", Name = "IX_Complaints_User")]
public partial class Complaint
{
    [Key]
    public int ComplaintId { get; set; }

    public int? BookingId { get; set; }

    public int? AttemptId { get; set; }

    public int? OrderId { get; set; }

    public int UserId { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string Type { get; set; } = null!;

    public string Reason { get; set; } = null!;

    [StringLength(10)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public string? Resolution { get; set; }

    public string? InternalNotes { get; set; }

    public int? ResolvedByUserId { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public int? ResolvedByGrantTxnId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("BookingId, UserId")]
    [InverseProperty("Complaints")]
    public virtual Booking? Booking { get; set; }

    [ForeignKey("ResolvedByGrantTxnId, UserId")]
    [InverseProperty("Complaints")]
    public virtual CreditTransaction? CreditTransaction { get; set; }

    [ForeignKey("AttemptId, UserId")]
    [InverseProperty("Complaints")]
    public virtual ExamAttempt? ExamAttempt { get; set; }

    [ForeignKey("OrderId, UserId")]
    [InverseProperty("Complaints")]
    public virtual Order? Order { get; set; }

    [ForeignKey("ResolvedByUserId")]
    [InverseProperty("ComplaintResolvedByUsers")]
    public virtual User? ResolvedByUser { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("ComplaintUsers")]
    public virtual User User { get; set; } = null!;
}
