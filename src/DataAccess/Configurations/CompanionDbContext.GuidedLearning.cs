using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    private static void ConfigureGuidedLearning(ModelBuilder modelBuilder)
    {


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

            // #232: no PathTemplate relationship. The path is synthesised, not looked up.
        });



        modelBuilder.Entity<LearnerPathStep>(entity =>
        {
            entity.HasKey(e => e.StepId).HasName("PK__LearnerP__24343357410D371C");

            // #191: the step-closure single-writer guard
            entity.ToTable(tb => tb.HasTrigger("trg_LearnerPathSteps_SingleWriter"));
            // #232: UI-44's rule moved here from the dropped PathModuleItems
            entity.ToTable(tb => tb.HasTrigger("trg_LearnerPathSteps_MockSectionIsSittableExam"));

            entity.Property(e => e.Status).HasDefaultValue("locked");
            entity.Property(e => e.TargetAccuracyPct).HasDefaultValue(70);
            entity.Property(e => e.WeekSequence).HasDefaultValue(0);

            entity.HasOne(d => d.LearnerPath).WithMany(p => p.LearnerPathSteps)
                .HasPrincipalKey(p => new { p.LearnerPathId, p.CertId })
                .HasForeignKey(d => new { d.LearnerPathId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Steps_PathCert");

            // #232: the coordinate's own relationships. Each carries CertId where the schema does, so
            // the model and the constraint cannot disagree about which key is being used.
            entity.HasOne(d => d.Skill).WithMany(p => p.LearnerPathSteps)
                .HasPrincipalKey(p => new { p.SkillId, p.Modality })
                .HasForeignKey(d => new { d.SkillId, d.Modality })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Steps_SkillModality");

            entity.HasOne(d => d.Topic).WithMany(p => p.LearnerPathSteps)
                .HasPrincipalKey(p => new { p.TopicId, p.Modality })
                .HasForeignKey(d => new { d.TopicId, d.Modality })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Steps_TopicModality");

            entity.HasOne(d => d.Lesson).WithMany(p => p.LearnerPathSteps)
                .HasPrincipalKey(p => new { p.LessonId, p.CertId })
                .HasForeignKey(d => new { d.RefLessonId, d.CertId })
                .HasConstraintName("FK_Steps_LessonCert");

            entity.HasOne(d => d.Exam).WithMany(p => p.LearnerPathSteps)
                .HasPrincipalKey(p => new { p.ExamId, p.CertId })
                .HasForeignKey(d => new { d.RefExamId, d.CertId })
                .HasConstraintName("FK_Steps_ExamCert");
        });



        modelBuilder.Entity<LearnerLessonProgress>(entity =>
        {
            entity.HasKey(e => e.ProgressId).HasName("PK__LearnerL__BAE29CA5C616E5E3");

            // #240: progress is per learner per lesson per module, so the key is (UserId, LessonId,
            // Module). Asserted by domain40.
            entity.HasIndex(e => new { e.UserId, e.LessonId, e.Module }, "UQ_LLP_UserLessonModule")
                .IsUnique();

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

        modelBuilder.Entity<LearnerLessonMicroCheck>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.CardId })
                .HasName("PK_LearnerLessonMicroChecks");

            entity.HasOne(d => d.Card).WithMany(p => p.LearnerLessonMicroChecks)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LLMC_Card");

            entity.HasOne(d => d.User).WithMany(p => p.LearnerLessonMicroChecks)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LLMC_User");
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

            // #240: the lesson's own cell. The level is stored by its code, never as an integer - the
            // second scale #182 exists to prevent.
            entity.Property(e => e.CefrLevel)
                .HasConversion(v => v.Code, v => ParseAuthoredLevel(v))
                .HasMaxLength(10)
                .IsUnicode(false);

            // #240: FK_Lessons_SkillModality and FK_Lessons_TopicModality are database guards, not EF
            // navigations: each composes a carried-down column with another, and the plain Topic
            // relationship below is what the app reads. The constraints still hold server-side.
            entity.HasOne(d => d.Topic).WithMany(p => p.Lessons)
                .HasForeignKey(d => d.TopicId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Lessons_TopicModality");
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
    }
}
