using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

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

    /// <summary>#191: which service declared this write. Required by the trigger on any write of the
    /// snapshot; NULL while the placement is still in progress.</summary>
    [StringLength(64)]
    [Unicode(false)]
    public string? WrittenByService { get; set; }

    /// <summary>#191: monotonic ordering token, so an older out-of-order callback cannot overwrite a
    /// newer result.</summary>
    public int ProjectionVersion { get; set; }

    public DateTimeOffset TakenAt { get; set; }

    /// <summary>
    /// #212 / PLC-36: the second honest way to finish. A placement with no measured skill can be done
    /// only if the reason is recorded here — the learner skipped, the sitting timed out, or grading
    /// failed. One of <see cref="PlacementFailureReason"/>.
    /// </summary>
    [StringLength(24)]
    [Unicode(false)]
    public string? CompletionFailureReason { get; set; }

    /// <summary>
    /// #212: the per-skill results, one per skill the sitting touched. The placement is done when at
    /// least one of these is <see cref="PlacementResultStatus.Measured"/> or
    /// <see cref="PlacementResultStatus.EstimatedFromOneTask"/>, or when
    /// <see cref="CompletionFailureReason"/> is set.
    /// </summary>
    [InverseProperty("Placement")]
    public virtual ICollection<PlacementSkillResult> SkillResults { get; set; } = new List<PlacementSkillResult>();

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
