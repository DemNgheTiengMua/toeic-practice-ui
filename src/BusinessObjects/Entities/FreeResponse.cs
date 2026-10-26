using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("QuestionId", Name = "IX_FreeResponses_Question")]
[Index("FreeResponseId", "SkillId", Name = "UQ_FreeResp_Id_Skill", IsUnique = true)]
[Index("AttemptId", "QuestionId", Name = "UQ_FreeResponses", IsUnique = true)]
public partial class FreeResponse
{
    [Key]
    public int FreeResponseId { get; set; }

    public int AttemptId { get; set; }

    public int QuestionId { get; set; }

    public int ExamId { get; set; }

    public int SkillId { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string QuestionType { get; set; } = null!;

    public string? ResponseText { get; set; }

    [StringLength(400)]
    public string? AudioPath { get; set; }

    public int? DurationMs { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public DateTimeOffset? DraftSavedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    [InverseProperty("FreeResponse")]
    public virtual AiGrading? AiGradingFreeResponse { get; set; }

    [InverseProperty("FreeResponseNavigation")]
    public virtual ICollection<AiGrading> AiGradingFreeResponseNavigations { get; set; } = new List<AiGrading>();

    [ForeignKey("AttemptId")]
    [InverseProperty("FreeResponseAttempts")]
    public virtual ExamAttempt Attempt { get; set; } = null!;

    [ForeignKey("AttemptId, ExamId")]
    [InverseProperty("FreeResponseExamAttempts")]
    public virtual ExamAttempt ExamAttempt { get; set; } = null!;

    [ForeignKey("QuestionId")]
    [InverseProperty("FreeResponseQuestions")]
    public virtual Question Question { get; set; } = null!;

    [ForeignKey("QuestionId, QuestionType")]
    [InverseProperty("FreeResponseQuestion1s")]
    public virtual Question Question1 { get; set; } = null!;

    [ForeignKey("QuestionId, SkillId")]
    [InverseProperty("FreeResponseQuestion2s")]
    public virtual Question Question2 { get; set; } = null!;

    [ForeignKey("QuestionId, ExamId")]
    [InverseProperty("FreeResponseQuestionNavigations")]
    public virtual Question QuestionNavigation { get; set; } = null!;
}
