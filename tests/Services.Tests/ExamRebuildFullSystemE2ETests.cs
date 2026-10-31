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
/// - TOEIC S&W: Independent skill scoring (0–200 each)
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

        // Fetch persisted IDs & seed ScoreBands & QuestionCategory
        int certId, secL1Id, secR1Id, secR2Id, secW1Id;
        int skillListId, skillReadId, skillWritId;
        int catId;
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

            // Seed ScoreBands for calibrated band matching
            dbSetup.ScoreBands.AddRange(
                new ScoreBand { CertId = certId, Code = "LOW", Name = "Developing", MinTotal = 0, MaxTotal = 59, DisplayOrder = 1 },
                new ScoreBand { CertId = certId, Code = "HIGH", Name = "Competent", MinTotal = 60, MaxTotal = 100, DisplayOrder = 2 }
            );

            // Seed Category for weakness analysis
            var category = new QuestionCategory
            {
                CertId = certId,
                Code = Unique("CAT_TECH"),
                Name = "Technical Vocabulary & Syntax"
            };
            dbSetup.QuestionCategories.Add(category);
            await dbSetup.SaveChangesAsync();
            catId = category.CategoryId;

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
        }

        // =========================================================================
        // SEAM 2: Exam Studio & Ingest Seam (#117, #118, #121)
        // Authoring 11-Component items, Readiness Auditing, and Publishing
        // =========================================================================
        int examId;
        int q1Id, q2Id, q3Id, q4Id, q5Id, q6Id, q7Id, q8Id;
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

            // 3. Question Groups
            var grpL1 = await adminExamService.CreateQuestionGroupAsync(examId, new CreateQuestionGroupRequest(secL1Id, UnitNumber: 1));
            grpL1Id = grpL1.GroupId;

            var grpR1_U1 = await adminExamService.CreateQuestionGroupAsync(examId, new CreateQuestionGroupRequest(secR1Id, UnitNumber: 1));
            grpR1_U1Id = grpR1_U1.GroupId;

            var grpR1_U2 = await adminExamService.CreateQuestionGroupAsync(examId, new CreateQuestionGroupRequest(secR1Id, UnitNumber: 2));
            grpR1_U2Id = grpR1_U2.GroupId;

            var grpR2 = await adminExamService.CreateQuestionGroupAsync(examId, new CreateQuestionGroupRequest(secR2Id, UnitNumber: 2));
            grpR2Id = grpR2.GroupId;

            var grpW1 = await adminExamService.CreateQuestionGroupAsync(examId, new CreateQuestionGroupRequest(secW1Id, UnitNumber: 1, ResponseSeconds: 600));
            grpW1Id = grpW1.GroupId;

            // 4. Author Questions across components:
            // Q1: mcq (in Listening SEC_L1)
            var q1 = await adminExamService.CreateQuestionAsync(examId, new CreateQuestionRequest(
                grpL1Id,
                "What is the scheduled arrival time of the conference speaker?",
                DifficultyLevel: 2,
                QuestionType: QuestionTypeDto.Mcq,
                CategoryId: catId,
                Options:
                [
                    new AdminQuestionOptionInput(null, "A", "10:30 AM", true, "Directly stated"),
                    new AdminQuestionOptionInput(null, "B", "11:00 AM", false, "Distractor"),
                    new AdminQuestionOptionInput(null, "C", "12:15 PM", false, "Distractor"),
                    new AdminQuestionOptionInput(null, "D", "01:00 PM", false, "Distractor")
                ]));
            q1Id = q1.QuestionId;
            optQ1AId = q1.Options.Single(o => o.Label == "A").OptionId;

            // Q2: inline_completion (in Listening SEC_L1)
            var q2 = await adminExamService.CreateQuestionAsync(examId, new CreateQuestionRequest(
                grpL1Id,
                "The corporate network is managed by the [blank] department.",
                DifficultyLevel: 3,
                QuestionType: QuestionTypeDto.InlineCompletion,
                CategoryId: catId,
                AcceptedAnswersJson: "[\"telecommunications\",\"telecom\"]",
                MaxWords: 2,
                AllowNumber: false));
            q2Id = q2.QuestionId;

            // Q3: mcq (in Reading SEC_R1 Unit 1)
            var q3 = await adminExamService.CreateQuestionAsync(examId, new CreateQuestionRequest(
                grpR1_U1Id,
                "Which executive approved the expenditure?",
                DifficultyLevel: 2,
                QuestionType: QuestionTypeDto.Mcq,
                CategoryId: catId,
                Options:
                [
                    new AdminQuestionOptionInput(null, "A", "Managing Director", true, "Approved"),
                    new AdminQuestionOptionInput(null, "B", "Sales Associate", false, null),
                    new AdminQuestionOptionInput(null, "C", "Intern", false, null),
                    new AdminQuestionOptionInput(null, "D", "Consultant", false, null)
                ]));
            q3Id = q3.QuestionId;
            optQ3AId = q3.Options.Single(o => o.Label == "A").OptionId;

            // Q4: mcq_3 (in Reading SEC_R1 Unit 1)
            var q4 = await adminExamService.CreateQuestionAsync(examId, new CreateQuestionRequest(
                grpR1_U1Id,
                "Where is the regional branch office located?",
                DifficultyLevel: 2,
                QuestionType: QuestionTypeDto.Mcq_3,
                CategoryId: catId,
                Options:
                [
                    new AdminQuestionOptionInput(null, "A", "Da Nang City Center", true, "Downtown"),
                    new AdminQuestionOptionInput(null, "B", "Suburban Industrial Park", false, null),
                    new AdminQuestionOptionInput(null, "C", "International Airport", false, null)
                ]));
            q4Id = q4.QuestionId;
            optQ4AId = q4.Options.Single(o => o.Label == "A").OptionId;

            // Q5: multi_select (in Reading SEC_R1 Unit 2 Adaptive)
            var q5 = await adminExamService.CreateQuestionAsync(examId, new CreateQuestionRequest(
                grpR1_U2Id,
                "Select TWO primary factors driving quarterly growth:",
                DifficultyLevel: 3,
                QuestionType: QuestionTypeDto.MultiSelect,
                CategoryId: catId,
                MaxSelections: 2,
                Options:
                [
                    new AdminQuestionOptionInput(null, "A", "Digital automation adoption", true, "Factor A"),
                    new AdminQuestionOptionInput(null, "B", "Office lease cancellation", false, null),
                    new AdminQuestionOptionInput(null, "C", "Export market expansion", true, "Factor C"),
                    new AdminQuestionOptionInput(null, "D", "Hiring freeze", false, null)
                ]));
            q5Id = q5.QuestionId;
            optQ5AId = q5.Options.Single(o => o.Label == "A").OptionId;
            optQ5CId = q5.Options.Single(o => o.Label == "C").OptionId;

            // Q6: dropdown (in Reading SEC_R2 FullSandbox)
            var q6 = await adminExamService.CreateQuestionAsync(examId, new CreateQuestionRequest(
                grpR2Id,
                "Select the appropriate linking conjunction for paragraph 2.",
                DifficultyLevel: 2,
                QuestionType: QuestionTypeDto.Dropdown,
                CategoryId: catId,
                Options:
                [
                    new AdminQuestionOptionInput(null, "A", "Consequently", true, "Correct conjunction"),
                    new AdminQuestionOptionInput(null, "B", "Otherwise", false, null),
                    new AdminQuestionOptionInput(null, "C", "Nevertheless", false, null)
                ]));
            q6Id = q6.QuestionId;
            optQ6AId = q6.Options.Single(o => o.Label == "A").OptionId;

            // Q7: drag_drop (in Reading SEC_R2 FullSandbox)
            var q7 = await adminExamService.CreateQuestionAsync(examId, new CreateQuestionRequest(
                grpR2Id,
                "Match the project phase label to the milestone schedule.",
                DifficultyLevel: 3,
                QuestionType: QuestionTypeDto.DragDrop,
                CategoryId: catId,
                Options:
                [
                    new AdminQuestionOptionInput(null, "A", "Phase 1: Discovery", true, "Correct slot"),
                    new AdminQuestionOptionInput(null, "B", "Phase 2: Deployment", false, null),
                    new AdminQuestionOptionInput(null, "C", "Phase 3: Retirement", false, null)
                ]));
            q7Id = q7.QuestionId;
            optQ7AId = q7.Options.Single(o => o.Label == "A").OptionId;

            // Q8: free_response (in Writing SEC_W1 HybridGroupLocked)
            var q8 = await adminExamService.CreateQuestionAsync(examId, new CreateQuestionRequest(
                grpW1Id,
                "Compose a concise proposal outlining environmental initiatives using the required keywords.",
                DifficultyLevel: 3,
                QuestionType: QuestionTypeDto.FreeResponse,
                ResponseSeconds: 600,
                MinWords: 5,
                MaxWords: 50,
                RequiredKeywords: "sustainability, efficiency"));
            q8Id = q8.QuestionId;

            // 5. Readiness Audit Check
            var audit = await adminExamService.AuditExamPublishReadinessAsync(examId);
            Assert.True(audit.CanPublish, $"Readiness audit blocked: {string.Join("; ", audit.Issues.Select(i => i.Message))}");
            Assert.Equal(0, audit.BlockingErrorCount);
            Assert.Equal(8, audit.TotalQuestions);

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

            // Answer Unit 2 question Q5 (MultiSelect exact set matching)
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

            // Verify objective answers evaluation in database
            await using var dbVerify = DbContextFactory.Create(database.ConnectionString);
            var savedAnswers = await dbVerify.AttemptAnswers
                .Where(a => a.AttemptId == attempt.AttemptId)
                .ToDictionaryAsync(a => a.QuestionId);

            Assert.True(savedAnswers[q1Id].IsCorrect, "Q1 (MCQ) must be correct.");
            Assert.True(savedAnswers[q2Id].IsCorrect, "Q2 (InlineCompletion) must be correct.");
            Assert.True(savedAnswers[q3Id].IsCorrect, "Q3 (MCQ Unit 1) must be correct.");
            Assert.True(savedAnswers[q4Id].IsCorrect, "Q4 (MCQ_3 Unit 1) must be correct.");
            Assert.True(savedAnswers[q5Id].IsCorrect, "Q5 (MultiSelect exact set) must be correct.");
            Assert.True(savedAnswers[q6Id].IsCorrect, "Q6 (Dropdown) must be correct.");
            Assert.True(savedAnswers[q7Id].IsCorrect, "Q7 (DragDrop) must be correct.");

            // Evaluate FreeResponse writing response rules
            var q8Entity = await dbVerify.Questions.SingleAsync(q => q.QuestionId == q8Id);
            var frEval = AttemptEngine.EvaluateWritingResponse(q8Entity, "Our enterprise strategy advances sustainability and energy efficiency across operations.");
            Assert.True(frEval.HasRequiredKeywords);
            Assert.False(frEval.IsUnderLength);
            Assert.Empty(frEval.MissingKeywords);

            // Simulate completed AI Grading for FreeResponse
            var fr = await dbVerify.FreeResponses.SingleAsync(r => r.AttemptId == attempt.AttemptId && r.QuestionId == q8Id);
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
            Assert.Equal("HIGH", finalizedAttempt.BandCode);
            Assert.True(finalizedAttempt.TotalScore >= 60, "Calibrated score should reflect high performance.");

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
        // Drilling non-MCQ items, verifying vw_UserCategoryAccuracy & vw_UserSectionAccuracyAll
        // =========================================================================
        await using (var dbPractice = DbContextFactory.Create(database.ConnectionString))
        {
            var learnerService = new LearnerService(dbPractice);
            var practiceService = new PracticeService(dbPractice, learnerService);
            var weaknessService = new WeaknessAnalysisService(dbPractice, learnerService);

            // Drill questions including non-MCQ item (Q2: inline_completion) in Listening section
            var pSession = await practiceService.StartSessionAsync(learnerId, new StartPracticeSessionInput(secL1Id, 2));
            var pAns1 = await practiceService.SubmitAnswerAsync(learnerId, pSession.SessionId, q1Id,
                new SubmitPracticeAnswerInput(SelectedOptionId: optQ1AId));
            Assert.True(pAns1.IsCorrect);

            var pAnsResult = await practiceService.SubmitAnswerAsync(learnerId, pSession.SessionId, q2Id,
                new SubmitPracticeAnswerInput(ResponseText: "telecommunications"));

            Assert.True(pAnsResult.IsCorrect, "Practice answer for inline_completion should be evaluated as correct.");
            Assert.True(pAnsResult.IsSessionFinished);

            // Verify Weakness Analysis Views
            // 1. Direct query against vw_UserCategoryAccuracy
            var catAcc = await dbPractice.vw_UserCategoryAccuracies
                .AsNoTracking()
                .SingleOrDefaultAsync(v => v.UserId == learnerId && v.CategoryId == catId);

            Assert.NotNull(catAcc);
            Assert.True(catAcc.Answered >= 1, "Category accuracy view must aggregate practice and exam answers.");
            Assert.True(catAcc.Correct >= 1);

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

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, NavigationMode, UnitCount)
            VALUES (@skill, @cert, 'reading', 'P7_CAT', N'Adaptive Reading', 1, 'UNIT_LOCKED_SANDBOX', 2);
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'CAT Sitting Exam', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, UnitNumber)
            VALUES (@exam, @cert, @sec, 1, 1), (@exam, @cert, @sec, 2, 2);
            DECLARE @g1 INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND UnitNumber = 1);
            DECLARE @g2 INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND UnitNumber = 2);

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@g1, @exam, @sec, @skill, N'Stage 1 Q', 1, 'mcq', 1),
                   (@g2, @exam, @sec, @skill, N'Stage 2 Adaptive Q', 2, 'mcq', 2);

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect)
            SELECT QuestionId, 'mcq', 'A', 'Correct', 1 FROM Questions WHERE ExamId = @exam;
            """);

        await using var dbCat = DbContextFactory.Create(database.ConnectionString);
        var cert = await dbCat.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await dbCat.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var qStage1 = await dbCat.Questions.SingleAsync(q => q.ExamId == exam.ExamId && q.DisplayOrder == 1);
        var qStage2 = await dbCat.Questions.SingleAsync(q => q.ExamId == exam.ExamId && q.DisplayOrder == 2);
        var optStage1 = await dbCat.QuestionOptions.FirstAsync(o => o.QuestionId == qStage1.QuestionId);
        var optStage2 = await dbCat.QuestionOptions.FirstAsync(o => o.QuestionId == qStage2.QuestionId);

        var userId = await SeedLearnerAsync(certCode);
        var engine = new AttemptEngine(dbCat);

        var attempt = await engine.CreateAttemptAsync(userId, cert.CertId, exam.ExamId, 60);
        Assert.Equal(1, attempt.ActiveUnitNumber);

        // Stage 1 is active, Stage 2 locked
        await engine.SaveAnswerAsync(attempt.AttemptId, userId, qStage1.QuestionId, optStage1.OptionId, false);
        await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, qStage2.QuestionId, optStage2.OptionId, false));

        // Transition to Stage 2 (Unit 2)
        var trans = await engine.TransitionUnitAsync(attempt.AttemptId, 2);
        Assert.Equal(2, trans.ActiveUnitNumber);

        // Stage 1 is permanently locked
        await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, qStage1.QuestionId, optStage1.OptionId, false));

        // Stage 2 is now open
        var res2 = await engine.SaveAnswerAsync(attempt.AttemptId, userId, qStage2.QuestionId, optStage2.OptionId, false);
        Assert.Equal(qStage2.QuestionId, res2.QuestionId);
    }

    [Fact]
    public void Preset_Score_Calibration_TOEIC_Speaking_And_Writing_Async()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var certService = new CertificateService(db);
        var presets = certService.GetOfficialPresets();

        // 1. Preset Blueprint Rules Check
        var toeicSw = presets.Single(p => p.PresetCode == "toeic_sw");
        Assert.Equal("independent", toeicSw.ScoreAggregationRule);
        Assert.Equal(2, toeicSw.Skills.Count);

        var spea = toeicSw.Skills.Single(s => s.Code == "SPEA");
        Assert.Equal(11, spea.Sections.Sum(s => s.ExpectedQuestionCount ?? 0));

        var writ = toeicSw.Skills.Single(s => s.Code == "WRIT");
        var w1 = writ.Sections.Single(s => s.Code == "W1");
        Assert.Equal(NavigationModeDto.HybridGroupLocked, w1.NavigationMode);
        Assert.Equal(2, w1.RequiredKeywordCount);

        // 2. Independent skill scoring rule: composite total score is 0 / independent
        var skillScores = new List<AttemptSkillScore>
        {
            new() { ScaledScore = 160 }, // Speaking 0–200
            new() { ScaledScore = 170 }  // Writing 0–200
        };
        var total = AttemptEngine.AggregateTotalScore("independent", skillScores);
        Assert.Equal(0, total);
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

    [Fact]
    public void Eleven_Component_Evaluation_Nuances_Verification()
    {
        // 1. MCQ & MCQ_3
        var qMcq = new Question { QuestionType = "mcq" };
        var qMcq3 = new Question { QuestionType = "mcq_3" };
        var opts = new List<QuestionOption>
        {
            new() { OptionId = 1, Label = "A", Text = "Correct", IsCorrect = true },
            new() { OptionId = 2, Label = "B", Text = "Wrong", IsCorrect = false }
        };
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qMcq, opts, 1, null, null));
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qMcq3, opts, 1, null, null));
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qMcq, opts, 2, null, null));

        // 2. MultiSelect: Exact set matching
        var qMulti = new Question { QuestionType = "multi_select", MaxSelections = 2 };
        var multiOpts = new List<QuestionOption>
        {
            new() { OptionId = 10, Label = "A", Text = "Opt A", IsCorrect = true },
            new() { OptionId = 11, Label = "B", Text = "Opt B", IsCorrect = false },
            new() { OptionId = 12, Label = "C", Text = "Opt C", IsCorrect = true }
        };
        // Exact set matches
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qMulti, multiOpts, null, "[10, 12]", null));
        // Partial set fails
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qMulti, multiOpts, null, "[10]", null));
        // Superset with distractor fails
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qMulti, multiOpts, null, "[10, 11, 12]", null));

        // 3. InlineCompletion: MAX_WORDS zeroing
        var qInlineWords = new Question
        {
            QuestionType = "inline_completion",
            MaxWords = 2,
            AcceptedAnswersJson = "[\"rapid transit\",\"transit\"]",
            AllowNumber = true
        };
        // Within limit matches
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qInlineWords, [], null, null, "rapid transit"));
        // Exceeds limit fails even if substring matches
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qInlineWords, [], null, null, "rapid urban transit system"));

        // 4. InlineCompletion: AllowNumber false
        var qInlineNoNum = new Question
        {
            QuestionType = "inline_completion",
            AllowNumber = false,
            AcceptedAnswersJson = "[\"building ten\",\"building 10\"]"
        };
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qInlineNoNum, [], null, null, "building ten"));
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qInlineNoNum, [], null, null, "building 10"));

        // 5. Dropdown & DragDrop: option or responseText match
        var qDrop = new Question { QuestionType = "dropdown" };
        var qDrag = new Question { QuestionType = "drag_drop" };
        var dropOpts = new List<QuestionOption>
        {
            new() { OptionId = 20, Label = "A", Text = "Deployment Phase", IsCorrect = true }
        };
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qDrop, dropOpts, 20, null, null));
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qDrag, dropOpts, 20, null, null));
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qDrop, dropOpts, null, null, "Deployment Phase"));
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qDrag, dropOpts, null, null, "deployment phase"));

        // 6. FreeResponse: Lemma keyword stem verification & word count
        var qFree = new Question
        {
            QuestionType = "free_response",
            MinWords = 5,
            RequiredKeywords = "develop, environment"
        };
        // Matches "developing" via stem "develop" and "environmental" via stem "environment"
        var evalSuccess = AttemptEngine.EvaluateWritingResponse(qFree, "Our team is developing modern environmental solutions.");
        Assert.True(evalSuccess.HasRequiredKeywords);
        Assert.False(evalSuccess.IsUnderLength);

        // Missing keyword fails
        var evalMissing = AttemptEngine.EvaluateWritingResponse(qFree, "Our team is working on software solutions.");
        Assert.False(evalMissing.HasRequiredKeywords);
        Assert.Contains("develop", evalMissing.MissingKeywords);
        Assert.Contains("environment", evalMissing.MissingKeywords);

        // Underlength triggers penalty flag
        var evalShort = AttemptEngine.EvaluateWritingResponse(qFree, "Too short.");
        Assert.True(evalShort.IsUnderLength);
    }
}
