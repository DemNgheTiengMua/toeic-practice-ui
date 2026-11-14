using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

public partial class MentorProfile
{
    [Key]
    public int MentorId { get; set; }

    [StringLength(120)]
    public string DisplayName { get; set; } = null!;

    public string? Bio { get; set; }

    public int CertId { get; set; }

    public int? SkillId { get; set; }

    [StringLength(400)]
    public string MeetLink { get; set; } = null!;

    public int PriceCredits { get; set; }

    [StringLength(200)]
    public string? Headline { get; set; }

    [StringLength(400)]
    public string? Expertise { get; set; }

    public int SessionMinutes { get; set; }

    public bool IsActive { get; set; }

    [InverseProperty("Mentor")]
    public virtual ICollection<Booking> BookingMentors { get; set; } = new List<Booking>();

    [ForeignKey("CertId")]
    [InverseProperty("MentorProfiles")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("Mentor")]
    public virtual ICollection<MentorSlot> MentorSlots { get; set; } = new List<MentorSlot>();

    [InverseProperty("Mentor")]
    public virtual ICollection<MentorWeeklySlot> MentorWeeklySlots { get; set; } = new List<MentorWeeklySlot>();

    [ForeignKey("SkillId")]
    [InverseProperty("MentorProfileSkills")]
    public virtual Skill? Skill { get; set; }

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("MentorProfileSkillNavigations")]
    public virtual Skill? SkillNavigation { get; set; }
}
