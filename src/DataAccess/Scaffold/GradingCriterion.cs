using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("SkillId", "Code", Name = "UQ_Criteria_Skill_Code", IsUnique = true)]
[Index("CriterionId", "SkillId", Name = "UQ_GradingCriteria_Id_Skill", IsUnique = true)]
public partial class GradingCriterion
{
    [Key]
    public int CriterionId { get; set; }

    public int CertId { get; set; }

    public int SkillId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [StringLength(120)]
    public string Name { get; set; } = null!;

    [Column(TypeName = "decimal(5, 2)")]
    public decimal MaxScore { get; set; }

    [Column(TypeName = "decimal(5, 2)")]
    public decimal Weight { get; set; }

    public int DisplayOrder { get; set; }

    [InverseProperty("GradingCriterion")]
    public virtual ICollection<AiGradingScore> AiGradingScores { get; set; } = new List<AiGradingScore>();

    [ForeignKey("CertId")]
    [InverseProperty("GradingCriteria")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("GradingCriterion")]
    public virtual ICollection<SectionCriterion> SectionCriteria { get; set; } = new List<SectionCriterion>();

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("GradingCriteria")]
    public virtual Skill Skill { get; set; } = null!;
}
