using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("ExamId", "CertId", Name = "UQ_Exams_Id_Cert", IsUnique = true)]
[Index("ExamId", "CertId", "Purpose", Name = "UQ_Exams_Id_Cert_Purpose", IsUnique = true)]
public partial class Exam
{
    [Key]
    public int ExamId { get; set; }

    public int CertId { get; set; }

    [StringLength(160)]
    public string Name { get; set; } = null!;

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public int DurationMinutes { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Purpose { get; set; } = null!;

    [StringLength(30)]
    public string DeliveryMode { get; set; } = "strict_exam";

    [StringLength(50)]
    public string? ExamFamily { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(120)]
    public string? SourceName { get; set; }

    [StringLength(1000)]
    public string? InternalNote { get; set; }

    public int? QuestionCount { get; set; }

    /// <summary>
    /// #185 / INV-6: which tier of stock this paper may draw from. Defaults to <c>demo</c> for the
    /// same reason <see cref="Item.ContentTier"/> does ΓÇö an exam is not production until someone
    /// promotes it, and the wrong default here is what lets an unreviewed paper go on sale.
    /// </summary>
    [StringLength(12)]
    [Unicode(false)]
    public string ContentTier { get; set; } = "demo";

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("Exams")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("ExamNavigation")]
    public virtual ICollection<ExamAttempt> ExamAttemptExamNavigations { get; set; } = new List<ExamAttempt>();

    [InverseProperty("Exam")]
    public virtual ICollection<ExamAttempt> ExamAttemptExams { get; set; } = new List<ExamAttempt>();

    [InverseProperty("Exam")]
    public virtual ICollection<ExamSectionAudio> ExamSectionAudios { get; set; } = new List<ExamSectionAudio>();

    /// <summary>#185: the versions this paper binds, one row per assembled item.</summary>
    [InverseProperty("Exam")]
    public virtual ICollection<ExamItem> ExamItems { get; set; } = new List<ExamItem>();

    /// <summary>#232: path steps that sit this exam as a mock section.</summary>
    [InverseProperty("Exam")]
    public virtual ICollection<LearnerPathStep> LearnerPathSteps { get; set; } = new List<LearnerPathStep>();

    [InverseProperty("ExamNavigation")]
    public virtual ICollection<PlacementTest> PlacementTestExamNavigations { get; set; } = new List<PlacementTest>();

    [InverseProperty("Exam")]
    public virtual ICollection<PlacementTest> PlacementTestExams { get; set; } = new List<PlacementTest>();

    [InverseProperty("Exam")]
    public virtual ICollection<QuestionGroup> QuestionGroups { get; set; } = new List<QuestionGroup>();

    [InverseProperty("Exam")]
    public virtual ICollection<ScoreScale> ScoreScales { get; set; } = new List<ScoreScale>();
}
