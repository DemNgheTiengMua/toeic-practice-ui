using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("UserId", "CertId", Name = "IX_Recommendations_User")]
public partial class Recommendation
{
    [Key]
    public int RecId { get; set; }

    public int UserId { get; set; }

    public int CertId { get; set; }

    public int? GradingId { get; set; }

    public int? InsightId { get; set; }

    public int? AttemptId { get; set; }

    public int? PracticeSessionId { get; set; }

    public int? SkillId { get; set; }

    public string Text { get; set; } = null!;

    [StringLength(8)]
    [Unicode(false)]
    public string Severity { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string? TargetBandCode { get; set; }

    public bool IsResolved { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("InsightId, UserId")]
    [InverseProperty("Recommendations")]
    public virtual AiInsight? AiInsight { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("Recommendations")]
    public virtual Certificate Cert { get; set; } = null!;

    [ForeignKey("AttemptId, UserId")]
    [InverseProperty("Recommendations")]
    public virtual ExamAttempt? ExamAttempt { get; set; }

    [ForeignKey("GradingId")]
    [InverseProperty("Recommendations")]
    public virtual AiGrading? Grading { get; set; }

    [ForeignKey("PracticeSessionId, UserId")]
    [InverseProperty("Recommendations")]
    public virtual PracticeSession? PracticeSession { get; set; }

    [ForeignKey("CertId, TargetBandCode")]
    [InverseProperty("Recommendations")]
    public virtual ScoreBand? ScoreBand { get; set; }

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("Recommendations")]
    public virtual Skill? Skill { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("Recommendations")]
    public virtual User User { get; set; } = null!;
}
