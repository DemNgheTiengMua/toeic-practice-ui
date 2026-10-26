using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[PrimaryKey("SectionId", "CriterionId")]
public partial class SectionCriterion
{
    [Key]
    public int SectionId { get; set; }

    [Key]
    public int CriterionId { get; set; }

    public int SkillId { get; set; }

    [Column(TypeName = "decimal(5, 2)")]
    public decimal? MaxScore { get; set; }

    [ForeignKey("CriterionId, SkillId")]
    [InverseProperty("SectionCriteria")]
    public virtual GradingCriterion GradingCriterion { get; set; } = null!;

    [ForeignKey("SectionId, SkillId")]
    [InverseProperty("SectionCriteria")]
    public virtual Section Section { get; set; } = null!;
}
