using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("CertId", "Code", Name = "UQ_Skills_Cert_Code", IsUnique = true)]
[Index("SkillId", "CertId", Name = "UQ_Skills_Id_Cert", IsUnique = true)]
[Index("SkillId", "Modality", Name = "UQ_Skills_Id_Modality", IsUnique = true)]
public partial class Skill
{
    [Key]
    public int SkillId { get; set; }

    public int CertId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [StringLength(120)]
    public string Name { get; set; } = null!;

    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public int? DurationMinutes { get; set; }

    [InverseProperty("SkillNavigation")]
    public virtual ICollection<AttemptSkillScore> AttemptSkillScoreSkillNavigations { get; set; } = new List<AttemptSkillScore>();

    [InverseProperty("Skill")]
    public virtual ICollection<AttemptSkillScore> AttemptSkillScoreSkills { get; set; } = new List<AttemptSkillScore>();

    [ForeignKey("CertId")]
    [InverseProperty("Skills")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("Skill")]
    public virtual ICollection<GradingCriterion> GradingCriteria { get; set; } = new List<GradingCriterion>();

    [InverseProperty("SkillNavigation")]
    public virtual ICollection<MentorProfile> MentorProfileSkillNavigations { get; set; } = new List<MentorProfile>();

    [InverseProperty("Skill")]
    public virtual ICollection<MentorProfile> MentorProfileSkills { get; set; } = new List<MentorProfile>();

    [InverseProperty("Skill")]
    public virtual ICollection<PathModule> PathModules { get; set; } = new List<PathModule>();

    [InverseProperty("Skill")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    [InverseProperty("Skill")]
    public virtual ICollection<ScoreScale> ScoreScales { get; set; } = new List<ScoreScale>();

    [InverseProperty("SkillNavigation")]
    public virtual ICollection<Section> SectionSkillNavigations { get; set; } = new List<Section>();

    [InverseProperty("Skill")]
    public virtual ICollection<Section> SectionSkills { get; set; } = new List<Section>();
}
