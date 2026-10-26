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



        modelBuilder.Entity<PathModuleTopic>(entity =>
        {
            entity.HasKey(e => e.TopicId).HasName("PK__PathModu__022E0F5D1DBB1B56");

            entity.HasOne(d => d.Category).WithMany(p => p.PathModuleTopics).HasConstraintName("FK__PathModul__Categ__1F2E9E6D");

            entity.HasOne(d => d.Module).WithMany(p => p.PathModuleTopics)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PathModul__Modul__1E3A7A34");
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
