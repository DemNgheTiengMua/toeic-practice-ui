using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("OptionId", "QuestionId", Name = "UQ_Options_Id_Question", IsUnique = true)]
[Index("QuestionId", "Label", Name = "UQ_Options_Question_Label", IsUnique = true)]
public partial class QuestionOption
{
    [Key]
    public int OptionId { get; set; }

    public int QuestionId { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string QuestionType { get; set; } = null!;

    [StringLength(4)]
    [Unicode(false)]
    public string Label { get; set; } = null!;

    public string Text { get; set; } = null!;

    public bool IsCorrect { get; set; }

    public string? Rationale { get; set; }

    [InverseProperty("QuestionOption")]
    public virtual ICollection<AttemptAnswer> AttemptAnswers { get; set; } = new List<AttemptAnswer>();

    [InverseProperty("QuestionOption")]
    public virtual ICollection<PracticeAnswer> PracticeAnswers { get; set; } = new List<PracticeAnswer>();

    [ForeignKey("QuestionId")]
    [InverseProperty("QuestionOptionQuestion")]
    public virtual Question Question { get; set; } = null!;

    [ForeignKey("QuestionId, QuestionType")]
    [InverseProperty("QuestionOptionQuestionNavigations")]
    public virtual Question QuestionNavigation { get; set; } = null!;
}
