using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

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
    public virtual ICollection<LearnerPath> LearnerPaths { get; set; } = new List<LearnerPath>();

    [InverseProperty("User")]
    public virtual ICollection<MentorProfile> MentorProfiles { get; set; } = new List<MentorProfile>();

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
}
