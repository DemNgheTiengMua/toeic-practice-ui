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
            // #187: a pool drill is resumed and ceiling-checked by its cell, not by a section.
            entity.HasIndex(e => new { e.UserId, e.TopicId, e.CefrLevel, e.StartedAt }, "IX_Practice_Cell")
                .HasFilter("([TopicId] IS NOT NULL)");
            // #240: one OPEN lesson sitting per learner per lesson.
            entity.HasIndex(e => new { e.UserId, e.LessonId }, "UX_Practice_OneLessonSitting")
                .IsUnique()
                .HasFilter("([LessonId] IS NOT NULL AND [Status] = 'in_progress')");

            entity.Property(e => e.QuestionSource).HasDefaultValue("all", "DF_Practice_Source");
            entity.Property(e => e.StartedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("in_progress");
            // #187: a CefrLevel is stored by its code, never as an integer - the second scale #182
            // exists to prevent.
            entity.Property(e => e.CefrLevel)
                .HasConversion(v => v!.Value.Code, v => ParseAuthoredLevel(v))
                .HasMaxLength(10)
                .IsUnicode(false);

            entity.HasOne(d => d.LearnerPathStep).WithMany(p => p.PracticeSessions).HasConstraintName("FK_Practice_PathStep");

            entity.HasOne(d => d.Cert).WithMany(p => p.PracticeSessions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Practice_Cert");

            entity.HasOne(d => d.Section).WithMany(p => p.PracticeSessionSections)
                .HasForeignKey(d => d.SectionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PracticeS__Secti__5D95E53A");

            entity.HasOne(d => d.User).WithMany(p => p.PracticeSessions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PracticeS__UserI__5CA1C101");

            // #187: FK_Practice_SectionModality, FK_Practice_TopicModality and FK_Practice_SectionCert
            // are database guards, not EF navigations. Each composes a nullable column with a
            // non-nullable one (Modality, CertId), and EF reads a partly-nullable composite key as
            // required - so a pool session (no section) fails the save with "the value of SectionId is
            // unknown". The constraints still hold: MATCH SIMPLE skips one exactly when its nullable
            // column is null, which is the pool shape. Section and Topic above are the plain single
            // column navigations, which EF can model.
        });



        modelBuilder.Entity<PracticeAnswer>(entity =>
        {
            entity.HasKey(e => e.AnswerId).HasName("PK__Practice__D48250043F262F2D");

            // #187: the unique keys are filtered indexes, because a pool answer has no QuestionId and
            // a legacy answer has no ItemVersionId. EF cannot express a filtered unique index from
            // conventions, so the names are declared here and dropped from the model as keys.
            entity.HasIndex(e => e.QuestionId, "IX_PracticeAnswers_Question").HasFilter("([QuestionId] IS NOT NULL)");
            // #240: one micro-check graded once per sitting.
            entity.HasIndex(e => new { e.SessionId, e.CardId }, "UQ_PracticeAnswers_Card")
                .IsUnique()
                .HasFilter("([CardId] IS NOT NULL)");

            entity.Property(e => e.ResponseText);
            entity.Property(e => e.SelectedOptionIdsJson);
            // #156: response time per practice answer, same measurement as the exam sitting.
            entity.Property(e => e.AnsweredAt).HasPrecision(3);

            entity.HasOne(d => d.Question).WithMany(p => p.PracticeAnswerQuestions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PracticeA__Quest__6CD828CA");

            entity.HasOne(d => d.Session).WithMany(p => p.PracticeAnswerSessions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PracticeA__Sessi__6BE40491");

            // #187: FK_PracticeAnswers_QuestionSection (the question must be the session's section)
            // and FK_PracticeAnswers_SessionSection (the answer's section must be the session's) are
            // database guards, not EF navigations: SectionId and QuestionId are now nullable, and EF
            // reads a partly-nullable composite key as required, refusing a pool answer's save with
            // "the value of QuestionId is unknown". MATCH SIMPLE skips each exactly when its nullable
            // column is null, which is the pool shape.

            // #187: the answered version must be a version of the answered item - the same composite
            // idiom ExamItems and AttemptAnswers use.
            entity.HasOne(d => d.ItemVersion).WithMany(p => p.PracticeAnswers)
                .HasPrincipalKey(p => new { p.ItemVersionId, p.ItemId })
                .HasForeignKey(d => new { d.ItemVersionId, d.ItemId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PracticeAnswers_VersionItem");
        });



        modelBuilder.Entity<UserCategoryMastery>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.CategoryId }).HasName("PK_UserCategoryMasteries");

            entity.HasIndex(e => new { e.UserId, e.CertId, e.NextReviewDueAt }, "IX_UCM_User_Due");

            entity.Property(e => e.State).HasDefaultValue("untested", "DF_UCM_State");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_UCM_Updated");

            entity.HasOne(d => d.Category).WithMany(p => p.UserCategoryMasteries)
                .HasPrincipalKey(p => new { p.CategoryId, p.CertId })
                .HasForeignKey(d => new { d.CategoryId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UCM_CategoryCert");

            entity.HasOne(d => d.User).WithMany(p => p.UserCategoryMasteries)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UCM_User");
        });

        modelBuilder.Entity<UserQuestionHistory>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.QuestionId }).HasName("PK_UserQuestionHistories");

            entity.HasIndex(e => new { e.UserId, e.CooldownUntil }, "IX_UQH_User_Cooldown");

            entity.Property(e => e.LastAnsweredAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_UQH_Last");

            entity.HasOne(d => d.Question).WithMany(p => p.UserQuestionHistories)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UQH_Question");

            entity.HasOne(d => d.User).WithMany(p => p.UserQuestionHistories)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UQH_User");
        });



        // #224: a saved link pins the item version it rendered, so it has two shapes and one address.
        // The two unique indexes are filtered (one per shape), which EF cannot infer, so they are
        // declared by name here and excluded from the key. The composite version FK is written by hand
        // for the same reason PracticeAnswers' is: EF cannot infer (version, item) as one relation.
        modelBuilder.Entity<SavedQuestion>(entity =>
        {
            entity.HasKey(e => e.SavedQuestionId).HasName("PK_SavedQuestions");

            entity.Property(e => e.SavedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasIndex(e => new { e.UserId, e.QuestionId }, "UQ_SavedQuestions_Question")
                .IsUnique()
                .HasFilter("([QuestionId] IS NOT NULL)");
            entity.HasIndex(e => new { e.UserId, e.ItemId }, "UQ_SavedQuestions_Item")
                .IsUnique()
                .HasFilter("([ItemId] IS NOT NULL)");
            entity.HasIndex(e => new { e.UserId, e.CertId }, "IX_SavedQuestions_User_Cert")
                .HasFilter("([CertId] IS NOT NULL)");

            entity.HasOne(d => d.Question).WithMany(p => p.SavedQuestions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__SavedQues__Quest__756D6ECB");

            // The pinned version must be a version of the item the link names.
            entity.HasOne(d => d.ItemVersion).WithMany(p => p.SavedQuestions)
                .HasPrincipalKey(p => new { p.ItemVersionId, p.ItemId })
                .HasForeignKey(d => new { d.ItemVersionId, d.ItemId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SavedQuestions_VersionItem");

            entity.HasOne(d => d.Cert).WithMany(p => p.SavedQuestions)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SavedQuestions_Cert");

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
