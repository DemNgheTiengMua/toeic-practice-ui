using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("CardId", "CertId", Name = "UQ_LessonCards_Id_Cert", IsUnique = true)]
public partial class LessonCard
{
    [Key]
    public int CardId { get; set; }

    public int LessonId { get; set; }

    public int CertId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string CardType { get; set; } = null!;

    [StringLength(160)]
    public string Title { get; set; } = null!;

    public string ContentMarkdown { get; set; } = null!;

    public string? QuestionPrompt { get; set; }

    [StringLength(250)]
    public string? OptionA { get; set; }

    [StringLength(250)]
    public string? OptionB { get; set; }

    [StringLength(250)]
    public string? OptionC { get; set; }

    [StringLength(250)]
    public string? OptionD { get; set; }

    [StringLength(1)]
    [Unicode(false)]
    public string? CorrectOption { get; set; }

    public string? Explanation { get; set; }

    public string? Rationale { get; set; }

    public int DisplayOrder { get; set; }

    [InverseProperty("LastViewedCard")]
    public virtual ICollection<LearnerLessonProgress> LearnerLessonProgresses { get; set; } = new List<LearnerLessonProgress>();

    [ForeignKey("LessonId, CertId")]
    [InverseProperty("LessonCards")]
    public virtual Lesson Lesson { get; set; } = null!;
}
