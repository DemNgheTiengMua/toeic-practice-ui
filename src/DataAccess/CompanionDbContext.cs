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
        ConfigureAuth(modelBuilder);
        ConfigureAuthoring(modelBuilder);
        ConfigureAttempts(modelBuilder);
        ConfigurePractice(modelBuilder);
        ConfigureFreeResponse(modelBuilder);
        ConfigureGuidedLearning(modelBuilder);
        ConfigureFinance(modelBuilder);
        ConfigureViews(modelBuilder);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
