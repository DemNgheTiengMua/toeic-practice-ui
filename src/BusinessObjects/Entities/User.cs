using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

public partial class User
{
    [Key]
    public int UserId { get; set; }

    public int RoleId { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string Email { get; set; } = null!;

    [MaxLength(256)]
    public byte[] PasswordHash { get; set; } = null!;

    public int? ActiveCertId { get; set; }

    [StringLength(120)]
    public string? FullName { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? Phone { get; set; }

    public bool IsLocked { get; set; }

    [StringLength(36)]
    [Unicode(false)]
    public string SecurityStamp { get; set; } = null!;

    public int FailedAttempts { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset? TermsAcceptedAt { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string TimeZoneId { get; set; } = null!;

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("ActiveCertId")]
    [InverseProperty("Users")]
    public virtual Certificate? ActiveCert { get; set; }

    [InverseProperty("User")]
    public virtual ICollection<AiInsight> AiInsights { get; set; } = new List<AiInsight>();

    [InverseProperty("User")]
    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    [InverseProperty("ResolvedByUser")]
    public virtual ICollection<Complaint> ComplaintResolvedByUsers { get; set; } = new List<Complaint>();

    [InverseProperty("User")]
    public virtual ICollection<Complaint> ComplaintUsers { get; set; } = new List<Complaint>();

    [InverseProperty("AdminUser")]
    public virtual ICollection<CreditTransaction> CreditTransactionAdminUsers { get; set; } = new List<CreditTransaction>();

    [InverseProperty("User")]
    public virtual ICollection<CreditTransaction> CreditTransactionUsers { get; set; } = new List<CreditTransaction>();

    [InverseProperty("User")]
    public virtual ICollection<EmailVerificationToken> EmailVerificationTokens { get; set; } = new List<EmailVerificationToken>();

    [InverseProperty("User")]
    public virtual ICollection<ExamAttempt> ExamAttempts { get; set; } = new List<ExamAttempt>();

    [InverseProperty("UploadedByNavigation")]
    public virtual ICollection<ImportBatch> ImportBatches { get; set; } = new List<ImportBatch>();

    [InverseProperty("User")]
    public virtual ICollection<LearnerLessonProgress> LearnerLessonProgresses { get; set; } = new List<LearnerLessonProgress>();

    [InverseProperty("User")]
    public virtual ICollection<LearnerPath> LearnerPaths { get; set; } = new List<LearnerPath>();

    /// <summary>#211: one stored state per skill the learner has been measured in.</summary>
    [InverseProperty("User")]
    public virtual ICollection<LearnerSkillState> LearnerSkillStates { get; set; } = new List<LearnerSkillState>();

    [InverseProperty("User")]
    public virtual Order? Order { get; set; }

    [InverseProperty("User")]
    public virtual ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();

    [InverseProperty("User")]
    public virtual ICollection<PlacementTest> PlacementTests { get; set; } = new List<PlacementTest>();

    [InverseProperty("User")]
    public virtual ICollection<PracticeSession> PracticeSessions { get; set; } = new List<PracticeSession>();

    [InverseProperty("User")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    [InverseProperty("User")]
    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    [ForeignKey("RoleId")]
    [InverseProperty("Users")]
    public virtual Role Role { get; set; } = null!;

    [InverseProperty("User")]
    public virtual ICollection<SavedQuestion> SavedQuestions { get; set; } = new List<SavedQuestion>();

    [InverseProperty("User")]
    public virtual ICollection<TargetBand> TargetBands { get; set; } = new List<TargetBand>();

    /// <summary>#155: one row per category this learner has ever answered.</summary>
    [InverseProperty("User")]
    public virtual ICollection<UserCategoryMastery> UserCategoryMasteries { get; set; } = new List<UserCategoryMastery>();

    /// <summary>#237: one row per cell this learner has ever answered.</summary>
    [InverseProperty("User")]
    public virtual ICollection<UserCellMastery> UserCellMasteries { get; set; } = new List<UserCellMastery>();

    /// <summary>#156: the learner's answers to calibration proposals.</summary>
    [InverseProperty("User")]
    public virtual ICollection<CalibrationDecision> CalibrationDecisions { get; set; } = new List<CalibrationDecision>();

    /// <summary>#159 review: micro-checks this learner has already had graded.</summary>
    [InverseProperty("User")]
    public virtual ICollection<LearnerLessonMicroCheck> LearnerLessonMicroChecks { get; set; } = new List<LearnerLessonMicroCheck>();

    /// <summary>#280: refunds made to this learner.</summary>
    [InverseProperty("User")]
    public virtual ICollection<Refund> RefundUsers { get; set; } = new List<Refund>();

    /// <summary>#280: refunds this account requested.</summary>
    [InverseProperty("RequestedByUser")]
    public virtual ICollection<Refund> RefundRequestedByUsers { get; set; } = new List<Refund>();

    /// <summary>#280: refunds this account approved or rejected.</summary>
    [InverseProperty("DecidedByUser")]
    public virtual ICollection<Refund> RefundDecidedByUsers { get; set; } = new List<Refund>();

    /// <summary>#280: refunds this account marked settled.</summary>
    [InverseProperty("SettledByUser")]
    public virtual ICollection<Refund> RefundSettledByUsers { get; set; } = new List<Refund>();
}
