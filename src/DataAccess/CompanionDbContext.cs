using System;
using System.Collections.Generic;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext : DbContext
{
    public CompanionDbContext(DbContextOptions<CompanionDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AiGrading> AiGradings { get; set; }

    public virtual DbSet<AiGradingScore> AiGradingScores { get; set; }

    public virtual DbSet<AiInsight> AiInsights { get; set; }

    public virtual DbSet<AttemptAnswer> AttemptAnswers { get; set; }

    public virtual DbSet<AttemptSkillScore> AttemptSkillScores { get; set; }

    public virtual DbSet<Booking> Bookings { get; set; }

    public virtual DbSet<Certificate> Certificates { get; set; }

    public virtual DbSet<Complaint> Complaints { get; set; }

    public virtual DbSet<CreditTransaction> CreditTransactions { get; set; }

    public virtual DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }

    public virtual DbSet<Exam> Exams { get; set; }

    public virtual DbSet<ExamAttempt> ExamAttempts { get; set; }

    public virtual DbSet<ExamSectionAudio> ExamSectionAudios { get; set; }

    public virtual DbSet<FreeResponse> FreeResponses { get; set; }

    public virtual DbSet<GradingCriterion> GradingCriteria { get; set; }

    public virtual DbSet<ImportBatch> ImportBatches { get; set; }

    public virtual DbSet<ImportLog> ImportLogs { get; set; }

    public virtual DbSet<LearnerLessonProgress> LearnerLessonProgresses { get; set; }

    public virtual DbSet<LearnerPath> LearnerPaths { get; set; }

    public virtual DbSet<LearnerPathStep> LearnerPathSteps { get; set; }

    public virtual DbSet<Lesson> Lessons { get; set; }

    public virtual DbSet<LessonCard> LessonCards { get; set; }

    public virtual DbSet<MentorPayout> MentorPayouts { get; set; }

    public virtual DbSet<MentorPayoutItem> MentorPayoutItems { get; set; }

    public virtual DbSet<MentorProfile> MentorProfiles { get; set; }

    public virtual DbSet<MentorSlot> MentorSlots { get; set; }

    public virtual DbSet<Order> Orders { get; set; }

    public virtual DbSet<Package> Packages { get; set; }

    public virtual DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

    public virtual DbSet<PathModule> PathModules { get; set; }

    public virtual DbSet<PathModuleItem> PathModuleItems { get; set; }

    public virtual DbSet<PathModuleTopic> PathModuleTopics { get; set; }

    public virtual DbSet<PathTemplate> PathTemplates { get; set; }

    public virtual DbSet<PaymentEvent> PaymentEvents { get; set; }

    public virtual DbSet<PlacementTest> PlacementTests { get; set; }

    public virtual DbSet<PracticeAnswer> PracticeAnswers { get; set; }

    public virtual DbSet<PracticeSession> PracticeSessions { get; set; }

    public virtual DbSet<Question> Questions { get; set; }

    public virtual DbSet<QuestionCategory> QuestionCategories { get; set; }

    public virtual DbSet<QuestionGroup> QuestionGroups { get; set; }

    public virtual DbSet<QuestionOption> QuestionOptions { get; set; }

    public virtual DbSet<Recommendation> Recommendations { get; set; }

    public virtual DbSet<Review> Reviews { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<SavedQuestion> SavedQuestions { get; set; }

    public virtual DbSet<ScoreBand> ScoreBands { get; set; }

    public virtual DbSet<ScoreScale> ScoreScales { get; set; }

    public virtual DbSet<Section> Sections { get; set; }

    public virtual DbSet<SectionCriterion> SectionCriteria { get; set; }

    public virtual DbSet<Skill> Skills { get; set; }

    public virtual DbSet<TargetBand> TargetBands { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<vw_LearnerPathIsPremium> vw_LearnerPathIsPremia { get; set; }

    public virtual DbSet<vw_MentorAvgRating> vw_MentorAvgRatings { get; set; }

    public virtual DbSet<vw_MentorPayoutTotal> vw_MentorPayoutTotals { get; set; }

    public virtual DbSet<vw_UserCategoryAccuracy> vw_UserCategoryAccuracies { get; set; }

    public virtual DbSet<vw_UserCreditBalance> vw_UserCreditBalances { get; set; }

    public virtual DbSet<vw_UserCreditSummary> vw_UserCreditSummaries { get; set; }

    public virtual DbSet<vw_UserPurchaseTotal> vw_UserPurchaseTotals { get; set; }

    public virtual DbSet<vw_UserSectionAccuracy> vw_UserSectionAccuracies { get; set; }

    public virtual DbSet<vw_UserSectionAccuracyAll> vw_UserSectionAccuracyAlls { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiGrading>(entity =>
        {
            entity.HasKey(e => e.GradingId).HasName("PK__AiGradin__773832AF85C52DBC");

            entity.ToTable(tb =>
                {
                    tb.HasTrigger("trg_AiGradings_MarkFreeResponseGraded");
                    tb.HasTrigger("trg_AiGradings_NotOnDraft");
                });

            entity.HasIndex(e => new { e.Status, e.RequestedAt }, "IX_AiGradings_Queue").HasFilter("([Status] IN ('pending', 'failed'))");

            entity.Property(e => e.AttemptCount).HasDefaultValue(1, "DF_AiGradings_AttemptCount");
            entity.Property(e => e.RequestedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("pending");

            entity.HasOne(d => d.FreeResponse).WithOne(p => p.AiGradingFreeResponse)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__AiGrading__FreeR__3DE82FB7");

            entity.HasOne(d => d.FreeResponseNavigation).WithMany(p => p.AiGradingFreeResponseNavigations)
                .HasPrincipalKey(p => new { p.FreeResponseId, p.SkillId })
                .HasForeignKey(d => new { d.FreeResponseId, d.SkillId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AiGradings_ResponseSkill");
        });

        modelBuilder.Entity<AiGradingScore>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_AiGradingScores_BoundedByMaxScore"));

            entity.HasOne(d => d.GradingCriterion).WithMany(p => p.AiGradingScores)
                .HasPrincipalKey(p => new { p.CriterionId, p.SkillId })
                .HasForeignKey(d => new { d.CriterionId, d.SkillId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AGS_CriterionSkill");

            entity.HasOne(d => d.AiGrading).WithMany(p => p.AiGradingScores)
                .HasPrincipalKey(p => new { p.GradingId, p.SkillId })
                .HasForeignKey(d => new { d.GradingId, d.SkillId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AGS_GradingSkill");
        });

        modelBuilder.Entity<AiInsight>(entity =>
        {
            entity.HasKey(e => e.InsightId).HasName("PK__AiInsigh__6A3F54F4E73AE5FA");

            entity.HasIndex(e => new { e.UserId, e.CertId, e.Scope, e.CreatedAt }, "IX_AiInsights_Latest")
                .IsDescending(false, false, false, true)
                .HasFilter("([Status]='done')");

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_AiInsights_Queue").HasFilter("([Status]='pending')");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_AiInsights_CreatedAt");
            entity.Property(e => e.SourceFingerprint).IsFixedLength();
            entity.Property(e => e.Status).HasDefaultValue("pending", "DF_AiInsights_Status");

            entity.HasOne(d => d.Cert).WithMany(p => p.AiInsights)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__AiInsight__CertI__53D770D6");

            entity.HasOne(d => d.User).WithMany(p => p.AiInsights)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__AiInsight__UserI__52E34C9D");

            entity.HasOne(d => d.ExamAttempt).WithMany(p => p.AiInsights)
                .HasPrincipalKey(p => new { p.AttemptId, p.UserId })
                .HasForeignKey(d => new { d.AttemptId, d.UserId })
                .HasConstraintName("FK_AiInsights_AttemptUser");

            entity.HasOne(d => d.LearnerPath).WithMany(p => p.AiInsights)
                .HasPrincipalKey(p => new { p.LearnerPathId, p.UserId })
                .HasForeignKey(d => new { d.LearnerPathId, d.UserId })
                .HasConstraintName("FK_AiInsights_PathUser");

            entity.HasOne(d => d.PracticeSession).WithMany(p => p.AiInsights)
                .HasPrincipalKey(p => new { p.SessionId, p.UserId })
                .HasForeignKey(d => new { d.PracticeSessionId, d.UserId })
                .HasConstraintName("FK_AiInsights_SessionUser");
        });

        modelBuilder.Entity<AttemptAnswer>(entity =>
        {
            entity.HasKey(e => e.AnswerId).HasName("PK__AttemptA__D48250049EDA465F");

            entity.HasOne(d => d.Attempt).WithMany(p => p.AttemptAnswerAttempts)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__AttemptAn__Attem__0F2D40CE");

            entity.HasOne(d => d.Question).WithMany(p => p.AttemptAnswerQuestions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__AttemptAn__Quest__10216507");

            entity.HasOne(d => d.ExamAttempt).WithMany(p => p.AttemptAnswerExamAttempts)
                .HasPrincipalKey(p => new { p.AttemptId, p.ExamId })
                .HasForeignKey(d => new { d.AttemptId, d.ExamId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttemptAnswers_AttemptExam");

            entity.HasOne(d => d.QuestionNavigation).WithMany(p => p.AttemptAnswerQuestionNavigations)
                .HasPrincipalKey(p => new { p.QuestionId, p.ExamId })
                .HasForeignKey(d => new { d.QuestionId, d.ExamId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttemptAnswers_QuestionExam");

            entity.HasOne(d => d.QuestionOption).WithMany(p => p.AttemptAnswers)
                .HasPrincipalKey(p => new { p.OptionId, p.QuestionId })
                .HasForeignKey(d => new { d.SelectedOptionId, d.QuestionId })
                .HasConstraintName("FK_AttemptAnswers_OptionQuestion");
        });

        modelBuilder.Entity<AttemptSkillScore>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_Attempts_TotalMatchesSkills"));

            entity.HasOne(d => d.Attempt).WithMany(p => p.AttemptSkillScoreAttempts)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__AttemptSk__Attem__0880433F");

            entity.HasOne(d => d.Skill).WithMany(p => p.AttemptSkillScoreSkills)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__AttemptSk__Skill__09746778");

            entity.HasOne(d => d.ExamAttempt).WithMany(p => p.AttemptSkillScoreExamAttempts)
                .HasPrincipalKey(p => new { p.AttemptId, p.CertId })
                .HasForeignKey(d => new { d.AttemptId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ASS_AttemptCert");

            entity.HasOne(d => d.SkillNavigation).WithMany(p => p.AttemptSkillScoreSkillNavigations)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ASS_SkillCert");
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(e => e.BookingId).HasName("PK__Bookings__73951AEDC046E1C8");

            entity.ToTable(tb => tb.HasTrigger("trg_Bookings_SlotStatusGuardAndSync"));

            entity.HasIndex(e => e.SlotId, "UX_Bookings_OnePerSlot")
                .IsUnique()
                .HasFilter("([SlotId] IS NOT NULL AND [Status]<>'cancelled')");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("pending");

            entity.HasOne(d => d.Mentor).WithMany(p => p.BookingMentors)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Bookings__Mentor__6C6E1476");

            entity.HasOne(d => d.Slot).WithOne(p => p.BookingSlot).HasConstraintName("FK__Bookings__SlotId__6D6238AF");

            entity.HasOne(d => d.User).WithMany(p => p.Bookings)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Bookings__UserId__6B79F03D");

            entity.HasOne(d => d.MentorProfile).WithMany(p => p.BookingMentorProfiles)
                .HasPrincipalKey(p => new { p.MentorId, p.Type })
                .HasForeignKey(d => new { d.MentorId, d.MentorType })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Bookings_MentorType");

            entity.HasOne(d => d.MentorSlot).WithMany(p => p.BookingMentorSlots)
                .HasPrincipalKey(p => new { p.SlotId, p.MentorId })
                .HasForeignKey(d => new { d.SlotId, d.MentorId })
                .HasConstraintName("FK_Bookings_SlotMentor");
        });

        modelBuilder.Entity<Certificate>(entity =>
        {
            entity.HasKey(e => e.CertId).HasName("PK__Certific__E5BD38C52927F6C0");

            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Complaint>(entity =>
        {
            entity.HasKey(e => e.ComplaintId).HasName("PK__Complain__740D898F85AEDFE4");

            entity.ToTable(tb => tb.HasTrigger("trg_Complaints_GrantMustBeAdminGrant"));

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("open");

            entity.HasOne(d => d.ResolvedByUser).WithMany(p => p.ComplaintResolvedByUsers).HasConstraintName("FK_Complaints_ResolvedBy");

            entity.HasOne(d => d.User).WithMany(p => p.ComplaintUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Complaint__UserI__7E8CC4B1");

            entity.HasOne(d => d.ExamAttempt).WithMany(p => p.Complaints)
                .HasPrincipalKey(p => new { p.AttemptId, p.UserId })
                .HasForeignKey(d => new { d.AttemptId, d.UserId })
                .HasConstraintName("FK_Complaints_AttemptUser");

            entity.HasOne(d => d.Booking).WithMany(p => p.Complaints)
                .HasPrincipalKey(p => new { p.BookingId, p.UserId })
                .HasForeignKey(d => new { d.BookingId, d.UserId })
                .HasConstraintName("FK_Complaints_BookingUser");

            entity.HasOne(d => d.Order).WithMany(p => p.Complaints)
                .HasPrincipalKey(p => new { p.OrderId, p.UserId })
                .HasForeignKey(d => new { d.OrderId, d.UserId })
                .HasConstraintName("FK_Complaints_OrderUser");

            entity.HasOne(d => d.CreditTransaction).WithMany(p => p.Complaints)
                .HasPrincipalKey(p => new { p.TxnId, p.UserId })
                .HasForeignKey(d => new { d.ResolvedByGrantTxnId, d.UserId })
                .HasConstraintName("FK_Complaints_GrantTxn");
        });

        modelBuilder.Entity<CreditTransaction>(entity =>
        {
            entity.HasKey(e => e.TxnId).HasName("PK__CreditTr__C19608544ABB5611");

            entity.ToTable(tb =>
                {
                    tb.HasTrigger("trg_CreditTransactions_AppendOnly");
                    tb.HasTrigger("trg_CreditTransactions_BookingAmounts");
                    tb.HasTrigger("trg_CreditTransactions_ChargeMatchesExamPurpose");
                    tb.HasTrigger("trg_CreditTransactions_NoNegativeBalance");
                    tb.HasTrigger("trg_CreditTransactions_NoRewrite");
                    tb.HasTrigger("trg_CreditTransactions_PurchaseMatchesOrder");
                });

            entity.HasIndex(e => new { e.AttemptId, e.Reason }, "UX_CreditTxn_Attempt_Reason")
                .IsUnique()
                .HasFilter("([AttemptId] IS NOT NULL)");

            entity.HasIndex(e => new { e.BookingId, e.Reason }, "UX_CreditTxn_Booking")
                .IsUnique()
                .HasFilter("([BookingId] IS NOT NULL)");

            entity.HasIndex(e => e.OrderId, "UX_CreditTxn_Order")
                .IsUnique()
                .HasFilter("([OrderId] IS NOT NULL)");

            entity.HasIndex(e => e.LearnerPathId, "UX_CreditTxn_Path")
                .IsUnique()
                .HasFilter("([LearnerPathId] IS NOT NULL)");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.AdminUser).WithMany(p => p.CreditTransactionAdminUsers).HasConstraintName("FK_CreditTxn_Admin");

            entity.HasOne(d => d.User).WithMany(p => p.CreditTransactionUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CreditTra__UserI__39AD8A7F");

            entity.HasOne(d => d.ExamAttempt).WithMany(p => p.CreditTransactions)
                .HasPrincipalKey(p => new { p.AttemptId, p.UserId })
                .HasForeignKey(d => new { d.AttemptId, d.UserId })
                .HasConstraintName("FK_CreditTxn_Attempt");

            entity.HasOne(d => d.Booking).WithMany(p => p.CreditTransactions)
                .HasPrincipalKey(p => new { p.BookingId, p.UserId })
                .HasForeignKey(d => new { d.BookingId, d.UserId })
                .HasConstraintName("FK_CreditTxn_Booking");

            entity.HasOne(d => d.LearnerPath).WithMany(p => p.CreditTransactions)
                .HasPrincipalKey(p => new { p.LearnerPathId, p.UserId })
                .HasForeignKey(d => new { d.LearnerPathId, d.UserId })
                .HasConstraintName("FK_CreditTxn_Path");

            entity.HasOne(d => d.Order).WithMany(p => p.CreditTransactions)
                .HasPrincipalKey(p => new { p.OrderId, p.UserId })
                .HasForeignKey(d => new { d.OrderId, d.UserId })
                .HasConstraintName("FK_CreditTxn_Order");
        });

        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasKey(e => e.TokenId).HasName("PK__EmailVer__658FEEEA5F8B9DDD");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_EmailVerificationTokens_CreatedAt");

            entity.HasOne(d => d.User).WithMany(p => p.EmailVerificationTokens)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_EmailVerificationTokens_User");
        });

        modelBuilder.Entity<Exam>(entity =>
        {
            entity.HasKey(e => e.ExamId).HasName("PK__Exams__297521C7A4367810");

            entity.ToTable(tb => tb.HasTrigger("trg_Exams_PurposeKeepsContent"));

            entity.HasIndex(e => new { e.CertId, e.Purpose, e.Status }, "IX_Exams_Cert_Status").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.CertId, e.Name }, "UX_Exams_Cert_Name")
                .IsUnique()
                .HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Purpose).HasDefaultValue("mock", "DF_Exams_Purpose");
            entity.Property(e => e.Status).HasDefaultValue("draft");

            entity.HasOne(d => d.Cert).WithMany(p => p.Exams)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Exams__CertId__160F4887");
        });

        modelBuilder.Entity<ExamAttempt>(entity =>
        {
            entity.HasKey(e => e.AttemptId).HasName("PK__ExamAtte__891A68E672B77157");

            entity.ToTable(tb =>
                {
                    tb.HasTrigger("trg_Attempts_NoBandForSpeakingPractice");
                    tb.HasTrigger("trg_Attempts_NoDraftsAfterSubmit");
                    tb.HasTrigger("trg_Attempts_NoGradedWithPendingFreeResponses");
                    tb.HasTrigger("trg_Attempts_TotalMatchesSkills_OnAttempt");
                    tb.HasTrigger("trg_ExamAttempts_PublishedExamOnly");
                });

            entity.Property(e => e.StartedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("in_progress");

            entity.HasOne(d => d.Cert).WithMany(p => p.ExamAttempts)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ExamAttem__CertI__7EF6D905");

            entity.HasOne(d => d.Exam).WithMany(p => p.ExamAttemptExams)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ExamAttem__ExamI__7E02B4CC");

            entity.HasOne(d => d.User).WithMany(p => p.ExamAttempts)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ExamAttem__UserI__7D0E9093");

            entity.HasOne(d => d.ExamNavigation).WithMany(p => p.ExamAttemptExamNavigations)
                .HasPrincipalKey(p => new { p.ExamId, p.CertId })
                .HasForeignKey(d => new { d.ExamId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Attempts_ExamCert");

            entity.HasOne(d => d.Question).WithMany(p => p.ExamAttempts)
                .HasPrincipalKey(p => new { p.QuestionId, p.ExamId })
                .HasForeignKey(d => new { d.LastQuestionId, d.ExamId })
                .HasConstraintName("FK_Attempts_LastQuestionExam");
        });

        modelBuilder.Entity<ExamSectionAudio>(entity =>
        {
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.Exam).WithMany(p => p.ExamSectionAudios)
                .HasPrincipalKey(p => new { p.ExamId, p.CertId })
                .HasForeignKey(d => new { d.ExamId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ExamSectionAudio_ExamCert");

            entity.HasOne(d => d.Section).WithMany(p => p.ExamSectionAudios)
                .HasPrincipalKey(p => new { p.SectionId, p.CertId })
                .HasForeignKey(d => new { d.SectionId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ExamSectionAudio_SectionCert");
        });

        modelBuilder.Entity<FreeResponse>(entity =>
        {
            entity.HasKey(e => e.FreeResponseId).HasName("PK__FreeResp__239C05117F02D927");

            entity.ToTable(tb =>
                {
                    tb.HasTrigger("trg_FreeResponses_DraftOnlyInProgress");
                    tb.HasTrigger("trg_FreeResponses_NotIntoGradedAttempt");
                });

            entity.HasIndex(e => e.SubmittedAt, "IX_FreeResponses_Pending").HasFilter("([Status]='pending_ai')");

            entity.Property(e => e.Status).HasDefaultValue("pending_ai");
            entity.Property(e => e.SubmittedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.Attempt).WithMany(p => p.FreeResponseAttempts)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__FreeRespo__Attem__18B6AB08");

            entity.HasOne(d => d.Question).WithMany(p => p.FreeResponseQuestions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__FreeRespo__Quest__19AACF41");

            entity.HasOne(d => d.ExamAttempt).WithMany(p => p.FreeResponseExamAttempts)
                .HasPrincipalKey(p => new { p.AttemptId, p.ExamId })
                .HasForeignKey(d => new { d.AttemptId, d.ExamId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FreeResp_AttemptExam");

            entity.HasOne(d => d.QuestionNavigation).WithMany(p => p.FreeResponseQuestionNavigations)
                .HasPrincipalKey(p => new { p.QuestionId, p.ExamId })
                .HasForeignKey(d => new { d.QuestionId, d.ExamId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FreeResp_QuestionExam");

            entity.HasOne(d => d.Question1).WithMany(p => p.FreeResponseQuestion1s)
                .HasPrincipalKey(p => new { p.QuestionId, p.QuestionType })
                .HasForeignKey(d => new { d.QuestionId, d.QuestionType })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FreeResp_QuestionType");

            entity.HasOne(d => d.Question2).WithMany(p => p.FreeResponseQuestion2s)
                .HasPrincipalKey(p => new { p.QuestionId, p.SkillId })
                .HasForeignKey(d => new { d.QuestionId, d.SkillId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FreeResp_QuestionSkill");
        });

        modelBuilder.Entity<GradingCriterion>(entity =>
        {
            entity.HasKey(e => e.CriterionId).HasName("PK__GradingC__647C3BB12364CBC3");

            entity.ToTable(tb => tb.HasTrigger("trg_GradingCriteria_MaxScoreNotBelowStored"));

            entity.Property(e => e.Weight).HasDefaultValue(1.0m);

            entity.HasOne(d => d.Cert).WithMany(p => p.GradingCriteria)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__GradingCr__CertI__308E3499");

            entity.HasOne(d => d.Skill).WithMany(p => p.GradingCriteria)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GradingCriteria_SkillCert");
        });

        modelBuilder.Entity<ImportBatch>(entity =>
        {
            entity.HasKey(e => e.BatchId).HasName("PK__ImportBa__5D55CE589E3B37D5");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("pending");

            entity.HasOne(d => d.Cert).WithMany(p => p.ImportBatches)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ImportBat__CertI__4D5F7D71");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.ImportBatches)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ImportBat__Uploa__503BEA1C");
        });

        modelBuilder.Entity<ImportLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("PK__ImportLo__5E548648FBF92143");

            entity.Property(e => e.LoggedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.Batch).WithMany(p => p.ImportLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ImportLog__Batch__540C7B00");
        });

        modelBuilder.Entity<LearnerLessonProgress>(entity =>
        {
            entity.HasKey(e => e.ProgressId).HasName("PK__LearnerL__BAE29CA5C616E5E3");

            entity.Property(e => e.FirstOpenedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.LastViewedCard).WithMany(p => p.LearnerLessonProgresses).HasConstraintName("FK__LearnerLe__LastV__4924D839");

            entity.HasOne(d => d.User).WithMany(p => p.LearnerLessonProgresses)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__LearnerLe__UserI__4830B400");

            entity.HasOne(d => d.Lesson).WithMany(p => p.LearnerLessonProgresses)
                .HasPrincipalKey(p => new { p.LessonId, p.CertId })
                .HasForeignKey(d => new { d.LessonId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LLP_LessonCert");
        });

        modelBuilder.Entity<LearnerPath>(entity =>
        {
            entity.HasKey(e => e.LearnerPathId).HasName("PK__LearnerP__166F32282358C553");

            entity.HasIndex(e => new { e.UserId, e.CertId }, "UX_LearnerPaths_OneActive")
                .IsUnique()
                .HasFilter("([Status]='active')");

            entity.Property(e => e.StartedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("active");

            entity.HasOne(d => d.Cert).WithMany(p => p.LearnerPaths)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__LearnerPa__CertI__26CFC035");

            entity.HasOne(d => d.User).WithMany(p => p.LearnerPaths)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__LearnerPa__UserI__25DB9BFC");

            entity.HasOne(d => d.PlacementTest).WithMany(p => p.LearnerPaths)
                .HasPrincipalKey(p => new { p.PlacementId, p.UserId })
                .HasForeignKey(d => new { d.PlacementId, d.UserId })
                .HasConstraintName("FK_LearnerPaths_PlacementUser");

            entity.HasOne(d => d.PathTemplate).WithMany(p => p.LearnerPaths)
                .HasPrincipalKey(p => new { p.TemplateId, p.CertId })
                .HasForeignKey(d => new { d.TemplateId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LearnerPaths_TemplateCert");
        });

        modelBuilder.Entity<LearnerPathStep>(entity =>
        {
            entity.HasKey(e => e.StepId).HasName("PK__LearnerP__24343357410D371C");

            entity.Property(e => e.Status).HasDefaultValue("locked");

            entity.HasOne(d => d.LearnerPath).WithMany(p => p.LearnerPathSteps)
                .HasPrincipalKey(p => new { p.LearnerPathId, p.TemplateId })
                .HasForeignKey(d => new { d.LearnerPathId, d.TemplateId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Steps_PathTemplate");

            entity.HasOne(d => d.PathModuleItem).WithMany(p => p.LearnerPathSteps)
                .HasPrincipalKey(p => new { p.ItemId, p.TemplateId })
                .HasForeignKey(d => new { d.PathModuleItemId, d.TemplateId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Steps_ItemTemplate");
        });

        modelBuilder.Entity<Lesson>(entity =>
        {
            entity.HasKey(e => e.LessonId).HasName("PK__Lessons__B084ACD044B3F97E");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.EstimatedMinutes).HasDefaultValue(5);

            entity.HasOne(d => d.Category).WithMany(p => p.Lessons).HasConstraintName("FK__Lessons__Categor__38EE7070");

            entity.HasOne(d => d.Cert).WithMany(p => p.Lessons)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Lessons__CertId__37FA4C37");

            entity.HasOne(d => d.Skill).WithMany(p => p.Lessons)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Lessons_SkillCert");
        });

        modelBuilder.Entity<LessonCard>(entity =>
        {
            entity.HasKey(e => e.CardId).HasName("PK__LessonCa__55FECDAE5025293F");

            entity.Property(e => e.CorrectOption).IsFixedLength();

            entity.HasOne(d => d.Lesson).WithMany(p => p.LessonCards)
                .HasPrincipalKey(p => new { p.LessonId, p.CertId })
                .HasForeignKey(d => new { d.LessonId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LessonCards_LessonCert");
        });

        modelBuilder.Entity<MentorPayout>(entity =>
        {
            entity.HasKey(e => e.PayoutId).HasName("PK__MentorPa__35C3DF8E859EADFC");

            entity.Property(e => e.Status).HasDefaultValue("pending");

            entity.HasOne(d => d.Mentor).WithMany(p => p.MentorPayouts)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__MentorPay__Mento__0CDAE408");
        });

        modelBuilder.Entity<MentorPayoutItem>(entity =>
        {
            entity.HasKey(e => new { e.PayoutId, e.BookingId }).HasName("PK_PayoutItems");

            entity.HasOne(d => d.Booking).WithMany(p => p.MentorPayoutItems)
                .HasPrincipalKey(p => new { p.BookingId, p.MentorId })
                .HasForeignKey(d => new { d.BookingId, d.MentorId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PayoutItems_BookingMentor");

            entity.HasOne(d => d.MentorPayout).WithMany(p => p.MentorPayoutItems)
                .HasPrincipalKey(p => new { p.PayoutId, p.MentorId })
                .HasForeignKey(d => new { d.PayoutId, d.MentorId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PayoutItems_PayoutMentor");
        });

        modelBuilder.Entity<MentorProfile>(entity =>
        {
            entity.HasKey(e => e.MentorId).HasName("PK__MentorPr__053B7E982DEF0306");

            entity.HasIndex(e => e.CertId, "IX_Mentors_Cert").HasFilter("([IsActive]=(1))");

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PriceCredits).HasDefaultValue(1, "DF_Mentor_PriceCredits");
            entity.Property(e => e.SessionMinutes).HasDefaultValue(60, "DF_Mentor_SessionMinutes");

            entity.HasOne(d => d.Cert).WithMany(p => p.MentorProfiles)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__MentorPro__CertI__567ED357");

            entity.HasOne(d => d.Skill).WithMany(p => p.MentorProfileSkills).HasConstraintName("FK__MentorPro__Skill__5772F790");

            entity.HasOne(d => d.User).WithMany(p => p.MentorProfiles).HasConstraintName("FK__MentorPro__UserI__558AAF1E");

            entity.HasOne(d => d.SkillNavigation).WithMany(p => p.MentorProfileSkillNavigations)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .HasConstraintName("FK_Mentor_SkillCert");
        });

        modelBuilder.Entity<MentorSlot>(entity =>
        {
            entity.HasKey(e => e.SlotId).HasName("PK__MentorSl__0A124AAFDF2C4576");

            entity.Property(e => e.Status).HasDefaultValue("open");

            entity.HasOne(d => d.Mentor).WithMany(p => p.MentorSlots)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__MentorSlo__Mento__63D8CE75");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.OrderId).HasName("PK__Orders__C3905BCFA292CBC6");

            entity.ToTable(tb => tb.HasTrigger("trg_Orders_CreditedStaysPaid"));

            entity.HasIndex(e => e.CreatedAt, "IX_Orders_Open").HasFilter("([Status] IN ('pending', 'reconciling'))");

            entity.HasIndex(e => new { e.Gateway, e.GatewayTxnRef }, "UX_Orders_GatewayTxnRef")
                .IsUnique()
                .HasFilter("([GatewayTxnRef] IS NOT NULL)");

            entity.HasIndex(e => e.UserId, "UX_Orders_OnePendingPerUser")
                .IsUnique()
                .HasFilter("([Status] IN ('pending', 'reconciling'))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("pending");

            entity.HasOne(d => d.Package).WithMany(p => p.Orders)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Orders__PackageI__25A691D2");

            entity.HasOne(d => d.User).WithOne(p => p.Order)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Orders__UserId__24B26D99");
        });

        modelBuilder.Entity<Package>(entity =>
        {
            entity.HasKey(e => e.PackageId).HasName("PK__Packages__322035CCF93142FE");

            entity.HasIndex(e => e.IsFeatured, "UX_Packages_OneFeatured")
                .IsUnique()
                .HasFilter("([IsFeatured]=(1))");

            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.TokenId).HasName("PK__Password__658FEEEA66FD11FE");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_PasswordResetTokens_CreatedAt");

            entity.HasOne(d => d.User).WithMany(p => p.PasswordResetTokens)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PasswordResetTokens_User");
        });

        modelBuilder.Entity<PathModule>(entity =>
        {
            entity.HasKey(e => e.ModuleId).HasName("PK__PathModu__2B7477A71FBFA640");

            entity.HasOne(d => d.Skill).WithMany(p => p.PathModules)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PathModules_SkillCert");

            entity.HasOne(d => d.PathTemplate).WithMany(p => p.PathModules)
                .HasPrincipalKey(p => new { p.TemplateId, p.CertId })
                .HasForeignKey(d => new { d.TemplateId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PathModules_TemplateCert");
        });

        modelBuilder.Entity<PathModuleItem>(entity =>
        {
            entity.HasKey(e => e.ItemId).HasName("PK__PathModu__727E838B42341F73");

            entity.ToTable(tb => tb.HasTrigger("trg_PMI_AiTaskRefsSkillPracticeExam"));

            entity.HasOne(d => d.PathModule).WithMany(p => p.PathModuleItemPathModules)
                .HasPrincipalKey(p => new { p.ModuleId, p.CertId })
                .HasForeignKey(d => new { d.ModuleId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PMI_ModuleCert");

            entity.HasOne(d => d.PathModuleNavigation).WithMany(p => p.PathModuleItemPathModuleNavigations)
                .HasPrincipalKey(p => new { p.ModuleId, p.TemplateId })
                .HasForeignKey(d => new { d.ModuleId, d.TemplateId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PMI_ModuleTemplate");

            entity.HasOne(d => d.Exam).WithMany(p => p.PathModuleItems)
                .HasPrincipalKey(p => new { p.ExamId, p.CertId })
                .HasForeignKey(d => new { d.RefExamId, d.CertId })
                .HasConstraintName("FK_PMI_ExamCert");

            entity.HasOne(d => d.Lesson).WithMany(p => p.PathModuleItems)
                .HasPrincipalKey(p => new { p.LessonId, p.CertId })
                .HasForeignKey(d => new { d.RefLessonId, d.CertId })
                .HasConstraintName("FK_PMI_LessonCert");

            entity.HasOne(d => d.Section).WithMany(p => p.PathModuleItems)
                .HasPrincipalKey(p => new { p.SectionId, p.CertId })
                .HasForeignKey(d => new { d.RefSectionId, d.CertId })
                .HasConstraintName("FK_PMI_SectionCert");
        });

        modelBuilder.Entity<PathModuleTopic>(entity =>
        {
            entity.HasKey(e => e.TopicId).HasName("PK__PathModu__022E0F5D1DBB1B56");

            entity.HasOne(d => d.Category).WithMany(p => p.PathModuleTopics).HasConstraintName("FK__PathModul__Categ__1F2E9E6D");

            entity.HasOne(d => d.Module).WithMany(p => p.PathModuleTopics)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PathModul__Modul__1E3A7A34");
        });

        modelBuilder.Entity<PathTemplate>(entity =>
        {
            entity.HasKey(e => e.TemplateId).HasName("PK__PathTemp__F87ADD2726DC6DA2");

            entity.HasOne(d => d.Cert).WithMany(p => p.PathTemplates)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PathTempl__CertI__056ECC6A");

            entity.HasOne(d => d.ScoreBand).WithMany(p => p.PathTemplateScoreBands)
                .HasPrincipalKey(p => new { p.CertId, p.Code })
                .HasForeignKey(d => new { d.CertId, d.FromBandCode })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PathTpl_FromBand");

            entity.HasOne(d => d.ScoreBandNavigation).WithMany(p => p.PathTemplateScoreBandNavigations)
                .HasPrincipalKey(p => new { p.CertId, p.Code })
                .HasForeignKey(d => new { d.CertId, d.ToBandCode })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PathTpl_ToBand");
        });

        modelBuilder.Entity<PaymentEvent>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK__PaymentE__7944C8104787C6C1");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_PaymentEvents_CreatedAt");

            entity.HasOne(d => d.Order).WithMany(p => p.PaymentEvents)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PaymentEv__Order__2E3BD7D3");
        });

        modelBuilder.Entity<PlacementTest>(entity =>
        {
            entity.HasKey(e => e.PlacementId).HasName("PK__Placemen__2E328C652BD86022");

            entity.ToTable(tb => tb.HasTrigger("trg_PlacementTests_SnapshotMatchesAttempt"));

            entity.HasIndex(e => e.AttemptId, "UX_PlacementTests_Attempt")
                .IsUnique()
                .HasFilter("([AttemptId] IS NOT NULL)");

            entity.Property(e => e.ExamPurpose).HasDefaultValue("placement", "DF_Placement_ExamPurpose");
            entity.Property(e => e.Status).HasDefaultValue("in_progress");
            entity.Property(e => e.TakenAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.Cert).WithMany(p => p.PlacementTests)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Placement__CertI__725BF7F6");

            entity.HasOne(d => d.User).WithMany(p => p.PlacementTests)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Placement__UserI__7167D3BD");

            entity.HasOne(d => d.ExamAttempt).WithMany(p => p.PlacementTests)
                .HasPrincipalKey(p => new { p.AttemptId, p.UserId })
                .HasForeignKey(d => new { d.AttemptId, d.UserId })
                .HasConstraintName("FK_Placement_AttemptUser");

            entity.HasOne(d => d.Exam).WithMany(p => p.PlacementTestExams)
                .HasPrincipalKey(p => new { p.ExamId, p.CertId })
                .HasForeignKey(d => new { d.ExamId, d.CertId })
                .HasConstraintName("FK_Placement_ExamCert");

            entity.HasOne(d => d.ExamNavigation).WithMany(p => p.PlacementTestExamNavigations)
                .HasPrincipalKey(p => new { p.ExamId, p.CertId, p.Purpose })
                .HasForeignKey(d => new { d.ExamId, d.CertId, d.ExamPurpose })
                .HasConstraintName("FK_Placement_ExamPurpose");
        });

        modelBuilder.Entity<PracticeAnswer>(entity =>
        {
            entity.HasKey(e => e.AnswerId).HasName("PK__Practice__D48250043F262F2D");

            entity.HasOne(d => d.Question).WithMany(p => p.PracticeAnswerQuestions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PracticeA__Quest__6CD828CA");

            entity.HasOne(d => d.Session).WithMany(p => p.PracticeAnswerSessions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PracticeA__Sessi__6BE40491");

            entity.HasOne(d => d.QuestionNavigation).WithMany(p => p.PracticeAnswerQuestionNavigations)
                .HasPrincipalKey(p => new { p.QuestionId, p.SectionId })
                .HasForeignKey(d => new { d.QuestionId, d.SectionId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PracticeAnswers_QuestionSection");

            entity.HasOne(d => d.QuestionOption).WithMany(p => p.PracticeAnswers)
                .HasPrincipalKey(p => new { p.OptionId, p.QuestionId })
                .HasForeignKey(d => new { d.SelectedOptionId, d.QuestionId })
                .HasConstraintName("FK_PracticeAnswers_OptionQuestion");

            entity.HasOne(d => d.PracticeSession).WithMany(p => p.PracticeAnswerPracticeSessions)
                .HasPrincipalKey(p => new { p.SessionId, p.SectionId })
                .HasForeignKey(d => new { d.SessionId, d.SectionId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PracticeAnswers_SessionSection");
        });

        modelBuilder.Entity<PracticeSession>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("PK__Practice__C9F4929081A3986C");

            entity.HasIndex(e => e.LearnerPathStepId, "IX_Practice_PathStep").HasFilter("([LearnerPathStepId] IS NOT NULL)");

            entity.Property(e => e.FeedbackMode).HasDefaultValue("immediate", "DF_Practice_Feedback");
            entity.Property(e => e.QuestionSource).HasDefaultValue("all", "DF_Practice_Source");
            entity.Property(e => e.StartedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("in_progress");

            entity.HasOne(d => d.LearnerPathStep).WithMany(p => p.PracticeSessions).HasConstraintName("FK_Practice_PathStep");

            entity.HasOne(d => d.Section).WithMany(p => p.PracticeSessionSections)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PracticeS__Secti__5D95E53A");

            entity.HasOne(d => d.User).WithMany(p => p.PracticeSessions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PracticeS__UserI__5CA1C101");

            entity.HasOne(d => d.SectionNavigation).WithMany(p => p.PracticeSessionSectionNavigations)
                .HasPrincipalKey(p => new { p.SectionId, p.Modality })
                .HasForeignKey(d => new { d.SectionId, d.Modality })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Practice_SectionModality");
        });

        modelBuilder.Entity<Question>(entity =>
        {
            entity.HasKey(e => e.QuestionId).HasName("PK__Question__0DC06FACD56E3AE0");

            entity.HasIndex(e => e.CategoryId, "IX_Questions_Category").HasFilter("([CategoryId] IS NOT NULL)");

            entity.HasOne(d => d.Category).WithMany(p => p.Questions).HasConstraintName("FK_Questions_Category");

            entity.HasOne(d => d.Group).WithMany(p => p.QuestionGroups)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Questions__Group__3A4CA8FD");

            entity.HasOne(d => d.QuestionGroup).WithMany(p => p.QuestionQuestionGroups)
                .HasPrincipalKey(p => new { p.GroupId, p.ExamId })
                .HasForeignKey(d => new { d.GroupId, d.ExamId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Questions_GroupExam");

            entity.HasOne(d => d.QuestionGroupNavigation).WithMany(p => p.QuestionQuestionGroupNavigations)
                .HasPrincipalKey(p => new { p.GroupId, p.SectionId })
                .HasForeignKey(d => new { d.GroupId, d.SectionId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Questions_GroupSection");

            entity.HasOne(d => d.Section).WithMany(p => p.Questions)
                .HasPrincipalKey(p => new { p.SectionId, p.SkillId })
                .HasForeignKey(d => new { d.SectionId, d.SkillId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Questions_SectionSkill");
        });

        modelBuilder.Entity<QuestionCategory>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PK__Question__19093A0B5AA3959D");

            entity.HasOne(d => d.Cert).WithMany(p => p.QuestionCategories)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__QuestionC__CertI__29221CFB");
        });

        modelBuilder.Entity<QuestionGroup>(entity =>
        {
            entity.HasKey(e => e.GroupId).HasName("PK__Question__149AF36A03E95EE7");

            entity.ToTable(tb => tb.HasTrigger("trg_QGroups_SkillPracticeOneModality"));

            entity.HasOne(d => d.Exam).WithMany(p => p.QuestionGroups)
                .HasPrincipalKey(p => new { p.ExamId, p.CertId })
                .HasForeignKey(d => new { d.ExamId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_QGroups_ExamCert");

            entity.HasOne(d => d.Section).WithMany(p => p.QuestionGroups)
                .HasPrincipalKey(p => new { p.SectionId, p.CertId })
                .HasForeignKey(d => new { d.SectionId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_QGroups_SectionCert");
        });

        modelBuilder.Entity<QuestionOption>(entity =>
        {
            entity.HasKey(e => e.OptionId).HasName("PK__Question__92C7A1FFB8F0027D");

            entity.HasIndex(e => e.QuestionId, "UX_Options_OneCorrect")
                .IsUnique()
                .HasFilter("([IsCorrect]=(1))");

            entity.HasOne(d => d.Question).WithOne(p => p.QuestionOptionQuestion)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__QuestionO__Quest__47A6A41B");

            entity.HasOne(d => d.QuestionNavigation).WithMany(p => p.QuestionOptionQuestionNavigations)
                .HasPrincipalKey(p => new { p.QuestionId, p.QuestionType })
                .HasForeignKey(d => new { d.QuestionId, d.QuestionType })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Options_QuestionType");
        });

        modelBuilder.Entity<Recommendation>(entity =>
        {
            entity.HasKey(e => e.RecId).HasName("PK__Recommen__360414DF9A660680");

            entity.HasIndex(e => e.AttemptId, "IX_Recommendations_Attempt").HasFilter("([AttemptId] IS NOT NULL)");

            entity.HasIndex(e => e.PracticeSessionId, "IX_Recommendations_Session").HasFilter("([PracticeSessionId] IS NOT NULL)");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Severity).HasDefaultValue("info");

            entity.HasOne(d => d.Cert).WithMany(p => p.Recommendations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Recommend__CertI__6225902D");

            entity.HasOne(d => d.Grading).WithMany(p => p.Recommendations).HasConstraintName("FK__Recommend__Gradi__6319B466");

            entity.HasOne(d => d.User).WithMany(p => p.Recommendations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Recommend__UserI__61316BF4");

            entity.HasOne(d => d.ExamAttempt).WithMany(p => p.Recommendations)
                .HasPrincipalKey(p => new { p.AttemptId, p.UserId })
                .HasForeignKey(d => new { d.AttemptId, d.UserId })
                .HasConstraintName("FK_Reco_AttemptUser");

            entity.HasOne(d => d.ScoreBand).WithMany(p => p.Recommendations)
                .HasPrincipalKey(p => new { p.CertId, p.Code })
                .HasForeignKey(d => new { d.CertId, d.TargetBandCode })
                .HasConstraintName("FK_Reco_TargetBand");

            entity.HasOne(d => d.AiInsight).WithMany(p => p.Recommendations)
                .HasPrincipalKey(p => new { p.InsightId, p.UserId })
                .HasForeignKey(d => new { d.InsightId, d.UserId })
                .HasConstraintName("FK_Reco_InsightUser");

            entity.HasOne(d => d.PracticeSession).WithMany(p => p.Recommendations)
                .HasPrincipalKey(p => new { p.SessionId, p.UserId })
                .HasForeignKey(d => new { d.PracticeSessionId, d.UserId })
                .HasConstraintName("FK_Reco_SessionUser");

            entity.HasOne(d => d.Skill).WithMany(p => p.Recommendations)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .HasConstraintName("FK_Reco_SkillCert");
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(e => e.ReviewId).HasName("PK__Reviews__74BC79CE80FE2BB3");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.User).WithMany(p => p.Reviews)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Reviews__UserId__78D3EB5B");

            entity.HasOne(d => d.Booking).WithMany(p => p.Reviews)
                .HasPrincipalKey(p => new { p.BookingId, p.UserId })
                .HasForeignKey(d => new { d.BookingId, d.UserId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Reviews_BookingUser");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE1AE028D7CB");
        });

        modelBuilder.Entity<SavedQuestion>(entity =>
        {
            entity.Property(e => e.SavedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.Question).WithMany(p => p.SavedQuestions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__SavedQues__Quest__756D6ECB");

            entity.HasOne(d => d.User).WithMany(p => p.SavedQuestions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__SavedQues__UserI__74794A92");
        });

        modelBuilder.Entity<ScoreBand>(entity =>
        {
            entity.HasKey(e => e.BandId).HasName("PK__ScoreBan__A03693A89112EB10");

            entity.ToTable(tb => tb.HasTrigger("trg_ScoreBands_NoOverlap"));

            entity.HasOne(d => d.Cert).WithMany(p => p.ScoreBands)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ScoreBand__CertI__09A971A2");
        });

        modelBuilder.Entity<ScoreScale>(entity =>
        {
            entity.HasKey(e => e.ScaleId).HasName("PK__ScoreSca__27D595667A08EBC9");

            entity.HasOne(d => d.Cert).WithMany(p => p.ScoreScales)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ScoreScal__CertI__10566F31");

            entity.HasOne(d => d.Exam).WithMany(p => p.ScoreScales)
                .HasPrincipalKey(p => new { p.ExamId, p.CertId })
                .HasForeignKey(d => new { d.ExamId, d.CertId })
                .HasConstraintName("FK_ScoreScales_Exam");

            entity.HasOne(d => d.Skill).WithMany(p => p.ScoreScales)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .HasConstraintName("FK_ScoreScales_SkillCert");
        });

        modelBuilder.Entity<Section>(entity =>
        {
            entity.HasKey(e => e.SectionId).HasName("PK__Sections__80EF0872ACBB3BA8");

            entity.HasOne(d => d.Skill).WithMany(p => p.SectionSkills)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Sections_SkillCert");

            entity.HasOne(d => d.SkillNavigation).WithMany(p => p.SectionSkillNavigations)
                .HasPrincipalKey(p => new { p.SkillId, p.Modality })
                .HasForeignKey(d => new { d.SkillId, d.Modality })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Sections_SkillModality");
        });

        modelBuilder.Entity<SectionCriterion>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_SectionCriteria_MaxScoreNotBelowStored"));

            entity.HasOne(d => d.GradingCriterion).WithMany(p => p.SectionCriteria)
                .HasPrincipalKey(p => new { p.CriterionId, p.SkillId })
                .HasForeignKey(d => new { d.CriterionId, d.SkillId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SectionCriteria_CriterionSkill");

            entity.HasOne(d => d.Section).WithMany(p => p.SectionCriteria)
                .HasPrincipalKey(p => new { p.SectionId, p.SkillId })
                .HasForeignKey(d => new { d.SectionId, d.SkillId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SectionCriteria_SectionSkill");
        });

        modelBuilder.Entity<Skill>(entity =>
        {
            entity.HasKey(e => e.SkillId).HasName("PK__Skills__DFA09187AB75DC71");

            entity.HasOne(d => d.Cert).WithMany(p => p.Skills)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Skills__CertId__6383C8BA");
        });

        modelBuilder.Entity<TargetBand>(entity =>
        {
            entity.Property(e => e.SetAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.Cert).WithMany(p => p.TargetBands)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TargetBan__CertI__7FB5F314");

            entity.HasOne(d => d.User).WithMany(p => p.TargetBands)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TargetBan__UserI__7EC1CEDB");

            entity.HasOne(d => d.ScoreBand).WithMany(p => p.TargetBands)
                .HasPrincipalKey(p => new { p.CertId, p.Code })
                .HasForeignKey(d => new { d.CertId, d.TargetBandCode })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TargetBands_Band");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C33908FE3");

            entity.HasIndex(e => e.Email, "UX_Users_Email")
                .IsUnique()
                .HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_Users_CreatedAt");
            entity.Property(e => e.SecurityStamp).HasDefaultValueSql("(CONVERT([varchar](36),newid()))", "DF_Users_SecurityStamp");
            entity.Property(e => e.TimeZoneId).HasDefaultValue("Asia/Ho_Chi_Minh", "DF_Users_TimeZoneId");

            entity.HasOne(d => d.ActiveCert).WithMany(p => p.Users).HasConstraintName("FK_Users_ActiveCert");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Users_Role");
        });

        modelBuilder.Entity<vw_LearnerPathIsPremium>(entity =>
        {
            entity.ToView("vw_LearnerPathIsPremium");

            entity.Property(e => e.LearnerPathId).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<vw_MentorAvgRating>(entity =>
        {
            entity.ToView("vw_MentorAvgRating");
        });

        modelBuilder.Entity<vw_MentorPayoutTotal>(entity =>
        {
            entity.ToView("vw_MentorPayoutTotals");
        });

        modelBuilder.Entity<vw_UserCategoryAccuracy>(entity =>
        {
            entity.ToView("vw_UserCategoryAccuracy");
        });

        modelBuilder.Entity<vw_UserCreditBalance>(entity =>
        {
            entity.ToView("vw_UserCreditBalance");
        });

        modelBuilder.Entity<vw_UserCreditSummary>(entity =>
        {
            entity.ToView("vw_UserCreditSummary");
        });

        modelBuilder.Entity<vw_UserPurchaseTotal>(entity =>
        {
            entity.ToView("vw_UserPurchaseTotals");
        });

        modelBuilder.Entity<vw_UserSectionAccuracy>(entity =>
        {
            entity.ToView("vw_UserSectionAccuracy");
        });

        modelBuilder.Entity<vw_UserSectionAccuracyAll>(entity =>
        {
            entity.ToView("vw_UserSectionAccuracyAll");
        });
        modelBuilder.HasSequence("SEQ_PaymentNo");

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
