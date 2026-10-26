using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    private static void ConfigureAuthoring(ModelBuilder modelBuilder)
    {


        modelBuilder.Entity<Certificate>(entity =>
        {
            entity.HasKey(e => e.CertId).HasName("PK__Certific__E5BD38C52927F6C0");

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ScoreAggregationRule).HasMaxLength(30).HasDefaultValue("sum");
            entity.Property(e => e.PresetCode).HasMaxLength(50);
        });



        modelBuilder.Entity<Skill>(entity =>
        {
            entity.HasKey(e => e.SkillId).HasName("PK__Skills__DFA09187AB75DC71");

            entity.HasOne(d => d.Cert).WithMany(p => p.Skills)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Skills__CertId__6383C8BA");
        });



        modelBuilder.Entity<Section>(entity =>
        {
            entity.HasKey(e => e.SectionId).HasName("PK__Sections__80EF0872ACBB3BA8");

            entity.Property(e => e.ScoreWeight).HasPrecision(5, 2).HasDefaultValue(1.0m);
            entity.Property(e => e.NavigationMode).HasMaxLength(30).HasDefaultValue("FULL_SANDBOX");
            entity.Property(e => e.TimerPolicyJson);
            entity.Property(e => e.LayoutType).HasMaxLength(30).HasDefaultValue("STANDARD_VERTICAL");
            entity.Property(e => e.OptionCount).HasDefaultValue(4);
            entity.Property(e => e.ClusterSize).HasDefaultValue(1);
            entity.Property(e => e.UnitCount).HasDefaultValue(1);
            entity.Property(e => e.HideOptionText).HasDefaultValue(false);
            entity.Property(e => e.RequirePassage).HasDefaultValue(false);
            entity.Property(e => e.RequireAudio).HasDefaultValue(false);
            entity.Property(e => e.RequireImage).HasDefaultValue(false);
            entity.Property(e => e.RequiredKeywordCount).HasDefaultValue(0);
            entity.Property(e => e.AllowedComponentTypesJson);
            entity.Property(e => e.ToolsConfigJson);

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



        modelBuilder.Entity<ScoreBand>(entity =>
        {
            entity.HasKey(e => e.BandId).HasName("PK__ScoreBan__A03693A89112EB10");

            entity.ToTable(tb => tb.HasTrigger("trg_ScoreBands_NoOverlap"));

            entity.HasOne(d => d.Cert).WithMany(p => p.ScoreBands)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ScoreBand__CertI__09A971A2");
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
            entity.Property(e => e.DeliveryMode).HasMaxLength(30).HasDefaultValue("strict_exam");
            entity.Property(e => e.ExamFamily).HasMaxLength(50);
            entity.Property(e => e.Status).HasDefaultValue("draft");

            entity.HasOne(d => d.Cert).WithMany(p => p.Exams)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Exams__CertId__160F4887");
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

            entity.Property(e => e.UnitNumber).HasDefaultValue(1);

            entity.HasIndex(e => new { e.GroupId, e.CertId }, "UQ_QGroups_Id_Cert").IsUnique();

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



        modelBuilder.Entity<Question>(entity =>
        {
            entity.HasKey(e => e.QuestionId).HasName("PK__Question__0DC06FACD56E3AE0");

            entity.HasIndex(e => e.CategoryId, "IX_Questions_Category").HasFilter("([CategoryId] IS NOT NULL)");

            entity.Property(e => e.QuestionType).HasMaxLength(30);
            entity.Property(e => e.AllowNumber).HasDefaultValue(true);
            entity.Property(e => e.MaxSelections);
            entity.Property(e => e.AcceptedAnswersJson);
            entity.Property(e => e.RequiredKeywords);

            entity.Ignore(e => e.QuestionOptionQuestion);

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



        modelBuilder.Entity<QuestionOption>(entity =>
        {
            entity.HasKey(e => e.OptionId).HasName("PK__Question__92C7A1FFB8F0027D");

            entity.Property(e => e.QuestionType).HasMaxLength(30);

            entity.HasIndex(e => e.QuestionId, "UX_Options_OneCorrect")
                .IsUnique()
                .HasFilter("([IsCorrect]=(1) AND [QuestionType]<>'multi_select')");

            entity.Property(e => e.OptionId).ValueGeneratedOnAdd();

            entity.HasOne(d => d.Question).WithMany()
                .HasForeignKey(d => d.QuestionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__QuestionO__Quest__339FAB6E");

            entity.HasOne(d => d.QuestionNavigation).WithMany(p => p.QuestionOptionQuestionNavigations)
                .HasPrincipalKey(p => new { p.QuestionId, p.QuestionType })
                .HasForeignKey(d => new { d.QuestionId, d.QuestionType })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Options_QuestionType");
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
    }
}
