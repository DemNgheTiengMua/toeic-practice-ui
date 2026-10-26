using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("UserId", "StartedAt", Name = "IX_Practice_User")]
[Index("SessionId", "SectionId", Name = "UQ_PracticeSessions_Id_Section", IsUnique = true)]
[Index("SessionId", "UserId", Name = "UQ_PracticeSessions_Id_User", IsUnique = true)]
public partial class PracticeSession
{
    [Key]
    public int SessionId { get; set; }

    public int UserId { get; set; }

    public int SectionId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    public int QuestionCount { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public int CorrectCount { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string QuestionSource { get; set; } = null!;

    [StringLength(10)]
    [Unicode(false)]
    public string FeedbackMode { get; set; } = null!;

    public int? LearnerPathStepId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    [InverseProperty("PracticeSession")]
    public virtual ICollection<AiInsight> AiInsights { get; set; } = new List<AiInsight>();

    [ForeignKey("LearnerPathStepId")]
    [InverseProperty("PracticeSessions")]
    public virtual LearnerPathStep? LearnerPathStep { get; set; }

    [InverseProperty("PracticeSession")]
    public virtual ICollection<PracticeAnswer> PracticeAnswerPracticeSessions { get; set; } = new List<PracticeAnswer>();

    [InverseProperty("Session")]
    public virtual ICollection<PracticeAnswer> PracticeAnswerSessions { get; set; } = new List<PracticeAnswer>();

    [InverseProperty("PracticeSession")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    [ForeignKey("SectionId")]
    [InverseProperty("PracticeSessionSections")]
    public virtual Section Section { get; set; } = null!;

    [ForeignKey("SectionId, Modality")]
    [InverseProperty("PracticeSessionSectionNavigations")]
    public virtual Section SectionNavigation { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("PracticeSessions")]
    public virtual User User { get; set; } = null!;
}
