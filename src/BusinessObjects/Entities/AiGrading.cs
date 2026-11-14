using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("FreeResponseId", Name = "UQ_AiGradings_FreeResponse", IsUnique = true)]
[Index("GradingId", "SkillId", Name = "UQ_AiGradings_Id_Skill", IsUnique = true)]
public partial class AiGrading
{
    [Key]
    public int GradingId { get; set; }

    public int FreeResponseId { get; set; }

    public int SkillId { get; set; }

    [StringLength(8)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column(TypeName = "decimal(6, 2)")]
    public decimal? OverallScaled { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? BandCode { get; set; }

    [StringLength(80)]
    public string? Model { get; set; }

    [StringLength(400)]
    public string? FailureReason { get; set; }

    /// <summary>E2.1 (#151): the one change that would improve this text most, in the grader's own words.</summary>
    [StringLength(1000)]
    public string? PriorityFix { get; set; }

    /// <summary>E2.1 (#151): the grader's rewrite of the weakest part of the answer.</summary>
    public string? ModelRewrite { get; set; }

    /// <summary>E2.1 (#151): up to five located errors as JSON — original snippet, correction, category and a
    /// one-line explanation. Kept as the grader's raw list so the page and any later consumer read one shape.</summary>
    public string? ErrorSpansJson { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    [InverseProperty("AiGrading")]
    public virtual ICollection<AiGradingScore> AiGradingScores { get; set; } = new List<AiGradingScore>();

    [ForeignKey("FreeResponseId")]
    [InverseProperty("AiGradingFreeResponse")]
    public virtual FreeResponse FreeResponse { get; set; } = null!;

    [ForeignKey("FreeResponseId, SkillId")]
    [InverseProperty("AiGradingFreeResponseNavigations")]
    public virtual FreeResponse FreeResponseNavigation { get; set; } = null!;

    [InverseProperty("Grading")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();
}
