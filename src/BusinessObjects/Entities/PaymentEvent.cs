using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("OrderId", "CreatedAt", Name = "IX_PaymentEvents_Order", IsDescending = new[] { false, true })]
public partial class PaymentEvent
{
    [Key]
    public int EventId { get; set; }

    public int OrderId { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string Source { get; set; } = null!;

    [StringLength(12)]
    [Unicode(false)]
    public string? ReportedStatus { get; set; }

    public bool SignatureValid { get; set; }

    public bool WasApplied { get; set; }

    public string? Payload { get; set; }

    [StringLength(400)]
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("OrderId")]
    [InverseProperty("PaymentEvents")]
    public virtual Order Order { get; set; } = null!;
}
