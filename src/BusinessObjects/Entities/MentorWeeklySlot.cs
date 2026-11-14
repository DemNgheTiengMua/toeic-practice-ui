using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("MentorId", "DayOfWeek", Name = "IX_MentorWeeklySlots_Mentor")]
public partial class MentorWeeklySlot
{
    [Key]
    public int WeeklySlotId { get; set; }

    public int MentorId { get; set; }

    public byte DayOfWeek { get; set; }

    [Precision(0)]
    public TimeOnly StartTime { get; set; }

    [Precision(0)]
    public TimeOnly EndTime { get; set; }

    [ForeignKey("MentorId")]
    [InverseProperty("MentorWeeklySlots")]
    public virtual MentorProfile Mentor { get; set; } = null!;
}
