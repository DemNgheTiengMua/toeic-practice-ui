using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("MentorId", Name = "IX_Bookings_Mentor")]
[Index("UserId", "CreatedAt", Name = "IX_Bookings_User")]
[Index("BookingId", "UserId", Name = "UQ_Bookings_Id_User", IsUnique = true)]
public partial class Booking
{
    [Key]
    public int BookingId { get; set; }

    public int UserId { get; set; }

    public int MentorId { get; set; }

    public int SlotId { get; set; }

    public int PriceCredits { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [StringLength(400)]
    public string? MeetLink { get; set; }

    [StringLength(120)]
    public string? FocusArea { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [InverseProperty("Booking")]
    public virtual ICollection<Complaint> Complaints { get; set; } = new List<Complaint>();

    [InverseProperty("Booking")]
    public virtual ICollection<CreditTransaction> CreditTransactions { get; set; } = new List<CreditTransaction>();

    [ForeignKey("MentorId")]
    [InverseProperty("BookingMentors")]
    public virtual MentorProfile Mentor { get; set; } = null!;

    [ForeignKey("SlotId, MentorId")]
    [InverseProperty("BookingMentorSlots")]
    public virtual MentorSlot MentorSlot { get; set; } = null!;

    [InverseProperty("Booking")]
    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    [ForeignKey("SlotId")]
    [InverseProperty("BookingSlot")]
    public virtual MentorSlot Slot { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("Bookings")]
    public virtual User User { get; set; } = null!;
}
