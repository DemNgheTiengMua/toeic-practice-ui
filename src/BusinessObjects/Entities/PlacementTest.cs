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
    /// #213: the learner chose "carry over my valid skills" at the preflight, so a skill whose stored
    /// state is still inside D18's 90-day window is carried into this sitting's result instead of being
    /// re-measured. The choice is on the placement because it is a fact about this sitting, and the
    /// result writer — the one writer of the per-skill rows — is what reads it.
    /// </summary>
    public bool ReusesPriorResults { get; set; }

    /// <summary>#289: the skill scope chosen for this placement (<c>all_skills</c> or <c>listening_and_reading</c>).</summary>
    [StringLength(24)]
    [Unicode(false)]
    public string Scope { get; set; } = "all_skills";

    /// <summary>#289: the ledger debit written when starting a paid retake; NULL for a free first placement.</summary>
    public int? CreditTxnId { get; set; }

    /// <summary>#289: optional caller-supplied key ensuring retried start confirmations do not double-charge.</summary>
    [StringLength(64)]
    [Unicode(false)]
    public string? IdempotencyKey { get; set; }

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

    [ForeignKey("CreditTxnId, UserId")]
    [InverseProperty("PlacementTests")]
    public virtual CreditTransaction? CreditTransaction { get; set; }

    [InverseProperty("PlacementTest")]
    public virtual ICollection<LearnerPath> LearnerPaths { get; set; } = new List<LearnerPath>();

    [ForeignKey("UserId")]
    [InverseProperty("PlacementTests")]
    public virtual User User { get; set; } = null!;
}
