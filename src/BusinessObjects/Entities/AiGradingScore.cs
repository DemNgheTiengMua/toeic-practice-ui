using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[PrimaryKey("GradingId", "CriterionId")]
public partial class AiGradingScore
{
    [Key]
    public int GradingId { get; set; }

    [Key]
    public int CriterionId { get; set; }

    public int SkillId { get; set; }

    [Column(TypeName = "decimal(5, 2)")]
    public decimal Score { get; set; }

    public string? Comment { get; set; }

    [ForeignKey("GradingId, SkillId")]
    [InverseProperty("AiGradingScores")]
    public virtual AiGrading AiGrading { get; set; } = null!;

    [ForeignKey("CriterionId, SkillId")]
    [InverseProperty("AiGradingScores")]
    public virtual GradingCriterion GradingCriterion { get; set; } = null!;
}
