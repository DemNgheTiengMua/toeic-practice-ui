using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    private static void ConfigurePractice(ModelBuilder modelBuilder)
    {


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



        modelBuilder.Entity<PracticeAnswer>(entity =>
        {
            entity.HasKey(e => e.AnswerId).HasName("PK__Practice__D48250043F262F2D");

            entity.Property(e => e.ResponseText);
            entity.Property(e => e.SelectedOptionIdsJson);

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
    }
}
