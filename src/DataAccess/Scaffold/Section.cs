using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("SectionId", "CertId", Name = "UQ_Sections_Id_Cert", IsUnique = true)]
[Index("SectionId", "Modality", Name = "UQ_Sections_Id_Modality", IsUnique = true)]
[Index("SectionId", "SkillId", Name = "UQ_Sections_Id_Skill", IsUnique = true)]
[Index("SkillId", "Code", Name = "UQ_Sections_Skill_Code", IsUnique = true)]
public partial class Section
{
    [Key]
    public int SectionId { get; set; }

    public int SkillId { get; set; }

    public int CertId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [StringLength(120)]
    public string Name { get; set; } = null!;

    public int? OptionCount { get; set; }

    public int? ExpectedQuestionCount { get; set; }

    public int DisplayOrder { get; set; }

    [InverseProperty("Section")]
    public virtual ICollection<ExamSectionAudio> ExamSectionAudios { get; set; } = new List<ExamSectionAudio>();

    [InverseProperty("Section")]
    public virtual ICollection<PathModuleItem> PathModuleItems { get; set; } = new List<PathModuleItem>();

    [InverseProperty("SectionNavigation")]
    public virtual ICollection<PracticeSession> PracticeSessionSectionNavigations { get; set; } = new List<PracticeSession>();

    [InverseProperty("Section")]
    public virtual ICollection<PracticeSession> PracticeSessionSections { get; set; } = new List<PracticeSession>();

    [InverseProperty("Section")]
    public virtual ICollection<QuestionGroup> QuestionGroups { get; set; } = new List<QuestionGroup>();

    [InverseProperty("Section")]
    public virtual ICollection<Question> Questions { get; set; } = new List<Question>();

    [InverseProperty("Section")]
    public virtual ICollection<SectionCriterion> SectionCriteria { get; set; } = new List<SectionCriterion>();

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("SectionSkills")]
    public virtual Skill Skill { get; set; } = null!;

    [ForeignKey("SkillId, Modality")]
    [InverseProperty("SectionSkillNavigations")]
    public virtual Skill SkillNavigation { get; set; } = null!;
}
