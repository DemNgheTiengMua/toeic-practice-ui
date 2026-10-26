using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("MentorId", "StartAt", Name = "IX_MentorSlots_Mentor")]
[Index("SlotId", "MentorId", Name = "UQ_MentorSlots_Id_Mentor", IsUnique = true)]
public partial class MentorSlot
{
    [Key]
    public int SlotId { get; set; }

    public int MentorId { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    [StringLength(6)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [InverseProperty("MentorSlot")]
    public virtual ICollection<Booking> BookingMentorSlots { get; set; } = new List<Booking>();

    [InverseProperty("Slot")]
    public virtual Booking? BookingSlot { get; set; }

    [ForeignKey("MentorId")]
    [InverseProperty("MentorSlots")]
    public virtual MentorProfile Mentor { get; set; } = null!;
}
