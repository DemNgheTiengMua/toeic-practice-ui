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

    /// <summary>
    /// #227: which edition of this paper this row is. One row is one edition: a structural repair
    /// forks a new <see cref="Exam"/> whose <see cref="SupersedesExamId"/> names this one, so the
    /// items, the timers and the chrome a historical attempt sat never move. Defaults to 1, which is
    /// what every paper created before the column existed is.
    /// </summary>
    public int EditionNumber { get; set; } = 1;

    /// <summary>
    /// #227: the edition this one replaced, or NULL for a paper that was never forked. A chain rather
    /// than a "current edition" pointer: the live edition is the one whose status is published.
    /// </summary>
    public int? SupersedesExamId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// #201 / ADM-03: exam structure is guarded, so a write to this table declares who made it. NULL
    /// means a seed, a fixture or a migration, which names no account. Stamped by
    /// <c>StaffAccountStampInterceptor</c>.
    /// </summary>
    public int? LastWrittenBy { get; set; }

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
    /// <summary>#226: the recorded readiness pass that unlocks the publish door. At most one row.</summary>
    public virtual ExamPublishAudit? PublishAudit { get; set; }

    /// <summary>
    /// #227: the frozen edition - what was in force when this paper went live. Written by the publish
    /// trigger and by nothing else. NULL on a draft, which has no learners and therefore no edition to
    /// protect.
    /// </summary>
    [InverseProperty("Exam")]
    public virtual ExamEdition? Edition { get; set; }

    /// <summary>#227: the edition this paper replaced, or NULL for a first edition.</summary>
    [InverseProperty("SupersededBy")]
    public virtual Exam? SupersedesExam { get; set; }

    /// <summary>#227: the editions forked from this one.</summary>
    [InverseProperty("SupersedesExam")]
    public virtual ICollection<Exam> SupersededBy { get; set; } = new List<Exam>();

    [InverseProperty("Exam")]
    public virtual ICollection<ScoreScale> ScoreScales { get; set; } = new List<ScoreScale>();
}
