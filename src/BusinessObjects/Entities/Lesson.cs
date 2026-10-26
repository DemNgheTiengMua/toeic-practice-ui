using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("LessonId", "CertId", Name = "UQ_Lessons_Id_Cert", IsUnique = true)]
public partial class Lesson
{
    [Key]
    public int LessonId { get; set; }

    public int CertId { get; set; }

    public int SkillId { get; set; }

    public int? CategoryId { get; set; }

    [StringLength(160)]
    public string Title { get; set; } = null!;

    [StringLength(500)]
    public string? Summary { get; set; }

    public int EstimatedMinutes { get; set; }

    public int DisplayOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("CategoryId")]
    [InverseProperty("Lessons")]
    public virtual QuestionCategory? Category { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("Lessons")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("Lesson")]
    public virtual ICollection<LearnerLessonProgress> LearnerLessonProgresses { get; set; } = new List<LearnerLessonProgress>();

    [InverseProperty("Lesson")]
    public virtual ICollection<LessonCard> LessonCards { get; set; } = new List<LessonCard>();

    [InverseProperty("Lesson")]
    public virtual ICollection<PathModuleItem> PathModuleItems { get; set; } = new List<PathModuleItem>();

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("Lessons")]
    public virtual Skill Skill { get; set; } = null!;
}
