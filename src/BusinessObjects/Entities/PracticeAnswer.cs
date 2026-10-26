using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("QuestionId", Name = "IX_PracticeAnswers_Question")]
[Index("SessionId", "QuestionId", Name = "UQ_PracticeAnswers", IsUnique = true)]
public partial class PracticeAnswer
{
    [Key]
    public int AnswerId { get; set; }

    public int SessionId { get; set; }

    public int QuestionId { get; set; }

    public int SectionId { get; set; }

    public int? SelectedOptionId { get; set; }

    public string? ResponseText { get; set; }

    public string? SelectedOptionIdsJson { get; set; }

    public bool? IsCorrect { get; set; }

    public bool HintUsed { get; set; }

    public int DisplayOrder { get; set; }

    [ForeignKey("SessionId, SectionId")]
    [InverseProperty("PracticeAnswerPracticeSessions")]
    public virtual PracticeSession PracticeSession { get; set; } = null!;

    [ForeignKey("QuestionId")]
    [InverseProperty("PracticeAnswerQuestions")]
    public virtual Question Question { get; set; } = null!;

    [ForeignKey("QuestionId, SectionId")]
    [InverseProperty("PracticeAnswerQuestionNavigations")]
    public virtual Question QuestionNavigation { get; set; } = null!;

    [ForeignKey("SelectedOptionId, QuestionId")]
    [InverseProperty("PracticeAnswers")]
    public virtual QuestionOption? QuestionOption { get; set; }

    [ForeignKey("SessionId")]
    [InverseProperty("PracticeAnswerSessions")]
    public virtual PracticeSession Session { get; set; } = null!;
}
