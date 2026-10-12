using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("InsightId", "UserId", Name = "UQ_AiInsights_Id_User", IsUnique = true)]
[Index("UserId", "Scope", "SourceFingerprint", Name = "UX_AiInsights_Fingerprint", IsUnique = true)]
public partial class AiInsight
{
    [Key]
    public int InsightId { get; set; }

    public int UserId { get; set; }

    public int CertId { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string Scope { get; set; } = null!;

    public int? PracticeSessionId { get; set; }

    public int? AttemptId { get; set; }

    public int? LearnerPathId { get; set; }

    public string? Narrative { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? Trend { get; set; }

    [StringLength(8)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [StringLength(400)]
    public string? FailureReason { get; set; }

    [StringLength(80)]
    public string? Model { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string SourceFingerprint { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("AiInsights")]
    public virtual Certificate Cert { get; set; } = null!;

    [ForeignKey("AttemptId, UserId")]
    [InverseProperty("AiInsights")]
    public virtual ExamAttempt? ExamAttempt { get; set; }

    [ForeignKey("LearnerPathId, UserId")]
    [InverseProperty("AiInsights")]
    public virtual LearnerPath? LearnerPath { get; set; }

    [ForeignKey("PracticeSessionId, UserId")]
    [InverseProperty("AiInsights")]
    public virtual PracticeSession? PracticeSession { get; set; }

    [InverseProperty("AiInsight")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    [ForeignKey("UserId")]
    [InverseProperty("AiInsights")]
    public virtual User User { get; set; } = null!;
}
