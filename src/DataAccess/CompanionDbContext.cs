using BusinessObjects.Entities;
using BusinessObjects.Views;
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


    /// <summary>#156: the learner's answers to a calibration proposal.</summary>
    public virtual DbSet<CalibrationDecision> CalibrationDecisions { get; set; }


    public virtual DbSet<Certificate> Certificates { get; set; }


    public virtual DbSet<Complaint> Complaints { get; set; }


    public virtual DbSet<CreditTransaction> CreditTransactions { get; set; }


    public virtual DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }


    public virtual DbSet<Exam> Exams { get; set; }


    public virtual DbSet<ExamAttempt> ExamAttempts { get; set; }


    public virtual DbSet<ExamSectionAudio> ExamSectionAudios { get; set; }


    public virtual DbSet<FreeResponse> FreeResponses { get; set; }


    public virtual DbSet<GradingCriterion> GradingCriteria { get; set; }


    // #192: the seven-band grid and the rubric lock.
    public virtual DbSet<GradingCriterionDescriptor> GradingCriterionDescriptors { get; set; }


    // #193: the calibrated benchmark responses injected into every grading call.
    public virtual DbSet<GradingCriterionAnchor> GradingCriterionAnchors { get; set; }


    public virtual DbSet<GradingRubricLock> GradingRubricLocks { get; set; }


    public virtual DbSet<ImportBatch> ImportBatches { get; set; }


    public virtual DbSet<ImportLog> ImportLogs { get; set; }


    public virtual DbSet<LearnerLessonProgress> LearnerLessonProgresses { get; set; }

    /// <summary>#159 review: micro-checks already graded, so one card cannot move mastery twice.</summary>
    public virtual DbSet<LearnerLessonMicroCheck> LearnerLessonMicroChecks { get; set; }


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


    public virtual DbSet<PaymentEvent> PaymentEvents { get; set; }


    public virtual DbSet<PlacementTest> PlacementTests { get; set; }


    public virtual DbSet<PracticeAnswer> PracticeAnswers { get; set; }


    public virtual DbSet<PracticeSession> PracticeSessions { get; set; }


    public virtual DbSet<Question> Questions { get; set; }


    public virtual DbSet<QuestionCategory> QuestionCategories { get; set; }

    /// <summary>#183: the twenty-topic axis. Certificate-agnostic, closed set.</summary>
    public virtual DbSet<Topic> Topics { get; set; }


    /// <summary>#184: the pool. A stimulus carries no exam, section or certificate.</summary>
    public virtual DbSet<Stimulus> Stimuli { get; set; }


    /// <summary>#184: a pool item, addressed by skill, level and topic - never by exam.</summary>
    public virtual DbSet<Item> Items { get; set; }


    /// <summary>#184: everything scorable, copy-on-write.</summary>
    public virtual DbSet<ItemVersion> ItemVersions { get; set; }


    /// <summary>#202 / D0.5: the per-cell content budget, one row per (skill, level).</summary>
    public virtual DbSet<ContentQuota> ContentQuotas { get; set; }


    /// <summary>#185: the assembly binding - which pinned version each exam puts on its paper.</summary>
    public virtual DbSet<ExamItem> ExamItems { get; set; }


    /// <summary>#199 / D0.7: one item's stored-answer verdict at one administration checkpoint.</summary>
    public virtual DbSet<ItemTelemetryEvaluation> ItemTelemetryEvaluations { get; set; }


    public virtual DbSet<QuestionGroup> QuestionGroups { get; set; }


    public virtual DbSet<QuestionOption> QuestionOptions { get; set; }


    public virtual DbSet<Recommendation> Recommendations { get; set; }


    public virtual DbSet<Review> Reviews { get; set; }


    public virtual DbSet<Permission> Permissions { get; set; }


    public virtual DbSet<Role> Roles { get; set; }


    public virtual DbSet<RolePermission> RolePermissions { get; set; }


    public virtual DbSet<SavedQuestion> SavedQuestions { get; set; }


    public virtual DbSet<ScoreBand> ScoreBands { get; set; }


    public virtual DbSet<ScoreScale> ScoreScales { get; set; }

    /// <summary>#210: the versioned per-skill CEFR cut-score table and its version headers.</summary>
    public virtual DbSet<CefrCutScore> CefrCutScores { get; set; }

    public virtual DbSet<CefrCutScoreVersion> CefrCutScoreVersions { get; set; }

    /// <summary>#211: one row per learner per skill — the answer every "what is my level" screen reads.</summary>
    public virtual DbSet<LearnerSkillState> LearnerSkillStates { get; set; }


    public virtual DbSet<Section> Sections { get; set; }


    public virtual DbSet<SectionCriterion> SectionCriteria { get; set; }


    public virtual DbSet<Skill> Skills { get; set; }


    public virtual DbSet<TargetBand> TargetBands { get; set; }


    public virtual DbSet<User> Users { get; set; }


    public virtual DbSet<UserCategoryMastery> UserCategoryMasteries { get; set; }


    public virtual DbSet<UserQuestionHistory> UserQuestionHistories { get; set; }


    public virtual DbSet<vw_MentorAvgRating> vw_MentorAvgRatings { get; set; }


    public virtual DbSet<vw_MentorPayoutTotal> vw_MentorPayoutTotals { get; set; }


    public virtual DbSet<vw_UserCategoryAccuracy> vw_UserCategoryAccuracies { get; set; }


    public virtual DbSet<vw_UserCreditBalance> vw_UserCreditBalances { get; set; }


    public virtual DbSet<vw_UserCreditSummary> vw_UserCreditSummaries { get; set; }


    public virtual DbSet<vw_UserPurchaseTotal> vw_UserPurchaseTotals { get; set; }


    public virtual DbSet<vw_UserSectionAccuracy> vw_UserSectionAccuracies { get; set; }


    public virtual DbSet<vw_UserSectionAccuracyAll> vw_UserSectionAccuracyAlls { get; set; }


    /// <summary>#202: one row per coverage cell, always all 120 of them.</summary>
    public virtual DbSet<vw_CoverageMatrix> vw_CoverageMatrices { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAuth(modelBuilder);
        ConfigureAuthoring(modelBuilder);
        ConfigureAttempts(modelBuilder);
        ConfigureCalibration(modelBuilder);
        ConfigurePractice(modelBuilder);
        ConfigureSkillState(modelBuilder);
        ConfigureFreeResponse(modelBuilder);
        ConfigureGuidedLearning(modelBuilder);
        ConfigureFinance(modelBuilder);
        ConfigureViews(modelBuilder);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
