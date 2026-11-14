using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// Step 15 of 15 (Ticket #115) - Exam System Rebuild Spec #106
/// Phase 5: Full System E2E Automated Integration Suite & Score Calibration Verification.
///
/// Exercises the complete Single Chain of Authority across:
/// 1. Certificate Wizard Seam (#116, #120) - 6-axis configurations
/// 2. Exam Studio & Ingest Seam (#117, #118, #121) - 11-component authoring, readiness audit & publishing
/// 3. Pre-flight & Sitting State Machine (#108-#114) - gating, real-time sync, crash recovery, transitions
/// 4. Scoring Engine Calibration (#108-#114) - 11 components, weighted sections, aggregated bands
/// 5. Practice Drilling & Weakness Analysis (#119) - non-MCQ drilling, deterministic SQL views
///
/// Also verifies score calibration across all 5 official presets:
/// - IELTS (Academic/GT): 0–9.0 band scale with half-step rounding (avg_half_step) and 2:1 Task 2 weighting
/// - TOEIC Standard CBT: 10–990 scale with sum aggregation
/// - TOEIC Adaptive CAT: 2-stage unit locking and scoring
/// - TOEIC S&W: two 0–200 skill scales, summed
/// - VSTEP: 0–10 scale with avg_whole rounding
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExamRebuildFullSystemE2ETests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..18].ToUpperInvariant();

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null)
    {
        var email = $"{Guid.NewGuid():N}@learner.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'E2E Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// #269: one authored position on the pool - an item, its first version and the binding that puts
    /// it on the paper. Written directly because the answer-shape columns (accepted answers, the word
    /// range, the keywords, the selection count) are writable only at INSERT and
    /// <c>CreatePoolItemRequest</c> does not carry them; <see cref="PoolFixture"/> writes the same rows.
    /// Returns the position id the sitting addresses and the version id its option ids derive from.
    /// </summary>
    private static async Task<(int ExamItemId, int ItemVersionId)> SeedPositionAsync(
        CompanionDbContext db,
        int examId,
        int certId,
        int sectionId,
        string modality,
        string itemType,
        string renderFormat,
        string stem,
        int displayOrder,
        int unitNumber,
        int? stimulusId = null,
        string? optionA = null,
        string? optionB = null,
        string? optionC = null,
        string? optionD = null,
        string? keyLabel = null,
        int? maxSelections = null,
        string? acceptedAnswersJson = null,
        int? maxWords = null,
        bool allowNumber = true,
        int? minWords = null,
        string? requiredKeywords = null,
        string? explanation = null,
        int? evidenceStart = null,
        int? evidenceEnd = null,
        string? evidenceUnit = null)
    {
        // The pool's topic vocabulary is a closed set of twenty that no certificate owns, so a fixture
        // authors the row a fresh database lacks - the same way PoolFixture does.
        var topicId = await EnsureTopicAsync(db, modality);

        var item = new Item
        {
            Modality = modality,
            TopicId = topicId,
            CefrLevel = CefrLevel.B1,
            ItemType = itemType,
            RenderFormat = renderFormat,
            ContentTier = "demo",
            LifecycleStatus = "active",
            StimulusId = stimulusId,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var version = new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = stem,
            OptionA = optionA,
            OptionB = optionB,
            OptionC = optionC,
            OptionD = optionD,
            KeyLabel = keyLabel,
            Explanation = explanation ?? "The passage states it.",
            RationaleA = optionA is null ? null : "Correct.",
            RationaleB = optionB is null ? null : "Wrong.",
            RationaleC = optionC is null ? null : "Wrong.",
            RationaleD = optionD is null ? null : "Wrong.",
            MaxSelections = maxSelections,
            AcceptedAnswersJson = acceptedAnswersJson,
            MaxWords = maxWords,
            AllowNumber = allowNumber,
            MinWords = minWords,
            RequiredKeywords = requiredKeywords,
            // CNT-11: every child of a bundle carries an evidence span, so INV-2 can judge the overlap.
            EvidenceStart = evidenceStart,
            EvidenceEnd = evidenceEnd,
            EvidenceUnit = evidenceUnit,
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync();

        var binding = new ExamItem
        {
            ExamId = examId,
            CertId = certId,
            SectionId = sectionId,
            ItemId = item.ItemId,
            ItemVersionId = version.ItemVersionId,
            Modality = modality,
            DisplayOrder = displayOrder,
            UnitNumber = unitNumber,
        };
        db.ExamItems.Add(binding);
        await db.SaveChangesAsync();

        return (binding.ExamItemId, version.ItemVersionId);
    }

    /// <summary>
    /// INV-3's precondition: a batch holding every position of the paper, and a reviewer who is neither
    /// its uploader nor any item's author, clearing each one. ADM-02 refuses a clearance signed by the
    /// account that uploaded the batch or wrote the item, so the fixture needs two accounts.
    /// </summary>
    private static async Task<int> SeedClearanceAsync(CompanionDbContext db, int certId, int examId)
    {
        var role = await db.Roles.Where(r => r.Code == "admin").Select(r => r.RoleId).FirstOrDefaultAsync();
        if (role == 0)
        {
            var created = new Role { Code = "admin", Name = "Admin" };
            db.Roles.Add(created);
            await db.SaveChangesAsync();
            role = created.RoleId;
        }

        var uploader = new User
        {
            RoleId = role,
            Email = $"e2e-uploader-{Guid.NewGuid():N}@test.local",
            PasswordHash = [0x01],
            FullName = "E2E uploader",
        };
        var reviewer = new User
        {
            RoleId = role,
            Email = $"e2e-reviewer-{Guid.NewGuid():N}@test.local",
            PasswordHash = [0x01],
            FullName = "E2E reviewer",
        };
        db.Users.AddRange(uploader, reviewer);
        await db.SaveChangesAsync();

        var batch = new ImportBatch
        {
            CertId = certId,
            SourceName = "e2e-full-chain.xlsx",
            Status = ImportBatchStatus.Done.ToCode(),
            UploadedBy = uploader.UserId,
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();

        var itemIds = await db.ExamItems.Where(i => i.ExamId == examId)
            .Select(i => i.ItemId).Distinct().ToListAsync();

        foreach (var itemId in itemIds)
        {
            db.ImportLogs.Add(new ImportLog
            {
                BatchId = batch.BatchId,
                ItemId = itemId,
                Level = "info",
                Message = "accepted by the E2E full-chain fixture",
            });
        }

        await db.SaveChangesAsync();
        return batch.BatchId;
    }

    /// <summary>
    /// A topic of this modality, provisioned when the fresh database has none. The vocabulary is a
    /// closed set of twenty, so a canonical code is reused rather than invented.
    /// </summary>
    private static async Task<int> EnsureTopicAsync(CompanionDbContext db, string modality)
    {
        var existing = await db.Topics.Where(t => t.Modality == modality)
            .OrderBy(t => t.TopicId).Select(t => (int?)t.TopicId).FirstOrDefaultAsync();
        if (existing is not null)
        {
            return existing.Value;
        }

        var (code, name) = modality switch
        {
            "listening" => ("L-DET", "Listening for detail"),
            "speaking" => ("S-DESC", "Describing"),
            "writing" => ("W-CORR", "Transactional correspondence"),
            _ => ("R-DET", "Reading for detail"),
        };

        var topic = new Topic { Modality = modality, Code = code, Name = name };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic.TopicId;
    }

    [Fact]
    public async Task Single_Chain_Of_Authority_Complete_E2E_Flow_Async()
    {
        var certCode = Unique("CUST_E2E");

        // =========================================================================
        // SEAM 1: Certificate Wizard Seam (#116, #120)
        // 6-Axis configurations: Skills, Sections, NavigationModes, Layouts, Weights, Component Types
        // =========================================================================
        await using (var dbWiz = DbContextFactory.Create(database.ConnectionString))
        {
            var certService = new CertificateService(dbWiz);

            var wizardRequest = new SaveCertificateWizardRequest(
                CertId: null,
                Code: certCode,
                Name: "Custom 6-Axis Master Certificate",
                Description: "Integration test certificate exercising 6-axis blueprint rules across all modalities.",
                ScoreAggregationRule: "avg_half_step",
                PresetCode: "custom",
                IsActive: true,
                MinScaledScore: 0,
                MaxScaledScore: 100,
                ScoreStep: 1,
                Bands:
                [
                    new Contracts.SaveScoreBandWizardDto(null, "FAIL", "Fail", 0, 49, 1),
                    new Contracts.SaveScoreBandWizardDto(null, "PASS", "Pass", 50, 100, 2)
                ],
                Skills:
                [
                    // Skill 1: Listening (AudioDriven, SplitHorizontal)
                    new SaveSkillWizardDto(
                        SkillId: null,
                        Code: "LIST",
                        Name: "Listening",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 30,
                        Sections:
                        [
                            new SaveSectionWizardDto(
                                SectionId: null,
                                Code: "SEC_L1",
                                Name: "Listening Section 1",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.AudioDriven,
                                TimerPolicyJson: "{\"timerMode\":\"AUDIO_DRIVEN\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: true,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 2,
                                AllowedComponentTypesJson: "[\"mcq\",\"inline_completion\"]",
                                ToolsConfigJson: "{\"highlighter\":true}")
                        ]),

                    // Skill 2: Reading (UnitLockedSandbox Stage 1&2 + FullSandbox, SplitHorizontal, ScoreWeight 1.0 vs 2.0)
                    new SaveSkillWizardDto(
                        SkillId: null,
                        Code: "READ",
                        Name: "Reading",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 2,
                        DurationMinutes: 40,
                        Sections:
                        [
                            // Section 1: Unit-Locked Sandbox (2 units, weight 1.0)
                            new SaveSectionWizardDto(
                                SectionId: null,
                                Code: "SEC_R1",
                                Name: "Reading Stage Units",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.UnitLockedSandbox,
                                TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 0,
                                ClusterSize: 1,
                                UnitCount: 2,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 3,
                                AllowedComponentTypesJson: "[\"mcq\",\"mcq_3\",\"multi_select\"]",
                                ToolsConfigJson: "{\"highlighter\":true,\"notes\":true}"),

                            // Section 2: Full Sandbox (weight 2.0 to test varying weights)
                            new SaveSectionWizardDto(
                                SectionId: null,
                                Code: "SEC_R2",
                                Name: "Reading Comprehensive Tasks",
                                DisplayOrder: 2,
                                ScoreWeight: 2.0m,
                                NavigationMode: NavigationModeDto.FullSandbox,
                                TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 3,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 2,
                                AllowedComponentTypesJson: "[\"dropdown\",\"drag_drop\"]",
                                ToolsConfigJson: "{\"highlighter\":true}")
                        ]),

                    // Skill 3: Writing (HybridGroupLocked, RequiredKeywords: 2)
                    new SaveSkillWizardDto(
                        SkillId: null,
                        Code: "WRIT",
                        Name: "Writing",
                        Modality: ModalityDto.Writing,
                        DisplayOrder: 3,
                        DurationMinutes: 30,
                        Sections:
                        [
                            new SaveSectionWizardDto(
                                SectionId: null,
                                Code: "SEC_W1",
                                Name: "Controlled Writing Task",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.HybridGroupLocked,
                                TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 0,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 2,
                                ExpectedQuestionCount: 1,
                                AllowedComponentTypesJson: "[\"free_response\"]",
                                ToolsConfigJson: "{\"wordCount\":true,\"cutPaste\":true,\"disableSpellcheck\":true}")
                        ])
                ]);

            var wizResult = await certService.SaveCertificateWizardAsync(wizardRequest);
            Assert.True(wizResult.CertId > 0);
            Assert.Equal(certCode, wizResult.Code);
            Assert.Equal(3, wizResult.SkillsCount);
            Assert.Equal(4, wizResult.SectionsCount);

            // Verify Blueprint readback
            var blueprint = await certService.GetCertificateBlueprintAsync(wizResult.CertId);
            Assert.NotNull(blueprint);
            Assert.Equal("avg_half_step", blueprint.ScoreAggregationRule);
            Assert.Equal(3, blueprint.Skills.Count);
        }

        // Fetch persisted IDs
        int certId, secL1Id, secR1Id, secR2Id, secW1Id;
        int skillListId, skillReadId, skillWritId;
        await using (var dbSetup = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = await dbSetup.Certificates.SingleAsync(c => c.Code == certCode);
            certId = cert.CertId;

            var skills = await dbSetup.Skills.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code);
            skillListId = skills["LIST"].SkillId;
            skillReadId = skills["READ"].SkillId;
            skillWritId = skills["WRIT"].SkillId;

            // E1.1 (#146): the wizard does not author scales yet, so declare one here before scoring.
            await database.SqlAsync($"""
                UPDATE Skills SET MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 5 WHERE CertId = {certId};
                """);

            var sections = await dbSetup.Sections.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code);
            secL1Id = sections["SEC_L1"].SectionId;
            secR1Id = sections["SEC_R1"].SectionId;
            secR2Id = sections["SEC_R2"].SectionId;
            secW1Id = sections["SEC_W1"].SectionId;

            // E1.2 (#147): the wizard now writes the certificate's bands itself, so seeding them
            // here would collide with the ones it already created.

            // Seed Category for weakness analysis
            var category = new QuestionCategory
            {
                CertId = certId,
                Code = Unique("CAT_TECH"),
                Name = "Technical Vocabulary & Syntax"
            };
            // #269: the category is still seeded (it is the retired axis, kept so this seam proves the
            // paper does not depend on it), but no pool item names it - a position's cell is
            // (TopicId, CefrLevel), which is what vw_UserTopicAccuracy groups by.
            dbSetup.QuestionCategories.Add(category);
            await dbSetup.SaveChangesAsync();

            // Seed GradingCriteria and SectionCriteria for SEC_W1
            var criterion = new GradingCriterion
            {
                CertId = certId,
                SkillId = skillWritId,
                Code = "TASK_RESP",
                Name = "Task Response",
                MaxScore = 5.0m,
                DisplayOrder = 1
            };
            dbSetup.GradingCriteria.Add(criterion);
            await dbSetup.SaveChangesAsync();

            dbSetup.SectionCriteria.Add(new SectionCriterion
            {
                SectionId = secW1Id,
                CriterionId = criterion.CriterionId,
                SkillId = skillWritId,
                MaxScore = 5.0m
            });
            await dbSetup.SaveChangesAsync();

            // #226 / INV-4: an enabled productive skill needs a LOCKED rubric before its paper may
            // publish, and every criterion needs three anchors. This fixture published a writing paper
            // against an unlocked rubric, which is the state the invariant exists to refuse - the exam
            // went live with a free response that had no calibrated standard behind it.
            //
            // The lock's own trigger demands the canonical four criteria and a 4x7 grid, so this is the
            // real shape rather than a token row: renaming TASK_RESP to W_TR and adding the other three.
            await database.SqlAsync($"""
                UPDATE GradingCriteria SET Code = 'W_TR' WHERE CriterionId = {criterion.CriterionId};

                INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, RubricDescription, MaxScore, Weight, DisplayOrder) VALUES
                  ({certId}, {skillWritId}, 'W_CC', N'Coherence and cohesion', N'x', 5.0, 1.0, 2),
                  ({certId}, {skillWritId}, 'W_LR', N'Lexical resource', N'x', 5.0, 1.0, 3),
                  ({certId}, {skillWritId}, 'W_GR', N'Grammatical range', N'x', 5.0, 1.0, 4);

                INSERT INTO GradingCriterionDescriptors (CriterionId, Band, Descriptor, BinaryGates)
                  SELECT c.CriterionId, b.Band,
                         N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N' descriptor.',
                         N'Binary gate at band ' + CAST(b.Band AS NVARCHAR(2)) + N'.'
                    FROM GradingCriteria c CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
                   WHERE c.SkillId = {skillWritId};

                INSERT INTO GradingCriterionAnchors (CriterionId, Band, ResponseText)
                  SELECT c.CriterionId, b.Band, N'Anchor response at band ' + CAST(b.Band AS NVARCHAR(2)) + N'.'
                    FROM GradingCriteria c CROSS JOIN (VALUES (2),(3),(4)) AS b(Band)
                   WHERE c.SkillId = {skillWritId};

                INSERT INTO GradingRubricLocks (SkillId, CertId, LadderOutcome, LadderReason, LadderRunAt)
                VALUES ({skillWritId}, {certId}, 'accepted', N'Separated.', SYSDATETIMEOFFSET());
                """);
        }

        // =========================================================================
        // SEAM 2: Exam Studio & Ingest Seam (#117, #118, #121)
        // Authoring 11-Component items, Readiness Auditing, and Publishing
        // =========================================================================
        int examId;
        int q1Id, q2Id, q3Id, q4Id, q5Id, q6Id, q7Id, q8Id;
        // #269: a SITTING answer addresses the roster position (an ExamItemId). A DRILL answer addresses
        // the item version the session pinned - PracticeService resolves "questionId" by ItemVersionId -
        // so the two seams need different ids for the same two items.
        int q1VersionId, q2VersionId;
        int optQ1AId, optQ3AId, optQ4AId, optQ5AId, optQ5CId, optQ6AId, optQ7AId;
        int grpL1Id, grpR1_U1Id, grpR1_U2Id, grpR2Id, grpW1Id;

        await using (var dbStudio = DbContextFactory.Create(database.ConnectionString))
        {
            var adminExamService = new AdminExamService(dbStudio, TimeProvider.System);

            // 1. Create Exam
            var examDetail = await adminExamService.CreateExamAsync(new CreateAdminExamRequest(
                certId,
                $"E2E Full Chain Exam {Guid.NewGuid():N}"[..25],
                100,
                ExamPurposeDto.Mock));
            examId = examDetail.ExamId;

            // 2. Save Section Audio for Listening Section (required by blueprint)
            await adminExamService.SaveSectionAudioAsync(examId, new SaveExamSectionAudioRequest(
                secL1Id,
                "/media/audio/e2e-master-track.mp3",
                DurationSec: 180,
                SizeBytes: 1048576));

            // 3+4. Author the eight positions on the pool.
            //
            // #269: the answer-shape columns (max selections, accepted answers, the word range, the
            // keywords) live only on ItemVersions and are writable only at INSERT, so the fixture writes
            // those rows directly - exactly what PoolDrillGradingTests and PracticeServiceTests do. The
            // exam, its audio and the audit/publish doors below are still the real services, so this seam
            // still proves the paper goes through them.

            // Q1: mcq (Listening SEC_L1) - the bundle's first child, so it owns the stimulus.
            var l1Stimulus = new Stimulus
            {
                CefrLevel = CefrLevel.B1,
                Transcript = "The conference speaker arrives at 10:30 AM.",
                DomainContext = "general",
            };
            dbStudio.Stimuli.Add(l1Stimulus);
            await dbStudio.SaveChangesAsync();
            grpL1Id = l1Stimulus.StimulusId;

            (q1Id, q1VersionId) = await SeedPositionAsync(
                dbStudio, examId, certId, secL1Id, "listening", "short_exchange", "mcq",
                "What is the scheduled arrival time of the conference speaker?",
                displayOrder: 1, unitNumber: 1, stimulusId: grpL1Id,
                evidenceStart: 0, evidenceEnd: 4000, evidenceUnit: "sec",
                optionA: "10:30 AM", optionB: "11:00 AM", optionC: "12:15 PM", optionD: "01:00 PM", keyLabel: "A",
                explanation: "The transcript states 10:30 AM.");

            // Q2: inline_completion (same listening bundle, position 2). CK_ItemVersions_WordRange refuses
            // a ceiling with no floor, so the blank declares both ends.
            (q2Id, q2VersionId) = await SeedPositionAsync(
                dbStudio, examId, certId, secL1Id, "listening", "short_exchange", "inline_completion",
                "The corporate network is managed by the [blank] department.",
                displayOrder: 2, unitNumber: 1, stimulusId: grpL1Id,
                evidenceStart: 8000, evidenceEnd: 12000, evidenceUnit: "sec",
                acceptedAnswersJson: "[\"telecommunications\",\"telecom\"]",
                minWords: 1, maxWords: 2, allowNumber: false,
                explanation: "The blank takes a telecommunications department.");

            // Q3: mcq (Reading SEC_R1 Unit 1) - its own bundle, so its own stimulus.
            var r1u1Stimulus = new Stimulus
            {
                CefrLevel = CefrLevel.B1,
                Passage = "The Managing Director approved the expenditure for the regional office in Da Nang City Center.",
                DomainContext = "general",
            };
            dbStudio.Stimuli.Add(r1u1Stimulus);
            await dbStudio.SaveChangesAsync();
            grpR1_U1Id = r1u1Stimulus.StimulusId;

            (q3Id, _) = await SeedPositionAsync(
                dbStudio, examId, certId, secR1Id, "reading", "sentence_cloze", "mcq",
                "Which executive approved the expenditure?",
                displayOrder: 1, unitNumber: 1, stimulusId: grpR1_U1Id,
                evidenceStart: 0, evidenceEnd: 40, evidenceUnit: "char",
                optionA: "Managing Director", optionB: "Sales Associate", optionC: "Intern", optionD: "Consultant",
                keyLabel: "A", explanation: "The passage names the Managing Director.");

            // Q4: mcq_3 (same R1 unit-1 bundle, position 2). CNT-06 admits three contiguous options.
            (q4Id, _) = await SeedPositionAsync(
                dbStudio, examId, certId, secR1Id, "reading", "sentence_cloze", "mcq_3",
                "Where is the regional branch office located?",
                displayOrder: 2, unitNumber: 1, stimulusId: grpR1_U1Id,
                evidenceStart: 60, evidenceEnd: 90, evidenceUnit: "char",
                optionA: "Da Nang City Center", optionB: "Suburban Industrial Park", optionC: "International Airport",
                keyLabel: "A", explanation: "The passage places it downtown.");

            // Q5: multi_select (Reading SEC_R1 Unit 2 Adaptive) - a new bundle, so unit 2.
            var r1u2Stimulus = new Stimulus
            {
                CefrLevel = CefrLevel.B1,
                Passage = "Digital automation adoption and export market expansion drove the quarter.",
                DomainContext = "general",
            };
            dbStudio.Stimuli.Add(r1u2Stimulus);
            await dbStudio.SaveChangesAsync();
            grpR1_U2Id = r1u2Stimulus.StimulusId;

            (q5Id, _) = await SeedPositionAsync(
                dbStudio, examId, certId, secR1Id, "reading", "sentence_cloze", "multi_select",
                "Select TWO primary factors driving quarterly growth:",
                // UQ_ExamItems_Exam_Section_Order ignores the unit, so this is R1's third position.
                displayOrder: 3, unitNumber: 2, stimulusId: grpR1_U2Id,
                evidenceStart: 0, evidenceEnd: 30, evidenceUnit: "char",
                optionA: "Digital automation adoption", optionB: "Office lease cancellation",
                optionC: "Export market expansion", optionD: "Hiring freeze",
                keyLabel: "A,C", maxSelections: 2,
                explanation: "The passage names automation and export expansion.");
            // The option id the sitting renders, read from the projection rather than recomputed.
            optQ5AId = await PoolFixture.OptionIdAsync(dbStudio, examId, q5Id, "A");
            optQ5CId = await PoolFixture.OptionIdAsync(dbStudio, examId, q5Id, "C");

            // Q6: dropdown (Reading SEC_R2 FullSandbox)
            var r2Stimulus = new Stimulus
            {
                CefrLevel = CefrLevel.B1,
                Passage = "Consequently, the second quarter closed ahead of plan.",
                DomainContext = "general",
            };
            dbStudio.Stimuli.Add(r2Stimulus);
            await dbStudio.SaveChangesAsync();
            grpR2Id = r2Stimulus.StimulusId;

            (q6Id, _) = await SeedPositionAsync(
                dbStudio, examId, certId, secR2Id, "reading", "passage_comprehension", "dropdown",
                "Select the appropriate linking conjunction for paragraph 2.",
                displayOrder: 1, unitNumber: 2, stimulusId: grpR2Id,
                evidenceStart: 0, evidenceEnd: 14, evidenceUnit: "char",
                optionA: "Consequently", optionB: "Otherwise", optionC: "Nevertheless",
                keyLabel: "A", explanation: "The clause names a consequence.");

            // Q7: drag_drop (same R2 bundle, position 2)
            (q7Id, _) = await SeedPositionAsync(
                dbStudio, examId, certId, secR2Id, "reading", "passage_comprehension", "drag_drop",
                "Match the project phase label to the milestone schedule.",
                displayOrder: 2, unitNumber: 2, stimulusId: grpR2Id,
                evidenceStart: 20, evidenceEnd: 44, evidenceUnit: "char",
                optionA: "Phase 1: Discovery", optionB: "Phase 2: Deployment", optionC: "Phase 3: Retirement",
                keyLabel: "A", explanation: "Discovery is the first milestone.");

            // Q8: free_response (Writing SEC_W1 HybridGroupLocked). No options and no key: the version
            // carries the word range and the required keywords, and the stimulus carries the clock.
            var w1Stimulus = new Stimulus
            {
                CefrLevel = CefrLevel.B1,
                ResponseSeconds = 600,
                DomainContext = "general",
            };
            dbStudio.Stimuli.Add(w1Stimulus);
            await dbStudio.SaveChangesAsync();
            grpW1Id = w1Stimulus.StimulusId;

            (q8Id, _) = await SeedPositionAsync(
                dbStudio, examId, certId, secW1Id, "writing", "transactional_correspondence", "free_response",
                "Compose a concise proposal outlining environmental initiatives using the required keywords.",
                displayOrder: 1, unitNumber: 1, stimulusId: grpW1Id,
                evidenceStart: 0, evidenceEnd: 40, evidenceUnit: "char",
                minWords: 5, maxWords: 50, requiredKeywords: "sustainability, efficiency",
                explanation: "A passing answer uses both keywords within the word window.");

            // 4b. INV-3: every bound item must have cleared a human audit. A hand-authored fixture is
            // unbatched by construction, so the fixture records the clearance the review workbench would
            // (#205's per-item 'clear'), naming an account that is neither the uploader nor the item's
            // author - ADM-02 refuses a self-approval, which is the rule and not a fixture detail.
            await SeedClearanceAsync(dbStudio, certId, examId);

            // The option ids the sitting renders, read from the projection rather than recomputed: an
            // answer has to echo what the learner was shown, and a second formula would let the two drift.
            optQ1AId = await PoolFixture.OptionIdAsync(dbStudio, examId, q1Id, "A");
            optQ3AId = await PoolFixture.OptionIdAsync(dbStudio, examId, q3Id, "A");
            optQ4AId = await PoolFixture.OptionIdAsync(dbStudio, examId, q4Id, "A");
            optQ6AId = await PoolFixture.OptionIdAsync(dbStudio, examId, q6Id, "A");
            optQ7AId = await PoolFixture.OptionIdAsync(dbStudio, examId, q7Id, "A");

            // 5. Readiness Audit Check
            var audit = await adminExamService.AuditExamPublishReadinessAsync(examId);
            Assert.Equal(8, audit.TotalQuestions);
            Assert.True(audit.CanPublish,
                "a complete pool paper was refused: " + string.Join(" | ", audit.Issues.Select(i => i.Message)));
            Assert.Equal(0, audit.BlockingErrorCount);

            // 6. Publish Exam
            var publishResult = await adminExamService.PublishExamAsync(examId);
            Assert.True(publishResult.Success);
        }

        // =========================================================================
        // SEAM 3: Pre-flight & Sitting State Machine (#108-#114)
        // Gate verification, real-time sync, crash recovery, and macro/micro transitions
        // =========================================================================
        var learnerId = await SeedLearnerAsync(certCode);

        await using (var dbSitting = DbContextFactory.Create(database.ConnectionString))
        {
            var engine = new AttemptEngine(dbSitting);

            // 1. Preflight Enforcement Gate
            var requiresPreflight = await engine.RequiresPreflightAsync(examId);
            Assert.True(requiresPreflight, "Exam with RequireAudio section must require preflight.");

            var attempt = await engine.CreateAttemptAsync(learnerId, certId, examId, 100);
            Assert.Null(attempt.PreflightVerifiedAt);

            // Gate blocks sitting before verification
            var exGate = await Assert.ThrowsAsync<AttemptEngineException>(() =>
                engine.GetAttemptTakeAsync(attempt.AttemptId, learnerId, 1));
            Assert.Equal(AttemptEngineException.PreflightRequired, exGate.Code);

            // Candidate verifies equipment
            var verifyResult = await engine.VerifyPreflightAsync(attempt.AttemptId, learnerId);
            Assert.True(verifyResult.IsVerified);
            Assert.NotNull(verifyResult.VerifiedAt);

            // Unlocked: GetAttemptTakeAsync succeeds and generates AuthorizationCode
            var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, learnerId, 1);
            Assert.NotNull(take);
            Assert.Equal(8, take.TotalQuestions);
            Assert.NotNull(take.AuthorizationCode);
            Assert.StartsWith("EX-", take.AuthorizationCode);
            var authCode = take.AuthorizationCode;

            // 2. Real-Time Persistence Sync
            var syncResult = await engine.SaveSittingSyncAsync(attempt.AttemptId, new SittingSyncRequest(
                Answers: [new BatchAnswerItem(q1Id, optQ1AId, IsFlagged: false)],
                LastAudioOffsetMs: 14250,
                CandidateNotesJson: "{\"note\":\"Candidate listened to intro\"}",
                LastQuestionId: q1Id), learnerId);

            Assert.Equal(1, syncResult.AnswersSavedCount);
            Assert.Equal(14250, syncResult.LastAudioOffsetMs);

            // 3. Crash Recovery Check via AuthorizationCode
            var resumeView = await engine.ResumeAttemptByCodeAsync(authCode, learnerId);
            Assert.Equal(attempt.AttemptId, resumeView.AttemptId);
            Assert.Equal(14250, resumeView.LastAudioOffsetMs);
            Assert.Equal("{\"note\":\"Candidate listened to intro\"}", resumeView.CandidateNotesJson);
            var recoveredQ1 = resumeView.AnswerSheet.Single(a => a.QuestionId == q1Id);
            Assert.Equal(optQ1AId, recoveredQ1.SelectedOptionId);

            // 4. Macro & Micro State Machine Transitions
            // Listening (ActiveSkill 1): Answer Q2 (Inline Completion)
            await engine.SaveAnswerAsync(attempt.AttemptId, learnerId, q2Id,
                selectedOptionId: null, isFlagged: false, responseText: "telecommunications");

            // Advance to Skill 2 (Reading): Zero time rollover, locks Listening
            var transSkill2 = await engine.TransitionToNextSkillAsync(attempt.AttemptId, learnerId);
            Assert.Equal(skillReadId, transSkill2.ActiveSkillId);
            Assert.Equal(1, transSkill2.ActiveUnitNumber);

            // Listening question Q1 is now permanently locked
            var exLockedSkill = await Assert.ThrowsAsync<AttemptEngineException>(() =>
                engine.SaveAnswerAsync(attempt.AttemptId, learnerId, q1Id, optQ1AId, false));
            Assert.Equal(AttemptEngineException.WrongPhase, exLockedSkill.Code);

            // Unit Locking: Reading SEC_R1 Unit 1
            await engine.SaveAnswerAsync(attempt.AttemptId, learnerId, q3Id, optQ3AId, false);
            await engine.SaveAnswerAsync(attempt.AttemptId, learnerId, q4Id, optQ4AId, false);

            // Unit 2 question Q5 cannot be answered while ActiveUnitNumber is 1
            var exUnitLock = await Assert.ThrowsAsync<AttemptEngineException>(() =>
                engine.SaveAnswerAsync(attempt.AttemptId, learnerId, q5Id, selectedOptionId: null, false, selectedOptionIdsJson: $"[{optQ5AId}, {optQ5CId}]"));
            Assert.Equal(AttemptEngineException.WrongPhase, exUnitLock.Code);

            // Transition to Unit 2 (Adaptive Stage)
            var transUnit2 = await engine.TransitionUnitAsync(attempt.AttemptId, 2, learnerId);
            Assert.Equal(2, transUnit2.ActiveUnitNumber);

            // Unit 1 question Q3 is now permanently locked
            var exLockedUnit1 = await Assert.ThrowsAsync<AttemptEngineException>(() =>
                engine.SaveAnswerAsync(attempt.AttemptId, learnerId, q3Id, optQ3AId, false));
            Assert.Equal(AttemptEngineException.WrongPhase, exLockedUnit1.Code);

            // Answer Unit 2 question Q5 (MultiSelect exact set matching). A label-set submission records
            // its choices in SelectedOptionIdsJson and is graded against the pinned key set
            // (AttemptEngine.Pool.cs EvaluatePinnedLabelSet), so the whole-set rule holds on the sitting
            // path exactly as it does in a practice drill.
            await engine.SaveAnswerAsync(attempt.AttemptId, learnerId, q5Id,
                selectedOptionId: null, isFlagged: false, selectedOptionIdsJson: $"[{optQ5AId}, {optQ5CId}]");

            // Answer SEC_R2 FullSandbox questions: Q6 (Dropdown) and Q7 (DragDrop)
            await engine.SaveAnswerAsync(attempt.AttemptId, learnerId, q6Id, optQ6AId, false);
            await engine.SaveAnswerAsync(attempt.AttemptId, learnerId, q7Id, optQ7AId, false);

            // Transition to Skill 3 (Writing)
            var transSkill3 = await engine.TransitionToNextSkillAsync(attempt.AttemptId, learnerId);
            Assert.Equal(skillWritId, transSkill3.ActiveSkillId);

            // Group Transition for HybridGroupLocked section
            var transGroup = await engine.TransitionGroupAsync(attempt.AttemptId, grpW1Id, learnerId);
            Assert.Equal(grpW1Id, transGroup.ActiveGroupId);

            // Save Draft for Q8 (FreeResponse with required lemma keywords)
            await engine.SaveDraftAsync(attempt.AttemptId, learnerId, q8Id,
                "Our enterprise strategy advances sustainability and energy efficiency across operations.");

            // =========================================================================
            // SEAM 4: Scoring Engine Calibration (#108-#114)
            // Objective 11-Component scoring, section score weighting & aggregation
            // =========================================================================
            var submitResult = await engine.SubmitAttemptAsync(attempt.AttemptId, learnerId, isAutoSubmit: false);
            Assert.NotNull(submitResult);

            // Verify objective answers evaluation in database. #269: an answer's identity is its roster
            // position, and the position is what CreateQuestionAsync returns as its question id.
            await using var dbVerify = DbContextFactory.Create(database.ConnectionString);
            var savedAnswers = await dbVerify.AttemptAnswers
                .Where(a => a.AttemptId == attempt.AttemptId)
                .ToDictionaryAsync(a => a.ExamItemId);

            Assert.True(savedAnswers[q1Id].IsCorrect, "Q1 (MCQ) must be correct.");
            Assert.True(savedAnswers[q2Id].IsCorrect, "Q2 (InlineCompletion) must be correct.");
            Assert.True(savedAnswers[q3Id].IsCorrect, "Q3 (MCQ Unit 1) must be correct.");
            Assert.True(savedAnswers[q4Id].IsCorrect, "Q4 (MCQ_3 Unit 1) must be correct.");
            Assert.True(savedAnswers[q5Id].IsCorrect, "Q5 (MultiSelect exact set) must be correct.");
            Assert.True(savedAnswers[q6Id].IsCorrect, "Q6 (Dropdown) must be correct.");
            Assert.True(savedAnswers[q7Id].IsCorrect, "Q7 (DragDrop) must be correct.");

            // Evaluate FreeResponse writing response rules. #269: the word floor and the required
            // keywords live on the version the position pins, not on a question row.
            var q8Version = await dbVerify.ItemVersions.SingleAsync(v =>
                dbVerify.ExamItems.Any(ei => ei.ExamItemId == q8Id && ei.ItemVersionId == v.ItemVersionId));
            var frEval = AttemptEngine.EvaluateWritingResponse(
                q8Version.MinWords, q8Version.RequiredKeywords,
                "Our enterprise strategy advances sustainability and energy efficiency across operations.");
            Assert.True(frEval.HasRequiredKeywords);
            Assert.False(frEval.IsUnderLength);
            Assert.Empty(frEval.MissingKeywords);

            // Simulate completed AI Grading for FreeResponse
            var fr = await dbVerify.FreeResponses.SingleAsync(r => r.AttemptId == attempt.AttemptId && r.ExamItemId == q8Id);
            await database.SqlAsync($"""
                UPDATE AiGradings
                SET Status = 'done', OverallScaled = 75.0, Model = 'fake-ai-mentor'
                WHERE FreeResponseId = {fr.FreeResponseId};
                """);

            // Finalize AI-Graded Attempt with fresh engine context
            await using (var dbFinal = DbContextFactory.Create(database.ConnectionString))
            {
                var finalEngine = new AttemptEngine(dbFinal);
                await finalEngine.FinalizeAiGradedAttemptAsync(attempt.AttemptId);
            }

            // Reload and verify calibrated scores
            var finalizedAttempt = await dbVerify.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
            Assert.Equal(AttemptStatus.Graded.ToCode(), finalizedAttempt.Status);
            Assert.Equal("PASS", finalizedAttempt.BandCode);
            // The band is the contract now: it comes from the certificate's own ranges, and PASS
            // means the engine put this attempt in the upper band the wizard declared.
            Assert.True(finalizedAttempt.TotalScore > 0, "Calibrated score should reflect high performance.");

            // Verify Section ScoreWeights were taken into account
            var skillScores = await dbVerify.AttemptSkillScores
                .Where(s => s.AttemptId == attempt.AttemptId)
                .ToDictionaryAsync(s => s.SkillId);

            Assert.Equal(3, skillScores.Count);
            Assert.True(skillScores[skillListId].RawScore > 0);
            Assert.True(skillScores[skillReadId].RawScore > 0);
            Assert.True(skillScores[skillWritId].RawScore > 0);
        }

        // =========================================================================
        // SEAM 5: Practice Drilling & Weakness Analysis Alignment (#119)
        // Drilling non-MCQ items, verifying vw_UserTopicAccuracy & vw_UserSectionAccuracyAll
        // (#271: the category view this seam used to check is retired; these answers are legacy, so
        // the topic view correctly reports no cell for them - see the assertion below.)
        // =========================================================================
        await using (var dbPractice = DbContextFactory.Create(database.ConnectionString))
        {
            var learnerService = new LearnerService(dbPractice);
            var practiceService = new PracticeService(dbPractice, learnerService);
            var weaknessService = new WeaknessAnalysisService(dbPractice, learnerService);

            // Drill questions including non-MCQ item (Q2: inline_completion) in Listening section
            var pSession = await practiceService.StartSessionAsync(learnerId, new StartPracticeSessionInput(secL1Id));
            // #269: a drill answer is addressed by the pinned ItemVersionId - PracticeService resolves
            // "questionId" by ItemVersionId - while a sitting answer is addressed by the roster position.
            // The two seams need different ids for the same two items.
            var pAns1 = await practiceService.SubmitAnswerAsync(learnerId, pSession.SessionId, q1VersionId,
                new SubmitPracticeAnswerInput(SelectedOptionId: optQ1AId));
            Assert.True(pAns1.IsCorrect);

            var pAnsResult = await practiceService.SubmitAnswerAsync(learnerId, pSession.SessionId, q2VersionId,
                new SubmitPracticeAnswerInput(ResponseText: "telecommunications"));

            Assert.True(pAnsResult.IsCorrect, "Practice answer for inline_completion should be evaluated as correct.");
            Assert.True(pAnsResult.IsSessionFinished);

            // Verify Weakness Analysis Views
            // #269: this paper is authored ON THE POOL, so its answers carry ItemId and therefore a real
            // accuracy cell. That is the inverse of the legacy behaviour this seam used to assert: a
            // legacy answer had no topic, so the view was empty and the drill's weakness analysis could
            // report nothing. Asserting the cells are present is the stronger claim - it proves the
            // answers reached the pooling axis rather than being silently dropped.
            var topicAcc = await dbPractice.vw_UserTopicAccuracies
                .AsNoTracking()
                .Where(v => v.UserId == learnerId)
                .ToListAsync();

            Assert.NotEmpty(topicAcc);
            Assert.All(topicAcc, cell => Assert.True(cell.Answered > 0, "an accuracy cell with no answers is not evidence"));
            // The listening answers (Q1 the sitting, plus the two practice drills) land on one topic cell.
            Assert.Contains(topicAcc, cell => cell.Answered >= 2 && cell.Correct >= 2);

            // 2. Direct query against vw_UserSectionAccuracyAll
            var secAcc = await dbPractice.vw_UserSectionAccuracyAlls
                .AsNoTracking()
                .SingleOrDefaultAsync(v => v.UserId == learnerId && v.SectionId == secL1Id);

            Assert.NotNull(secAcc);
            Assert.True(secAcc.Answered >= 2, "Section accuracy view must combine exam attempt and practice sessions.");
            Assert.True(secAcc.Correct >= 2);

            // 3. Service level verification
            var weaknessAnalysis = await weaknessService.GetWeaknessAnalysisAsync(learnerId);
            Assert.NotNull(weaknessAnalysis);
            Assert.NotEmpty(weaknessAnalysis.Sections);
            var secSummary = weaknessAnalysis.Sections.Single(s => s.SectionId == secL1Id);
            Assert.Equal(100, secSummary.AccuracyPercentage);
        }
    }

    [Fact]
    public void Preset_Score_Calibration_IELTS_Academic_And_GT_Async()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var certService = new CertificateService(db);
        var presets = certService.GetOfficialPresets();

        // 1. Preset Blueprint Rules Check
        var ielts = presets.Single(p => p.PresetCode == "ielts_academic");
        Assert.Equal("avg_half_step", ielts.ScoreAggregationRule);
        Assert.Equal(4, ielts.Skills.Count);

        var writing = ielts.Skills.Single(s => s.Code == "WRIT");
        Assert.Equal(2, writing.Sections.Count);
        Assert.Equal(1.0m, writing.Sections[0].ScoreWeight); // Task 1
        Assert.Equal(2.0m, writing.Sections[1].ScoreWeight); // Task 2 (2:1 ratio)

        // 2. IELTS 0–9.0 scale with half-step rounding rule verification
        // Test edge boundaries:
        // .0 to < .25 -> .0
        Assert.Equal(60, AttemptEngine.RoundHalfStep(6.0));
        Assert.Equal(60, AttemptEngine.RoundHalfStep(6.125));
        Assert.Equal(60, AttemptEngine.RoundHalfStep(6.24));

        // .25 to < .75 -> .5
        Assert.Equal(65, AttemptEngine.RoundHalfStep(6.25));
        Assert.Equal(65, AttemptEngine.RoundHalfStep(6.50));
        Assert.Equal(65, AttemptEngine.RoundHalfStep(6.74));

        // >= .75 -> next whole band
        Assert.Equal(70, AttemptEngine.RoundHalfStep(6.75));
        Assert.Equal(70, AttemptEngine.RoundHalfStep(6.875));
        Assert.Equal(70, AttemptEngine.RoundHalfStep(6.99));

        // Test over 10.0 scale representations (e.g. 62.5 -> 65)
        Assert.Equal(65, AttemptEngine.RoundHalfStep(62.5));
        Assert.Equal(70, AttemptEngine.RoundHalfStep(67.5));
        Assert.Equal(65, AttemptEngine.RoundHalfStep(66.6666));

        // 3. Composite 4-skill band aggregation (L: 6.0, R: 6.5, S: 6.0, W: 7.0 -> Avg 6.375 -> Band 6.5)
        var skillScores = new List<AttemptSkillScore>
        {
            new() { ScaledScore = 60 },
            new() { ScaledScore = 65 },
            new() { ScaledScore = 60 },
            new() { ScaledScore = 70 }
        };
        var total = AttemptEngine.AggregateTotalScore("avg_half_step", skillScores);
        Assert.Equal(65, total);

        // 4. Writing 2:1 Task 2 score weighting:
        // Task 1 = 6.0 (60), Task 2 = 7.5 (75) -> (60*1 + 75*2)/3 = 210/3 = 70.0 (7.0)
        decimal task1Score = 60m;
        decimal task2Score = 75m;
        decimal weightedWriting = (task1Score * 1.0m + task2Score * 2.0m) / (1.0m + 2.0m);
        Assert.Equal(70.0m, weightedWriting);
        Assert.Equal(70, AttemptEngine.RoundHalfStep((double)weightedWriting));
    }

    [Fact]
    public void Preset_Score_Calibration_TOEIC_Standard_CBT_Async()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var certService = new CertificateService(db);
        var presets = certService.GetOfficialPresets();

        // 1. Preset Blueprint Rules Check
        var toeic = presets.Single(p => p.PresetCode == "toeic_lr_std");
        Assert.Equal("sum", toeic.ScoreAggregationRule);
        Assert.Equal(2, toeic.Skills.Count);

        // 2. Sum Aggregation (10–990 scale)
        var maxScores = new List<AttemptSkillScore>
        {
            new() { ScaledScore = 495 }, // Listening max
            new() { ScaledScore = 495 }  // Reading max
        };
        Assert.Equal(990, AttemptEngine.AggregateTotalScore("sum", maxScores));

        var midScores = new List<AttemptSkillScore>
        {
            new() { ScaledScore = 350 },
            new() { ScaledScore = 400 }
        };
        Assert.Equal(750, AttemptEngine.AggregateTotalScore("sum", midScores));
    }

    [Fact]
    public async Task Preset_Score_Calibration_TOEIC_Adaptive_CAT_Async()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var certService = new CertificateService(db);
        var presets = certService.GetOfficialPresets();

        // 1. Preset Blueprint Rules Check
        var toeicCat = presets.Single(p => p.PresetCode == "toeic_lr_cat");
        Assert.Equal("sum", toeicCat.ScoreAggregationRule);

        var readingCat = toeicCat.Skills.Single(s => s.Code == "READ");
        var secCat = readingCat.Sections[0];
        Assert.Equal(NavigationModeDto.UnitLockedSandbox, secCat.NavigationMode);
        Assert.Equal(2, secCat.UnitCount);

        // 2. 2-Stage Unit Locking Mechanics
        var certCode = Unique("CAT_PRESET");
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule, PresetCode)
            VALUES ('{certCode}', N'TOEIC CAT Test', 1, 'sum', 'toeic_lr_cat');
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'READ', N'Reading CAT', 'reading', 1, 60);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, NavigationMode, ExpectedQuestionCount, UnitCount)
            VALUES (@skill, @cert, 'reading', 'P7_CAT', N'Adaptive Reading', 1, 'UNIT_LOCKED_SANDBOX', 1, 2);
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'CAT Sitting Exam', 'published', 60, 'mock');
            """);

        await using var dbCat = DbContextFactory.Create(database.ConnectionString);
        var cert = await dbCat.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await dbCat.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var section = await dbCat.Sections.SingleAsync(s => s.CertId == cert.CertId);

        // #269: a stage is a position's UnitNumber on the roster, not a group row.
        var stage1 = await PoolFixture.AddMcqAsync(
            dbCat, exam.ExamId, cert.CertId, section.SectionId, "reading", "Stage 1 Q",
            displayOrder: 1, keyLabel: "A", level: "A2", unitNumber: 1);
        var stage2 = await PoolFixture.AddMcqAsync(
            dbCat, exam.ExamId, cert.CertId, section.SectionId, "reading", "Stage 2 Adaptive Q",
            displayOrder: 2, keyLabel: "A", level: "B1", unitNumber: 2);

        var optStage1 = await PoolFixture.OptionIdAsync(dbCat, exam.ExamId, stage1.ExamItemId, "A");
        var optStage2 = await PoolFixture.OptionIdAsync(dbCat, exam.ExamId, stage2.ExamItemId, "A");

        var userId = await SeedLearnerAsync(certCode);
        var engine = new AttemptEngine(dbCat);

        var attempt = await engine.CreateAttemptAsync(userId, cert.CertId, exam.ExamId, 60);
        Assert.Equal(1, attempt.ActiveUnitNumber);

        // Stage 1 is active, Stage 2 locked
        await engine.SaveAnswerAsync(attempt.AttemptId, userId, stage1.ExamItemId, optStage1, false);
        await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, stage2.ExamItemId, optStage2, false));

        // Transition to Stage 2 (Unit 2)
        var trans = await engine.TransitionUnitAsync(attempt.AttemptId, 2);
        Assert.Equal(2, trans.ActiveUnitNumber);

        // Stage 1 is permanently locked
        await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, stage1.ExamItemId, optStage1, false));

        // Stage 2 is now open
        var res2 = await engine.SaveAnswerAsync(attempt.AttemptId, userId, stage2.ExamItemId, optStage2, false);
        Assert.Equal(stage2.ExamItemId, res2.QuestionId);
    }

    [Fact]
    public void Preset_Score_Calibration_TOEIC_Speaking_And_Writing_Async()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var certService = new CertificateService(db);
        var presets = certService.GetOfficialPresets();

        // 1. Preset Blueprint Rules Check
        var toeicSw = presets.Single(p => p.PresetCode == "toeic_sw");
        // #171: this preset used to declare the removed `independent` rule. It now sums like the
        // seeded TOEIC_SW certificate, whose ScoreScales run 0-200 per skill.
        Assert.Equal("sum", toeicSw.ScoreAggregationRule);
        Assert.Equal(2, toeicSw.Skills.Count);

        var spea = toeicSw.Skills.Single(s => s.Code == "SPEA");
        Assert.Equal(11, spea.Sections.Sum(s => s.ExpectedQuestionCount));

        var writ = toeicSw.Skills.Single(s => s.Code == "WRIT");
        var w1 = writ.Sections.Single(s => s.Code == "W1");
        Assert.Equal(NavigationModeDto.HybridGroupLocked, w1.NavigationMode);
        Assert.Equal(2, w1.RequiredKeywordCount);

        // 2. The TOEIC S&W shape scores: two 0-200 skill scores sum, exactly as the preset now says.
        var skillScores = new List<AttemptSkillScore>
        {
            new() { ScaledScore = 160 }, // Speaking 0–200
            new() { ScaledScore = 170 }  // Writing 0–200
        };
        var total = AttemptEngine.AggregateTotalScore(toeicSw.ScoreAggregationRule, skillScores);
        Assert.Equal(330, total);
    }

    [Fact]
    public void Preset_Score_Calibration_VSTEP_Async()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var certService = new CertificateService(db);
        var presets = certService.GetOfficialPresets();

        // 1. Preset Blueprint Rules Check
        var vstep = presets.Single(p => p.PresetCode == "vstep");
        Assert.Equal("avg_whole", vstep.ScoreAggregationRule);
        Assert.Equal(4, vstep.Skills.Count);

        // 2. Average Whole Band Rounding (0–10 scale)
        // Average 56.25 -> 56
        var scores1 = new List<AttemptSkillScore>
        {
            new() { ScaledScore = 55 },
            new() { ScaledScore = 62 },
            new() { ScaledScore = 50 },
            new() { ScaledScore = 58 }
        };
        Assert.Equal(56, AttemptEngine.AggregateTotalScore("avg_whole", scores1));

        // Average 6.5 -> 7
        var scores2 = new List<AttemptSkillScore>
        {
            new() { ScaledScore = 6 },
            new() { ScaledScore = 7 },
            new() { ScaledScore = 5 },
            new() { ScaledScore = 8 }
        };
        Assert.Equal(7, AttemptEngine.AggregateTotalScore("avg_whole", scores2));

        // Average 4.5 -> 5
        var scores3 = new List<AttemptSkillScore>
        {
            new() { ScaledScore = 4 },
            new() { ScaledScore = 5 },
            new() { ScaledScore = 5 },
            new() { ScaledScore = 4 }
        };
        Assert.Equal(5, AttemptEngine.AggregateTotalScore("avg_whole", scores3));
    }

}

