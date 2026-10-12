using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("MentorId", "Type", Name = "UQ_MentorProfiles_Id_Type", IsUnique = true)]
public partial class MentorProfile
{
    [Key]
    public int MentorId { get; set; }

    public int? UserId { get; set; }

    [StringLength(120)]
    public string DisplayName { get; set; } = null!;

    public string? Bio { get; set; }

    public int CertId { get; set; }

    public int? SkillId { get; set; }

    [StringLength(6)]
    [Unicode(false)]
    public string Type { get; set; } = null!;

    [Column(TypeName = "decimal(10, 2)")]
    public decimal PricePerSlot { get; set; }

    public int PriceCredits { get; set; }

    [StringLength(200)]
    public string? Headline { get; set; }

    [StringLength(400)]
    public string? Expertise { get; set; }

    public int SessionMinutes { get; set; }

    public bool IsActive { get; set; }

    [InverseProperty("MentorProfile")]
    public virtual ICollection<Booking> BookingMentorProfiles { get; set; } = new List<Booking>();

    [InverseProperty("Mentor")]
    public virtual ICollection<Booking> BookingMentors { get; set; } = new List<Booking>();

    [ForeignKey("CertId")]
    [InverseProperty("MentorProfiles")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("Mentor")]
    public virtual ICollection<MentorPayout> MentorPayouts { get; set; } = new List<MentorPayout>();

    [InverseProperty("Mentor")]
    public virtual ICollection<MentorSlot> MentorSlots { get; set; } = new List<MentorSlot>();

    [ForeignKey("SkillId")]
    [InverseProperty("MentorProfileSkills")]
    public virtual Skill? Skill { get; set; }

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("MentorProfileSkillNavigations")]
    public virtual Skill? SkillNavigation { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("MentorProfiles")]
    public virtual User? User { get; set; }
}
