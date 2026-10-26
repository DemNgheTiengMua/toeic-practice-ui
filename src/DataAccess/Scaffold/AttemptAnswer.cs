using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("QuestionId", Name = "IX_AttemptAnswers_Question")]
[Index("AttemptId", "QuestionId", Name = "UQ_AttemptAnswers", IsUnique = true)]
public partial class AttemptAnswer
{
    [Key]
    public int AnswerId { get; set; }

    public int AttemptId { get; set; }

    public int QuestionId { get; set; }

    public int ExamId { get; set; }

    public int? SelectedOptionId { get; set; }

    public string? ResponseText { get; set; }

    public string? SelectedOptionIdsJson { get; set; }

    public bool? IsCorrect { get; set; }

    public bool IsFlagged { get; set; }

    [ForeignKey("AttemptId")]
    [InverseProperty("AttemptAnswerAttempts")]
    public virtual ExamAttempt Attempt { get; set; } = null!;

    [ForeignKey("AttemptId, ExamId")]
    [InverseProperty("AttemptAnswerExamAttempts")]
    public virtual ExamAttempt ExamAttempt { get; set; } = null!;

    [ForeignKey("QuestionId")]
    [InverseProperty("AttemptAnswerQuestions")]
    public virtual Question Question { get; set; } = null!;

    [ForeignKey("QuestionId, ExamId")]
    [InverseProperty("AttemptAnswerQuestionNavigations")]
    public virtual Question QuestionNavigation { get; set; } = null!;

    [ForeignKey("SelectedOptionId, QuestionId")]
    [InverseProperty("AttemptAnswers")]
    public virtual QuestionOption? QuestionOption { get; set; }
}
