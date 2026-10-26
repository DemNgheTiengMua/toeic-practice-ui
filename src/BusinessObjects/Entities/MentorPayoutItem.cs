using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[PrimaryKey("PayoutId", "BookingId")]
[Index("BookingId", Name = "UQ_PayoutItems_Booking", IsUnique = true)]
public partial class MentorPayoutItem
{
    [Key]
    public int PayoutId { get; set; }

    [Key]
    public int BookingId { get; set; }

    public int MentorId { get; set; }

    [ForeignKey("BookingId, MentorId")]
    [InverseProperty("MentorPayoutItems")]
    public virtual Booking Booking { get; set; } = null!;

    [ForeignKey("PayoutId, MentorId")]
    [InverseProperty("MentorPayoutItems")]
    public virtual MentorPayout MentorPayout { get; set; } = null!;
}
