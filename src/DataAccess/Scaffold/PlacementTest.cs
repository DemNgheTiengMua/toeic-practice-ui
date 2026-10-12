using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("UserId", "CertId", Name = "IX_Placement_User")]
[Index("PlacementId", "UserId", Name = "UQ_PlacementTests_Id_User", IsUnique = true)]
public partial class PlacementTest
{
    [Key]
    public int PlacementId { get; set; }

    public int UserId { get; set; }

    public int CertId { get; set; }

    public int? ExamId { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string? ResultBandCode { get; set; }

    public int? AttemptId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string ExamPurpose { get; set; } = null!;

    public int? TotalScore { get; set; }

    public DateTimeOffset TakenAt { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("PlacementTests")]
    public virtual Certificate Cert { get; set; } = null!;

    [ForeignKey("ExamId, CertId")]
    [InverseProperty("PlacementTestExams")]
    public virtual Exam? Exam { get; set; }

    [ForeignKey("AttemptId, UserId")]
    [InverseProperty("PlacementTests")]
    public virtual ExamAttempt? ExamAttempt { get; set; }

    [ForeignKey("ExamId, CertId, ExamPurpose")]
    [InverseProperty("PlacementTestExamNavigations")]
    public virtual Exam? ExamNavigation { get; set; }

    [InverseProperty("PlacementTest")]
    public virtual ICollection<LearnerPath> LearnerPaths { get; set; } = new List<LearnerPath>();

    [ForeignKey("UserId")]
    [InverseProperty("PlacementTests")]
    public virtual User User { get; set; } = null!;
}
