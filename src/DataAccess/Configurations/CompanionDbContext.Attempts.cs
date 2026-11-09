using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    private static void ConfigureAttempts(ModelBuilder modelBuilder)
    {


        modelBuilder.Entity<ExamAttempt>(entity =>
        {
            entity.HasKey(e => e.AttemptId).HasName("PK__ExamAtte__891A68E672B77157");

            entity.ToTable(tb =>
                {
                    tb.HasTrigger("trg_Attempts_NoDraftsAfterSubmit");
                    tb.HasTrigger("trg_Attempts_NoGradedWithPendingFreeResponses");
                    tb.HasTrigger("trg_Attempts_NoTotalScoreForSkillPractice");
                    tb.HasTrigger("trg_Attempts_TotalMatchesSkills_OnAttempt");
                    tb.HasTrigger("trg_ExamAttempts_PublishedExamOnly");
                    // #227: an attempt's ExamId is the edition it sat and never moves. Declared here or
                    // every EF save on this table fails on the OUTPUT clause (ADR-0001).
                    tb.HasTrigger("trg_ExamAttempts_EditionIsFixed");
                    // #191: the single-writer guard, which reads WrittenByService and ProjectionVersion
                    tb.HasTrigger("trg_Attempts_SingleWriter");
                    // #246: a preserved attempt is read-only. Declared here or every EF save on this
                    // table fails on the OUTPUT clause (ADR-0001).
                    tb.HasTrigger("trg_Attempts_EpochIsReadOnly");
                });

            // #246: the cutover tag. Nullable and unset by default: a default would make every new
            // attempt claim an epoch, and "taken before" vs "taken after" is the whole distinction.
            entity.Property(e => e.EpochTag).HasMaxLength(16).IsUnicode(false);

            entity.Property(e => e.StartedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("in_progress");
            entity.Property(e => e.ActiveUnitNumber).HasDefaultValue(1);
            entity.Property(e => e.AuthorizationCode).HasMaxLength(64);
            entity.Property(e => e.PhaseDeadlineAt).HasColumnType("datetime2");
            entity.Property(e => e.LastAudioOffsetMs).HasDefaultValue(0);
            entity.Property(e => e.CandidateNotesJson);
            // #216 / PLC-13: nullable, so a sitting that has not been touched since it opened falls
            // back to StartedAt rather than to a value invented at insert time.
            entity.Property(e => e.LastActivityAt);
            entity.Property(e => e.PreflightVerifiedAt);

            entity.HasOne(d => d.ActiveSkill).WithMany()
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.ActiveSkillId, d.CertId })
                .HasConstraintName("FK_Attempts_ActiveSkill");

            entity.HasOne(d => d.ActiveGroup).WithMany()
                .HasPrincipalKey(p => new { p.GroupId, p.CertId })
                .HasForeignKey(d => new { d.ActiveGroupId, d.CertId })
                .HasConstraintName("FK_Attempts_ActiveGroup");

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



        modelBuilder.Entity<AttemptAnswer>(entity =>
        {
            entity.HasKey(e => e.AnswerId).HasName("PK__AttemptA__D48250049EDA465F");

            entity.Property(e => e.ResponseText);
            entity.Property(e => e.SelectedOptionIdsJson);
            entity.Property(e => e.SelectedLabel).HasMaxLength(1).IsUnicode(false);

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

            // #186: the answered version is pinned by (version, item), the same composite pair
            // ExamItems uses, so one row cannot name one item and another item's version.
            entity.HasOne(d => d.ItemVersion).WithMany(p => p.AttemptAnswers)
                .HasPrincipalKey(p => new { p.ItemVersionId, p.ItemId })
                .HasForeignKey(d => new { d.ItemVersionId, d.ItemId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttemptAnswers_VersionItem");
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



        modelBuilder.Entity<PlacementTest>(entity =>
        {
            entity.HasKey(e => e.PlacementId).HasName("PK__Placemen__2E328C652BD86022");

            entity.ToTable(tb => tb.HasTrigger("trg_PlacementTests_SnapshotMatchesAttempt"));

            // #212: a placement cannot be marked done without a measured row or a recorded failure.
            // Declared or every EF save on this table fails on the OUTPUT clause (ADR-0001).
            entity.ToTable(tb => tb.HasTrigger("trg_PlacementTests_CompletionIsEarned"));

            entity.ToTable(tb => tb.HasTrigger("trg_PlacementTests_SingleWriter"));

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
    }
}
