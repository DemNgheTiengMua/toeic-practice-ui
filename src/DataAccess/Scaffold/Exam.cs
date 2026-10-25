using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

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

    [InverseProperty("Exam")]
    public virtual ICollection<PathModuleItem> PathModuleItems { get; set; } = new List<PathModuleItem>();

    [InverseProperty("ExamNavigation")]
    public virtual ICollection<PlacementTest> PlacementTestExamNavigations { get; set; } = new List<PlacementTest>();

    [InverseProperty("Exam")]
    public virtual ICollection<PlacementTest> PlacementTestExams { get; set; } = new List<PlacementTest>();

    [InverseProperty("Exam")]
    public virtual ICollection<QuestionGroup> QuestionGroups { get; set; } = new List<QuestionGroup>();

    [InverseProperty("Exam")]
    public virtual ICollection<ScoreScale> ScoreScales { get; set; } = new List<ScoreScale>();
}
