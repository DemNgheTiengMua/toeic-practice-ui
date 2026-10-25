using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("ExamId", "DisplayOrder", Name = "IX_QGroups_Exam")]
[Index("SectionId", Name = "IX_QGroups_Section")]
[Index("GroupId", "ExamId", Name = "UQ_QGroups_Id_Exam", IsUnique = true)]
[Index("GroupId", "SectionId", Name = "UQ_QGroups_Id_Section", IsUnique = true)]
public partial class QuestionGroup
{
    [Key]
    public int GroupId { get; set; }

    public int ExamId { get; set; }

    public int CertId { get; set; }

    public int SectionId { get; set; }

    public int UnitNumber { get; set; } = 1;

    public string? Passage { get; set; }

    [StringLength(400)]
    public string? AudioPath { get; set; }

    public int? AudioStartMs { get; set; }

    public int? AudioEndMs { get; set; }

    [StringLength(400)]
    public string? ImagePath { get; set; }

    [StringLength(400)]
    public string? ImageAlt { get; set; }

    public string? Transcript { get; set; }

    public int? PrepSeconds { get; set; }

    public int? ResponseSeconds { get; set; }

    public int DisplayOrder { get; set; }

    [ForeignKey("ExamId, CertId")]
    [InverseProperty("QuestionGroups")]
    public virtual Exam Exam { get; set; } = null!;

    [InverseProperty("Group")]
    public virtual ICollection<Question> QuestionGroups { get; set; } = new List<Question>();

    [InverseProperty("QuestionGroupNavigation")]
    public virtual ICollection<Question> QuestionQuestionGroupNavigations { get; set; } = new List<Question>();

    [InverseProperty("QuestionGroup")]
    public virtual ICollection<Question> QuestionQuestionGroups { get; set; } = new List<Question>();

    [ForeignKey("SectionId, CertId")]
    [InverseProperty("QuestionGroups")]
    public virtual Section Section { get; set; } = null!;
}
