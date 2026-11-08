using BusinessObjects;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    /// <summary>
    /// #184: reads a stored level code back into a <see cref="CefrLevel"/>. Authored, so the reportable
    /// floor is refused: a row carrying <c>pre_a1</c> is a content defect, and CK_Stimuli_Level /
    /// CK_Items_Level refuse it at the database too. The schema CHECK means this throw is unreachable
    /// while the schema holds; it is here so a value that slipped past would fail loudly and not
    /// silently materialise as A1.
    /// </summary>
    private static CefrLevel ParseAuthoredLevel(string code) =>
        CefrLevel.TryParseAuthored(code, out var level)
            ? level
            : throw new InvalidOperationException(
                $"'{code}' is not an authored CEFR level. Content is authored at A1-C2; the pre-A1 floor is reportable only.");

    private static void ConfigureAuthoring(ModelBuilder modelBuilder)
    {


        modelBuilder.Entity<Certificate>(entity =>
        {
            entity.HasKey(e => e.CertId).HasName("PK__Certific__E5BD38C52927F6C0");

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ScoreAggregationRule).HasMaxLength(30).HasDefaultValue("sum");
            entity.Property(e => e.PresetCode).HasMaxLength(50);
            entity.Property(e => e.SectionNoun).HasMaxLength(30).HasDefaultValue("Section");
        });



        modelBuilder.Entity<Skill>(entity =>
        {
            entity.HasKey(e => e.SkillId).HasName("PK__Skills__DFA09187AB75DC71");

            entity.Property(e => e.LockNavigationOneWay).HasDefaultValue(false);

            entity.HasOne(d => d.Cert).WithMany(p => p.Skills)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Skills__CertId__6383C8BA");
        });



        modelBuilder.Entity<Section>(entity =>
        {
            entity.HasKey(e => e.SectionId).HasName("PK__Sections__80EF0872ACBB3BA8");

            // HasTrigger: #201 / ADM-03 refuses a section written by support triage (exam structure).
            // Without the declaration every section save fails on the OUTPUT clause - the defect
            // ADR-0001 and Tier1PersistenceAndDomainTests exist to catch.
            entity.ToTable(tb => tb.HasTrigger("trg_Sections_TriageCannotWrite"));

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

        // #210: the per-skill cut-score table. HasTrigger is required: trg_CefrCutScores_NoOverlap
        // refuses an overlapping range, and without the declaration every EF save fails on the
        // OUTPUT clause instead of on the rule.
        modelBuilder.Entity<CefrCutScoreVersion>(entity =>
        {
            entity.HasKey(e => new { e.CertId, e.TableVersion }).HasName("PK_CefrCutScoreVersions");

            entity.Property(e => e.Source).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PublishedAt).HasColumnType("date");

            entity.HasOne(d => d.Cert).WithMany(p => p.CutScoreVersions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CefrCutScoreVersions_Cert");
        });

        modelBuilder.Entity<CefrCutScore>(entity =>
        {
            entity.HasKey(e => e.CutScoreId).HasName("PK_CefrCutScores");

            entity.ToTable(tb => tb.HasTrigger("trg_CefrCutScores_NoOverlap"));

            entity.HasIndex(e => new { e.CertId, e.TableVersion, e.SkillId, e.CefrLevel },
                    "UQ_CefrCutScores_Version_Skill_Level")
                .IsUnique();

            entity.Property(e => e.CefrLevel).HasMaxLength(10).IsUnicode(false).IsRequired();
            entity.Property(e => e.RowKind).HasMaxLength(20).IsUnicode(false).IsRequired();

            entity.HasOne(d => d.Version).WithMany(p => p.CutScores)
                .HasPrincipalKey(p => new { p.CertId, p.TableVersion })
                .HasForeignKey(d => new { d.CertId, d.TableVersion })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CefrCutScores_Version");

            entity.HasOne(d => d.SkillNavigation).WithMany(p => p.CutScores)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CefrCutScores_SkillCert");
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

            // #153: the starting band is a band of the same certificate, or NULL when the goal
            // predates the rule. Optional relationship — MATCH SIMPLE keeps the NULL legal.
            entity.HasOne(d => d.CurrentScoreBand).WithMany(p => p.CurrentBandTargetBands)
                .HasPrincipalKey(p => new { p.CertId, p.Code })
                .HasForeignKey(d => new { d.CertId, d.CurrentBandCode })
                .HasConstraintName("FK_TargetBands_CurrentBand");
        });



        modelBuilder.Entity<Exam>(entity =>
        {
            entity.HasKey(e => e.ExamId).HasName("PK__Exams__297521C7A4367810");

            // #185: the second trigger on this table. INV-6 must fire when an exam is promoted too,
            // not only when a binding is written - a rule that fires on the child alone is bypassed by
            // editing the parent.
            // #226: the third. ADM-11's publish door - a paper cannot be created published, and may
            // only move into 'published' with a readiness pass at least as new as its last edit.
            entity.ToTable(tb =>
            {
                tb.HasTrigger("trg_Exams_PurposeKeepsContent");
                tb.HasTrigger("trg_Exams_TierKeepsContent");
                tb.HasTrigger("trg_Exams_PublishRequiresAudit");
                // #227: publishing freezes what was in force - timers, phase weights, aggregation
                // rule, band grid, cut-score version - so a review of a historical attempt never
                // re-derives the numbers it was scored with.
                tb.HasTrigger("trg_Exams_PublishFreezesEdition");
                // Triage cannot write this table. Declared or every EF save fails on the OUTPUT
                // clause (ADR-0001); the guard itself is the table's, not the service's.
                tb.HasTrigger("trg_Exams_TriageCannotWrite");
            });

            entity.HasIndex(e => new { e.CertId, e.Purpose, e.Status }, "IX_Exams_Cert_Status").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.CertId, e.Name }, "UX_Exams_Cert_Name")
                .IsUnique()
                .HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Purpose).HasDefaultValue("mock", "DF_Exams_Purpose");
            entity.Property(e => e.DeliveryMode).HasMaxLength(30).HasDefaultValue("strict_exam");
            entity.Property(e => e.ExamFamily).HasMaxLength(50);
            entity.Property(e => e.Status).HasDefaultValue("draft");
            // #185: a new exam is demo-tier until someone promotes it - the same default as Items.
            entity.Property(e => e.ContentTier).HasMaxLength(12).IsUnicode(false).HasDefaultValue("demo");
            // #227: a paper that was never forked is edition 1.
            entity.Property(e => e.EditionNumber).HasDefaultValue(1);

            entity.HasOne(d => d.Cert).WithMany(p => p.Exams)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Exams__CertId__160F4887");

            // #227: the fork chain. No inverse collection - a paper has at most one predecessor and
            // nothing reads the successors from the parent except the fork itself.
            entity.HasOne(d => d.SupersedesExam).WithMany()
                .HasForeignKey(d => d.SupersedesExamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Exams_Supersedes");
        });



        modelBuilder.Entity<ExamEdition>(entity =>
        {
            entity.HasKey(e => e.ExamId).HasName("PK_ExamEditions");

            // HasTrigger: ADR-0012's ownership guard. Without the declaration every save on this table
            // fails on the OUTPUT clause.
            entity.ToTable(tb => tb.HasTrigger("trg_ExamEditions_SingleWriter"));

            entity.Property(e => e.ScoreAggregationRule).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.WrittenByService).HasMaxLength(64).IsUnicode(false);
            entity.Property(e => e.ProjectionVersion).HasDefaultValue(1);

            // One-to-one with the paper, and the FK is the primary key: a second row for one exam would
            // be a second answer to "what was in force when it went live".
            entity.HasOne(d => d.Exam).WithOne(p => p.Edition)
                .HasForeignKey<ExamEdition>(d => d.ExamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ExamEditions_Exam");

            entity.HasOne(d => d.CutScoreVersion).WithMany()
                .HasPrincipalKey(p => new { p.CertId, p.TableVersion })
                .HasForeignKey(d => new { d.CertId, d.CutScoreTableVersion })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ExamEditions_CutScore");
        });



        modelBuilder.Entity<ExamPublishAudit>(entity =>
        {
            entity.HasKey(e => e.ExamId).HasName("PK_ExamPublishAudits");

            // HasTrigger: #226 / ADM-11 refuses a pass declared by a service other than the audit's
            // own writer. Without the declaration every save on this table fails on the OUTPUT clause.
            entity.ToTable(tb => tb.HasTrigger("trg_ExamPublishAudits_SingleWriter"));

            entity.Property(e => e.WrittenByService).HasMaxLength(64).IsUnicode(false);

            // One-to-one with the paper, and the FK is the primary key: a second row for one exam would
            // be a second verdict, which is the shape ADR-0012 refuses.
            entity.HasOne(d => d.Exam).WithOne(p => p.PublishAudit)
                .HasForeignKey<ExamPublishAudit>(d => d.ExamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ExamPublishAudits_Exam");
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

        // #183: the topic axis. No CertId, so no navigation - a topic belongs to the language.
        // HasTrigger: trg_Topics_ClosedVocabulary refuses a twenty-first topic, and without this
        // declaration EF's OUTPUT clause fails on every save to this table.
        modelBuilder.Entity<Topic>(entity =>
        {
            entity.HasKey(e => e.TopicId).HasName("PK_Topics");

            entity.ToTable(tb => tb.HasTrigger("trg_Topics_ClosedVocabulary"));

            // #184: the composite target FK_Items_TopicModality needs. TopicId is already the primary
            // key, so this adds no uniqueness - it exists only so an item's modality can be pinned to
            // its topic's own.
            entity.HasIndex(e => new { e.TopicId, e.Modality }, "UQ_Topics_Id_Modality").IsUnique();
        });

        // #184: the pool. A CefrLevel is stored by its code, never as an integer: an integer scale
        // beside the level codes is the second scale #182 exists to prevent. TryParseAuthored is what
        // reads it back, so the reportable floor cannot be authored by accident.
        modelBuilder.Entity<Stimulus>(entity =>
        {
            entity.HasKey(e => e.StimulusId).HasName("PK_Stimuli");

            entity.Property(e => e.CefrLevel)
                .HasConversion(v => v.Code, v => ParseAuthoredLevel(v))
                .HasMaxLength(10)
                .IsUnicode(false);

            entity.Property(e => e.DomainContext).HasDefaultValue("general");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasKey(e => e.ItemId).HasName("PK_Items");

            // HasTrigger: trg_Items_LifecycleTransitions refuses a state move the specification does
            // not name, and trg_Items_NoHardDelete refuses the delete of anything that has left draft
            // or that a learner answered. Without a declaration every save to this table fails on the
            // OUTPUT clause - the defect this repo has shipped once already.
            //
            // Only one of the two is strictly needed for that: EF emits one OUTPUT clause per table,
            // and any declared trigger switches it off. Both are named anyway, and
            // Tier1PersistenceAndDomainTests asserts the pair, because the declaration that "works"
            // is the one that happens to be there - so a reader deleting the redundant-looking second
            // line removes the guard's documentation, and the next person to drop the first one
            // breaks every save to Items. The removal is proved in prove-guard-lifecycle.sh's control.
            entity.ToTable(tb =>
            {
                tb.HasTrigger("trg_Items_LifecycleTransitions");
                tb.HasTrigger("trg_Items_NoHardDelete");
                // #255: triage cannot write this table. Declared or every EF save fails on the
                // OUTPUT clause (ADR-0001).
                tb.HasTrigger("trg_Items_TriageCannotWrite");
            });

            entity.Property(e => e.CefrLevel)
                .HasConversion(v => v.Code, v => ParseAuthoredLevel(v))
                .HasMaxLength(10)
                .IsUnicode(false);

            entity.Property(e => e.PlacementEligible).HasDefaultValue(false);
            entity.Property(e => e.ContentTier).HasDefaultValue("demo");
            entity.Property(e => e.LifecycleStatus).HasDefaultValue("draft");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasIndex(e => new { e.TopicId, e.CefrLevel }, "IX_Items_Cell");
            entity.HasIndex(e => new { e.LifecycleStatus, e.ContentTier }, "IX_Items_Selection");
            entity.HasIndex(e => e.StimulusId, "IX_Items_Stimulus").HasFilter("([StimulusId] IS NOT NULL)");
            // #185: composite target FK_ExamItems_ItemSkill needs. ItemId is already the primary key,
            // so this adds no uniqueness - it exists only so a bound item's skill is pinned to its
            // section's own by foreign key rather than by a trigger.
            entity.HasIndex(e => new { e.ItemId, e.Modality }, "UQ_Items_Id_Modality").IsUnique();

            entity.HasOne(d => d.Stimulus).WithMany(p => p.Items)
                .HasForeignKey(d => d.StimulusId)
                .HasConstraintName("FK_Items_Stimulus");

            // pins the item's skill to its topic's own (the idiom Sections already uses)
            entity.HasOne(d => d.Topic).WithMany(p => p.Items)
                .HasPrincipalKey(p => new { p.TopicId, p.Modality })
                .HasForeignKey(d => new { d.TopicId, d.Modality })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Items_TopicModality");
        });

        // HasTrigger: trg_ItemVersions_ScoredFieldsImmutable refuses an in-place change to a scored
        // field, and #201's trg_ItemVersions_TriageCannotWrite refuses item text and keys written by
        // support triage. Without these declarations every save to this table fails on the OUTPUT
        // clause - the exact defect ADR-0001 and Tier1PersistenceAndDomainTests exist to catch.
        modelBuilder.Entity<ItemVersion>(entity =>
        {
            entity.HasKey(e => e.ItemVersionId).HasName("PK_ItemVersions");

            entity.ToTable(tb =>
            {
                tb.HasTrigger("trg_ItemVersions_ScoredFieldsImmutable");
                tb.HasTrigger("trg_ItemVersions_TriageCannotWrite");
            });

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasIndex(e => new { e.ItemId, e.VersionNumber }, "UQ_ItemVersions_Item_Number").IsUnique();
            entity.HasIndex(e => new { e.ItemId, e.VersionNumber }, "IX_ItemVersions_Item");
            // #185: composite target FK_ExamItems_VersionItem needs - (version, item) is the pair a
            // binding pins, so a version can only be bound to the item it belongs to.
            entity.HasIndex(e => new { e.ItemVersionId, e.ItemId }, "UQ_ItemVersions_Id_Item").IsUnique();

            entity.HasOne(d => d.Item).WithMany(p => p.ItemVersions)
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ItemVersions_Item");
        });



        // #185: the assembly binding. HasTrigger: trg_ExamItems_ProductionBindsProduction refuses a
        // production exam binding demo-tier stock. Without the declaration every save to this table
        // fails on the OUTPUT clause, which is the defect this repo has shipped once already.
        //
        // The three composite FKs are declared by hand because two of them share a column: CertId is
        // read by both the exam and the section FK, Modality by both the section and the item FK.
        // EF cannot infer those from conventions, and inferring them wrongly would make certificate
        // isolation a convention instead of a constraint.
        modelBuilder.Entity<ExamItem>(entity =>
        {            entity.HasKey(e => e.ExamItemId).HasName("PK_ExamItems");

            entity.ToTable(tb =>
            {
                tb.HasTrigger("trg_ExamItems_ProductionBindsProduction");
                // #201 / ADM-03: exam assembly is structure, so triage cannot write it.
                tb.HasTrigger("trg_ExamItems_TriageCannotWrite");
            });

            entity.Property(e => e.Modality).HasMaxLength(10).IsUnicode(false);
            entity.Property(e => e.UnitNumber).HasDefaultValue(1);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasIndex(e => new { e.ExamId, e.SectionId, e.DisplayOrder }, "UQ_ExamItems_Exam_Section_Order").IsUnique();
            entity.HasIndex(e => e.ItemId, "IX_ExamItems_Item");
            entity.HasIndex(e => e.ItemVersionId, "IX_ExamItems_Version");

            entity.HasOne(d => d.Exam).WithMany(p => p.ExamItems)
                .HasPrincipalKey(p => new { p.ExamId, p.CertId })
                .HasForeignKey(d => new { d.ExamId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ExamItems_ExamCert");

            entity.HasOne(d => d.Section).WithMany(p => p.ExamItems)
                .HasPrincipalKey(p => new { p.SectionId, p.CertId })
                .HasForeignKey(d => new { d.SectionId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ExamItems_SectionCert");

            // The section's skill and the item's skill are pinned to the one Modality column, so a
            // listening section cannot bind a reading item's version - the pair has no target row.
            entity.HasOne(d => d.Item).WithMany(p => p.ExamItems)
                .HasPrincipalKey(p => new { p.ItemId, p.Modality })
                .HasForeignKey(d => new { d.ItemId, d.Modality })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ExamItems_ItemSkill");

            // The pinned version must belong to the item the binding names.
            entity.HasOne(d => d.ItemVersion).WithMany(p => p.ExamItems)
                .HasPrincipalKey(p => new { p.ItemVersionId, p.ItemId })
                .HasForeignKey(d => new { d.ItemVersionId, d.ItemId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ExamItems_VersionItem");
        });


        // #199 / D0.7: one item's stored-answer verdict at one administration checkpoint. No trigger,
        // so no HasTrigger - the row is written, never updated, and the primary key is what makes a
        // second evaluation of the same checkpoint a refusal rather than a double count.
        modelBuilder.Entity<ItemTelemetryEvaluation>(entity =>
        {
            entity.HasKey(e => new { e.ItemId, e.CheckpointSize }).HasName("PK_ItemTelemetryEvaluations");

            entity.Property(e => e.Discrimination).HasColumnType("decimal(9,6)");
            entity.Property(e => e.TargetFacility).HasColumnType("decimal(9,6)");
            entity.Property(e => e.MeanResponseBand).HasColumnType("decimal(9,6)");
            entity.Property(e => e.EvaluatedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            // The console's "what is quarantined, newest first" read. The primary key serves the other
            // direction (one item's own history), so this covers only the filtered one.
            entity.HasIndex(e => e.EvaluatedAt, "IX_ItemTelemetry_Quarantined")
                .HasFilter("([Quarantined] = 1)");

            entity.HasOne(d => d.Item).WithMany()
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ItemTelemetryEvaluations_Item");
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

            // #229 / D1: the level is stored by its code, never as an integer - an integer scale
            // beside the level codes is the second scale #182 exists to prevent. A NULL column stays a
            // NULL level: EF never passes null through a value converter.
            entity.Property(e => e.CefrLevel)
                .HasConversion(
                    v => v == null ? null : v.Value.Code,
                    v => v == null ? null : (CefrLevel?)ParseAuthoredLevel(v))
                .HasMaxLength(10)
                .IsUnicode(false);

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



        modelBuilder.Entity<ReviewSampleDecision>(entity =>
        {
            entity.HasKey(e => e.DecisionId).HasName("PK_ReviewSampleDecisions");

            // HasTrigger: #201 / ADM-02 refuses a self-approval and refuses DELETE outright. Without
            // this declaration every decision save fails on the OUTPUT clause - the defect ADR-0001
            // and Tier1PersistenceAndDomainTests exist to catch.
            entity.ToTable(tb => tb.HasTrigger("trg_ReviewSampleDecisions_NoSelfApproval"));

            entity.Property(e => e.Decision).HasMaxLength(12).IsUnicode(false);
            entity.Property(e => e.DecidedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasIndex(e => new { e.BatchId, e.ItemId }, "IX_RSD_Batch_Item");

            entity.HasOne<ImportBatch>().WithMany()
                .HasForeignKey(d => d.BatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RSD_Batch");

            entity.HasOne<Item>().WithMany()
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RSD_Item");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(d => d.ActorUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RSD_Actor");
        });

        modelBuilder.Entity<ImportBatch>(entity =>
        {
            entity.HasKey(e => e.BatchId).HasName("PK__ImportBa__5D55CE589E3B37D5");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("pending");

            // #205: a pool upload names no certificate (CNT-01). The exam importer still always writes
            // one, so the optional shape costs it nothing.
            entity.Property(e => e.CertId).IsRequired(false);
            entity.Property(e => e.SourceDocument);

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

            // #205: the verdict's own facts. Attempt defaults to 1 in the database so a row written
            // without it is the first judgement rather than an error.
            entity.Property(e => e.Attempt).HasDefaultValue(1);
            entity.Property(e => e.RuleCode).HasMaxLength(40).IsUnicode(false);

            entity.HasOne<Item>().WithMany()
                .HasForeignKey(e => e.ItemId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_ImportLog_Item");

            entity.HasOne(d => d.Batch).WithMany(p => p.ImportLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ImportLog__Batch__540C7B00");
        });

        // #206: the stratified audit. HasTrigger: trg_ReviewStrata_SingleWriter refuses a decision that
        // declares a second writer, and a re-run that rewrites a taken decision. Without this
        // declaration every save to this table fails on the OUTPUT clause - the defect ADR-0001 and
        // Tier1PersistenceAndDomainTests exist to catch.
        modelBuilder.Entity<ReviewStratum>(entity =>
        {
            entity.HasKey(e => e.StratumId).HasName("PK_ReviewStrata");

            entity.ToTable(tb => tb.HasTrigger("trg_ReviewStrata_SingleWriter"));

            entity.Property(e => e.CefrLevel)
                .HasConversion(v => v.Code, v => ParseAuthoredLevel(v))
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.Status).HasDefaultValue("pending");
            entity.Property(e => e.QuarantineReason).HasMaxLength(24).IsUnicode(false);
            entity.Property(e => e.WrittenByService).HasMaxLength(64).IsUnicode(false);
            entity.Property(e => e.ProjectionVersion).HasDefaultValue(0);

            entity.HasIndex(e => new { e.BatchId, e.TopicId, e.CefrLevel }, "UQ_ReviewStrata_Batch_Cell").IsUnique();
            entity.HasIndex(e => e.BatchId, "IX_ReviewStrata_Batch");

            entity.HasOne(d => d.Batch).WithMany(p => p.Strata)
                .HasForeignKey(d => d.BatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReviewStrata_Batch");

            entity.HasOne(d => d.Topic).WithMany(p => p.ReviewStrata)
                .HasForeignKey(d => d.TopicId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReviewStrata_Topic");
        });

        // HasTrigger: W-01's blind-solve order, and the batch-scope rule that stops a sample pointing at
        // another author's stock. Both would fail every save on the OUTPUT clause without this.
        modelBuilder.Entity<ReviewSampleItem>(entity =>
        {
            entity.HasKey(e => e.SampleItemId).HasName("PK_ReviewSampleItems");

            entity.ToTable(tb =>
            {
                tb.HasTrigger("trg_ReviewSampleItems_SolveIsBlind");
                tb.HasTrigger("trg_ReviewSampleItems_ItemIsInItsBatch");
            });

            entity.Property(e => e.SolvedLabel).HasMaxLength(1).IsUnicode(false);
            entity.Property(e => e.Verdict).HasMaxLength(20).IsUnicode(false);

            entity.HasIndex(e => new { e.StratumId, e.ItemId }, "UQ_ReviewSampleItems_Stratum_Item").IsUnique();
            entity.HasIndex(e => e.StratumId, "IX_ReviewSampleItems_Stratum");

            // The stratum is pinned by (id, batch) rather than id alone, so a sample cannot name a
            // stratum of one batch and an item of another. EF cannot infer the two-column shape.
            entity.HasOne(d => d.Stratum).WithMany(p => p.SampleItems)
                .HasPrincipalKey(p => new { p.StratumId, p.BatchId })
                .HasForeignKey(d => new { d.StratumId, d.BatchId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReviewSampleItems_Stratum");

            entity.HasOne(d => d.Item).WithMany(p => p.ReviewSampleItems)
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReviewSampleItems_Item");

            // The version must be the item's own: the same composite target ExamItems pins.
            entity.HasOne(d => d.ItemVersion).WithMany(p => p.ReviewSampleItems)
                .HasPrincipalKey(p => new { p.ItemVersionId, p.ItemId })
                .HasForeignKey(d => new { d.ItemVersionId, d.ItemId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReviewSampleItems_VersionItem");
        });
    }
}
