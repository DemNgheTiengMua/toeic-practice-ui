using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("MentorId", Name = "IX_MentorPayouts_Mentor")]
[Index("PayoutId", "MentorId", Name = "UQ_MentorPayouts_Id_Mentor", IsUnique = true)]
public partial class MentorPayout
{
    [Key]
    public int PayoutId { get; set; }

    public int MentorId { get; set; }

    public DateTimeOffset PeriodStart { get; set; }

    public DateTimeOffset PeriodEnd { get; set; }

    [StringLength(7)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public DateTimeOffset? PaidAt { get; set; }

    [ForeignKey("MentorId")]
    [InverseProperty("MentorPayouts")]
    public virtual MentorProfile Mentor { get; set; } = null!;

    [InverseProperty("MentorPayout")]
    public virtual ICollection<MentorPayoutItem> MentorPayoutItems { get; set; } = new List<MentorPayoutItem>();
}
