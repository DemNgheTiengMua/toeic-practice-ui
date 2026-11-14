using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("ExamId", Name = "IX_Attempts_Exam")]
[Index("UserId", "StartedAt", Name = "IX_Attempts_User")]
[Index("AttemptId", "CertId", Name = "UQ_Attempts_Id_Cert", IsUnique = true)]
[Index("AttemptId", "ExamId", Name = "UQ_Attempts_Id_Exam", IsUnique = true)]
[Index("AttemptId", "UserId", Name = "UQ_Attempts_Id_User", IsUnique = true)]
public partial class ExamAttempt
{
    [Key]
    public int AttemptId { get; set; }

    public int UserId { get; set; }

    public int ExamId { get; set; }

    public int CertId { get; set; }

    public int? ActiveSkillId { get; set; }

    public int ActiveUnitNumber { get; set; } = 1;

    public int? ActiveGroupId { get; set; }

    [StringLength(64)]
    public string? AuthorizationCode { get; set; }

    public DateTime? PhaseDeadlineAt { get; set; }

    public int LastAudioOffsetMs { get; set; }

    public string? CandidateNotesJson { get; set; }

    public DateTimeOffset? PreflightVerifiedAt { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>#216 / PLC-13: the first half of placement's inactivity expiry. Written by a write,
    /// never by a read, so a reload cannot extend the sitting.</summary>
    public DateTimeOffset? LastActivityAt { get; set; }

    public bool IsAutoSubmitted { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>
    /// #269: the roster position the learner is on. This replaces <c>LastQuestionId</c>, which was a
    /// foreign key into <c>Questions</c> and therefore could not hold a pool position at all.
    /// </summary>
    public int? CurrentExamItemId { get; set; }

    public int? TotalScore { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? BandCode { get; set; }

    /// <summary>
    /// #246: which generation of the product this sitting was taken under. NULL means "written by the
    /// current code"; a value means the row was PRESERVED across a cutover and is history, not a
    /// sitting. <c>trg_Attempts_EpochIsReadOnly</c> refuses every write to a tagged row, so this is
    /// read-only in fact and not by convention.
    /// </summary>
    [StringLength(16)]
    [Unicode(false)]
    public string? EpochTag { get; set; }

    /// <summary>
    /// #289: the chosen placement scope (<c>all_skills</c> or <c>listening_and_reading</c>); NULL for
    /// mock sittings and legacy rows.
    /// </summary>
    [StringLength(24)]
    [Unicode(false)]
    public string? PlacementScope { get; set; }

    [InverseProperty("ExamAttempt")]
    public virtual ICollection<AiInsight> AiInsights { get; set; } = new List<AiInsight>();

    [InverseProperty("Attempt")]
    public virtual ICollection<AttemptAnswer> AttemptAnswerAttempts { get; set; } = new List<AttemptAnswer>();

    [InverseProperty("ExamAttempt")]
    public virtual ICollection<AttemptAnswer> AttemptAnswerExamAttempts { get; set; } = new List<AttemptAnswer>();

    [InverseProperty("Attempt")]
    public virtual ICollection<AttemptSkillScore> AttemptSkillScoreAttempts { get; set; } = new List<AttemptSkillScore>();

    [InverseProperty("ExamAttempt")]
    public virtual ICollection<AttemptSkillScore> AttemptSkillScoreExamAttempts { get; set; } = new List<AttemptSkillScore>();

    /// <summary>
    /// #274: the paper this sitting was served, per skill and stage. Empty for an attempt that reads
    /// the exam's shared roster, which is every attempt created before #274 and every mock sitting.
    /// </summary>

    /// <summary>#274: the positions this sitting was served, in sitting order.</summary>

    [ForeignKey("CertId")]
    [InverseProperty("ExamAttempts")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("ExamAttempt")]
    public virtual ICollection<Complaint> Complaints { get; set; } = new List<Complaint>();

    [InverseProperty("ExamAttempt")]
    public virtual ICollection<CreditTransaction> CreditTransactions { get; set; } = new List<CreditTransaction>();

    [ForeignKey("ExamId")]
    [InverseProperty("ExamAttemptExams")]
    public virtual Exam Exam { get; set; } = null!;

    [ForeignKey("ExamId, CertId")]
    [InverseProperty("ExamAttemptExamNavigations")]
    public virtual Exam ExamNavigation { get; set; } = null!;

    [InverseProperty("Attempt")]
    public virtual ICollection<FreeResponse> FreeResponseAttempts { get; set; } = new List<FreeResponse>();

    [InverseProperty("ExamAttempt")]
    public virtual ICollection<FreeResponse> FreeResponseExamAttempts { get; set; } = new List<FreeResponse>();

    [InverseProperty("ExamAttempt")]
    public virtual ICollection<PlacementTest> PlacementTests { get; set; } = new List<PlacementTest>();

    [InverseProperty("ExamAttempt")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    [ForeignKey("UserId")]
    [InverseProperty("ExamAttempts")]
    public virtual User User { get; set; } = null!;

    [ForeignKey("ActiveSkillId")]
    public virtual Skill? ActiveSkill { get; set; }

    // #269: ActiveGroup is gone with the FK. ActiveGroupId is a block ADDRESS - a stimulus id, or the
    // negated position id for a solo block - so it names no QuestionGroup and no Stimuli row that a
    // navigation could follow.
}
