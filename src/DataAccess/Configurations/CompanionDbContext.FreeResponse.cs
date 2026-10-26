using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    private static void ConfigureFreeResponse(ModelBuilder modelBuilder)
    {


        modelBuilder.Entity<FreeResponse>(entity =>
        {
            entity.HasKey(e => e.FreeResponseId).HasName("PK__FreeResp__239C05117F02D927");

            entity.ToTable(tb =>
                {
                    tb.HasTrigger("trg_FreeResponses_DraftOnlyInProgress");
                    tb.HasTrigger("trg_FreeResponses_NotIntoGradedAttempt");
                });

            entity.HasIndex(e => e.SubmittedAt, "IX_FreeResponses_Pending").HasFilter("([Status]='pending_ai')");

            entity.Property(e => e.QuestionType).HasMaxLength(30);
            entity.Property(e => e.Status).HasDefaultValue("pending_ai");
            entity.Property(e => e.SubmittedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.SubmittedAt).ValueGeneratedNever();

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
    }
}
