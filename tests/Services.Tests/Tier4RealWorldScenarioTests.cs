using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services;
using Services.Credits;
using Xunit;

namespace Services.Tests;

/// <summary>
/// Tier 4: Real-World Scenarios Test Suite for Services.
/// Tests complete multi-step end-to-end user journeys and lifecycle workflows:
/// 1. Zero-Reload Exam Sitting Flow (ADR-0010): Start sitting -> receive manifest -> batch answer sync -> submission -> score scaling & CEFR band assignment.
/// 2. Admin Studio Exam Authoring & Publish Lifecycle: Create draft -> add groups & questions -> readiness check -> publish -> learner catalog visibility.
/// 3. Diagnostic Placement-to-Guided-Path Loop: Placement test -> band calculation -> target band selection -> learning path generation -> step execution & pruning protection.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class Tier4RealWorldScenarioTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private async Task<int> SeedLearnerWithCreditsAsync(int startingCredits, string? activeCertCode = null)
    {
        var email = $"{Guid.NewGuid():N}@tier4.test";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Tier4 Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        if (startingCredits > 0)
        {
            await database.SqlAsync($"""
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
                  INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@test.local', 0x01, N'Admin');
                DECLARE @adminId INT = SCOPE_IDENTITY();
                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
                VALUES ({userId}, 'admin_grant', {startingCredits}, @adminId, N'Tier4 Credits');
                """);
        }

        return userId;
    }

    [Fact]
    public async Task Scenario1_ZeroReload_SittingLifecycle_Complete_EndToEnd_Flow()
    {
        var certCode = Unique("SIT4");
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'L_' + CAST(@cert AS VARCHAR(10)), N'Listening', 'listening', 1, 45, 5, 495, 5),
              (@cert, 'R_' + CAST(@cert AS VARCHAR(10)), N'Reading', 'reading', 2, 75, 5, 495, 5);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Modality = 'listening');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Modality = 'reading');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sL, @cert, 'listening', 'SEC_L_' + CAST(@cert AS VARCHAR(10)), N'Listening Part 1', 1, 1),
              (@sR, @cert, 'reading', 'SEC_R_' + CAST(@cert AS VARCHAR(10)), N'Reading Part 5', 1, 2);
            DECLARE @secL INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Modality = 'listening');
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Modality = 'reading');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A1', N'Beginner', 0, 250, 1),
              (@cert, 'A2', N'Elementary', 255, 500, 2),
              (@cert, 'B1', N'Intermediate', 505, 750, 3),
              (@cert, 'B2', N'Upper Intermediate', 755, 990, 4);

            INSERT INTO ScoreScales (CertId, SkillId, RawScore, ScaledScore) VALUES
              (@cert, @sL, 0, 5),
              (@cert, @sL, 1, 245),
              (@cert, @sR, 0, 5),
              (@cert, @sR, 1, 250);

            INSERT INTO Exams (CertId, Purpose, Name, DurationMinutes, Status, IsDeleted)
            VALUES (@cert, 'mock', N'Mock Sitting Exam', 120, 'published', 0);
            DECLARE @exam INT = SCOPE_IDENTITY();
            """);

        var learnerId = await SeedLearnerWithCreditsAsync(10, certCode);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var secL = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "listening");
        var secR = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "reading");

        // #269: the paper is the roster. A listening position must carry a stimulus - that is where a
        // pool paper keeps its audio - and the preflight gate is driven by audio existing. The reading
        // position is B2 rather than A2 so it stocks no cell another test counts exactly.
        var qListening = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, secL.SectionId, "listening", "Listening Question 1",
            displayOrder: 1, keyLabel: "A", level: "A2", passage: "Listen to the announcement.");
        var qReading = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, secR.SectionId, "reading", "Reading Question 1",
            displayOrder: 1, keyLabel: "B", level: "B2");

        // A pool MCQ is four options by construction (CNT-06), and the option ids are the sitting
        // projection's own - never recomputed here.
        var optLCorrect = await PoolFixture.OptionIdAsync(db, exam.ExamId, qListening.ExamItemId, "A");
        var optRCorrect = await PoolFixture.OptionIdAsync(db, exam.ExamId, qReading.ExamItemId, "B");

        var engine = new AttemptEngine(db);

        // 1. Create sitting attempt (ADR-0010: Zero-reload in-memory sitting manifest)
        var attempt = await engine.CreateAttemptAsync(learnerId, cert.CertId, exam.ExamId, 120);
        Assert.NotNull(attempt);
        Assert.Equal("in_progress", attempt.Status);

        // 2. Fetch sitting take manifest
        var takeView = await engine.GetAttemptTakeAsync(attempt.AttemptId, learnerId, null);
        Assert.NotNull(takeView);
        Assert.Equal(2, takeView.TotalQuestions);
        Assert.True(takeView.RemainingSeconds > 0);

        // 3. Phase 1: Answer Listening question in active skill
        var batchListening = new List<BatchAnswerItem>
        {
            new(qListening.ExamItemId, optLCorrect, false)
        };
        var batchResultL = await engine.SaveBatchAnswersAsync(attempt.AttemptId, learnerId, batchListening);
        Assert.Single(batchResultL);
        Assert.True(batchResultL[0].RemainingSeconds > 0);

        // Transition from Listening to Reading phase
        var transition = await engine.TransitionToNextSkillAsync(attempt.AttemptId, learnerId);
        Assert.NotNull(transition);

        // 4. Phase 2: Answer Reading question in active skill
        var batchReading = new List<BatchAnswerItem>
        {
            new(qReading.ExamItemId, optRCorrect, false)
        };
        var batchResultR = await engine.SaveBatchAnswersAsync(attempt.AttemptId, learnerId, batchReading);
        Assert.Single(batchResultR);
        Assert.True(batchResultR[0].RemainingSeconds > 0);

        // 5. Submit sitting attempt
        var submitResult = await engine.SubmitAttemptAsync(attempt.AttemptId, learnerId, isAutoSubmit: false);
        Assert.NotNull(submitResult);
        Assert.Equal("graded", submitResult.Attempt.Status);
        Assert.NotNull(submitResult.Attempt.SubmittedAt);

        // 5. Verify calibrated score and CEFR band assignment (245 + 250 = 495 -> A2 band: 255-500)
        Assert.Equal(495, submitResult.TotalScore);
        Assert.Equal("A2", submitResult.BandCode);

        // 6. Verify result view can be retrieved
        var resultView = await engine.GetAttemptResultAsync(attempt.AttemptId, learnerId);
        Assert.NotNull(resultView);
        Assert.Equal(495, resultView.TotalScore);
        Assert.Equal("A2", resultView.BandCode);
        Assert.Equal(2, resultView.SkillScores.Count);
    }

    [Fact]
    public async Task Scenario2_AdminStudio_ExamAuthoring_Ingest_And_Publish_Lifecycle()
    {
        var certCode = Unique("ADM4");
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            DECLARE @certStr VARCHAR(10) = CAST(@cert AS VARCHAR(10));

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'R_' + @certStr, N'Reading', 'reading', 1, 60);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sR, @cert, 'reading', 'SR_' + @certStr, N'Reading Part 5', 1, 1);

            -- The pool's topic vocabulary is a closed set of twenty that no certificate owns, so an
            -- authoring path needs one to exist; a fresh test database has none.
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var secR = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "reading");

        var adminService = new AdminExamService(db, TimeProvider.System);

        // 1. Create draft exam in admin studio
        var examTitle = $"Admin Studio Test {Guid.NewGuid():N}"[..25];
        var exam = await adminService.CreateExamAsync(new CreateAdminExamRequest(
            cert.CertId,
            examTitle,
            60,
            ExamPurposeDto.Mock));
        Assert.NotNull(exam);
        Assert.Equal(ExamStatusDto.Draft, exam.Status);

        // 2. Add a group (a stimulus plus its positions) on the pool. #269: this replaced
        // CreateQuestionGroupAsync/CreateQuestionAsync with CreateExamClusterAsync, which authors the
        // items, their versions and the bindings in one act.
        var group = await adminService.CreateExamClusterAsync(exam.ExamId, new CreateExamClusterRequest(
            secR.SectionId, QuestionCount: 1, CefrLevelCode: "B1"));
        Assert.NotNull(group);

        // 3. The position the cluster authored, with the four options CNT-06 fixes and the section
        // blueprint declares.
        var question = Assert.Single(group.Questions);
        Assert.Equal(4, question.Options.Count);
        Assert.Single(question.Options.Where(o => o.IsCorrect));

        // 3b. INV-3: an item created by authoring is 'draft' until review promotes it, and only an
        // active item is served. CNT-42 fixes the legal transitions, so the promotion is the two steps
        // the specification names - draft -> validated -> active - rather than a direct flip. The real
        // flow also records a reviewer's clearance (#205); this scenario's subject is the author-publish
        // lifecycle, so it walks the lifecycle and asserts the effect.
        var position = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == exam.ExamId)
            .Select(i => new { i.ItemId })
            .SingleAsync();
        await database.SqlAsync($"""
            UPDATE Items SET LifecycleStatus = 'validated' WHERE ItemId = {position.ItemId};
            UPDATE Items SET LifecycleStatus = 'active' WHERE ItemId = {position.ItemId};
            """);

        // 3c. INV-3's other half: an item must also have cleared a human audit. A hand-authored item is
        // unbatched, so the fixture records the clearance the review workbench would (#205's per-item
        // 'clear'), signed by an account that is neither the batch's uploader nor the item's author -
        // ADM-02 refuses a self-approval.
        var roleId = await db.Roles.Where(r => r.Code == "admin").Select(r => r.RoleId).FirstOrDefaultAsync();
        if (roleId == 0)
        {
            var role = new Role { Code = "admin", Name = "Admin" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            roleId = role.RoleId;
        }

        var uploader = new User
        {
            RoleId = roleId, Email = $"t4-up-{Guid.NewGuid():N}@test.local",
            PasswordHash = [0x01], FullName = "Tier4 uploader",
        };
        var reviewer = new User
        {
            RoleId = roleId, Email = $"t4-rv-{Guid.NewGuid():N}@test.local",
            PasswordHash = [0x01], FullName = "Tier4 reviewer",
        };
        db.Users.AddRange(uploader, reviewer);
        await db.SaveChangesAsync();

        var clearanceBatch = new ImportBatch
        {
            CertId = cert.CertId, SourceName = "tier4-authoring.xlsx",
            Status = ImportBatchStatus.Done.ToCode(), UploadedBy = uploader.UserId,
        };
        db.ImportBatches.Add(clearanceBatch);
        await db.SaveChangesAsync();

        db.ImportLogs.Add(new ImportLog
        {
            BatchId = clearanceBatch.BatchId, ItemId = position.ItemId,
            Level = "info", Message = "accepted by the tier4 scenario",
        });
        await db.SaveChangesAsync();

        // 4. Audit exam readiness
        var audit = await adminService.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.NotNull(audit);
        Assert.True(audit.CanPublish, string.Join("; ", audit.Issues.Select(i => i.Message)));

        // 5. Publish the exam
        var publishResult = await adminService.PublishExamAsync(exam.ExamId);
        Assert.NotNull(publishResult);
        Assert.True(publishResult.Success);

        // 6. Verify exam is queryable in the published catalog
        var publishedExams = await adminService.GetExamsAsync(null, "published", cert.CertId, 1, 10);
        Assert.Contains(publishedExams.Exams, e => e.ExamId == exam.ExamId);
    }

    [Fact]
    public async Task Scenario3_DiagnosticPlacement_To_GuidedLearningPath_Complete_Loop()
    {
        var certCode = Unique("PLC4");
        await database.SqlAsync($"""
            -- #232: the canonical twenty topics are the step coordinate's topic axis.
            INSERT INTO Topics (Modality, Code, Name)
            SELECT v.Modality, v.Code, v.Name
            FROM (VALUES
              ('listening','L-GIST',N'Global gist'), ('listening','L-DET',N'Detail'),
              ('listening','L-INF',N'Inference'), ('listening','L-ATT',N'Attitude'),
              ('listening','L-FUNC',N'Function'), ('listening','L-ORG',N'Organisation'),
              ('reading','R-GIST',N'Global thesis'), ('reading','R-DET',N'Local detail'),
              ('reading','R-INF',N'Bridging inference'), ('reading','R-ATT',N'Authorial stance'),
              ('reading','R-COH',N'Cohesion'), ('reading','R-GRAM',N'Grammar'),
              ('speaking','S-DESC',N'Description'), ('speaking','S-INT',N'Interaction'),
              ('speaking','S-ARG',N'Argument'), ('speaking','S-SYN',N'Synthesis'),
              ('writing','W-CORR',N'Correspondence'), ('writing','W-DATA',N'Data'),
              ('writing','W-ARG',N'Argument'), ('writing','W-DISC',N'Discourse')
            ) v(Modality, Code, Name)
            WHERE NOT EXISTS (SELECT 1 FROM Topics t WHERE t.Modality = v.Modality AND t.Code = v.Code);

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            DECLARE @certStr VARCHAR(10) = CAST(@cert AS VARCHAR(10));

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'R_' + @certStr, N'Reading', 'reading', 1, 30, 0, 100, 5);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sR, @cert, 'reading', 'SR_' + @certStr, N'Reading Diagnostic', 1, 1);
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert);

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A2', N'Elementary', 0, 50, 1),
              (@cert, 'B1', N'Intermediate', 51, 100, 2);

            INSERT INTO ScoreScales (CertId, SkillId, RawScore, ScaledScore) VALUES
              (@cert, @sR, 0, 30),
              (@cert, @sR, 1, 80);

            -- Diagnostic Placement Exam
            INSERT INTO Exams (CertId, Purpose, Name, DurationMinutes, Status, IsDeleted)
            VALUES (@cert, 'placement', N'Placement Test 1', 30, 'published', 0);
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- #232: the plan is synthesised from the skill's own level, so the fixture supplies the
            -- coordinate's inputs - a cut-score row and drillable stock - rather than a template.

            -- Stock is at B1 because the placement below reports B1, and it is stocked in R-COH rather
            -- than R-DET because `PoolIngestionTests` counts that cell exactly and the pool is shared by
            -- the whole assembly.
            DECLARE @topicId INT = (SELECT TOP 1 TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-COH');
            DECLARE @n INT = 1;
            WHILE @n <= 4
            BEGIN
              INSERT INTO Stimuli (CefrLevel, Passage) VALUES ('B1', N'Passage');
              INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus)
              VALUES (SCOPE_IDENTITY(), 'reading', @topicId, 'B1', 'passage_comprehension', 0, 'demo', 'active');
              DECLARE @itemId INT = SCOPE_IDENTITY();
              INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
              VALUES (@itemId, 1, N'Stem', N'A', N'B', N'C', N'D', 'A');
              SET @n = @n + 1;
            END
            """);

        var learnerId = await SeedLearnerWithCreditsAsync(5, certCode);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId && e.Purpose == "placement");
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);

        // #269: the diagnostic paper is a roster too. Its position's level is B1, which is what the
        // placement below must report.
        var position = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading", "Placement Diagnostic Question 1",
            displayOrder: 1, keyLabel: "A", level: "B1");
        var correctOption = await PoolFixture.OptionIdAsync(db, exam.ExamId, position.ExamItemId, "A");

        var learners = new LearnerService(db);
        var placementService = new PlacementService(db, learners);
        var pathService = new LearningPathService(db, learners);

        // 1. Learner starts diagnostic placement test (0 credits charged - free diagnostic loop)
        var placementInfo = await placementService.StartPlacementTestAsync(learnerId, exam.ExamId);
        Assert.NotNull(placementInfo);

        // 2. Save diagnostic answer
        var saveResult = await placementService.SaveAnswerAsync(
            learnerId,
            placementInfo.PlacementId,
            position.ExamItemId,
            correctOption,
            isFlagged: false);
        Assert.NotNull(saveResult);

        // 3. Submit placement test
        var placementResult = await placementService.SubmitPlacementTestAsync(
            learnerId,
            placementInfo.PlacementId,
            isAutoSubmit: false);
        Assert.NotNull(placementResult);
        Assert.Equal("B1", placementResult.ResultBandCode);

        // 4. Set target band
        var targetBandResult = await placementService.SetTargetBandAsync(learnerId, "B1");
        Assert.Equal("B1", targetBandResult.TargetBandCode);

        // 5. Generate guided learning path
        var pathSummary = await pathService.GeneratePathAsync(learnerId, "B1");
        Assert.NotNull(pathSummary);
        Assert.True(pathSummary.HasActivePath);
        Assert.NotEmpty(pathSummary.OpenSteps);

        // 6. Retrieve the first step by its own id and start it. #232: a step is a coordinate, so
        // there is no module to walk into.
        var stepId = pathSummary.OpenSteps[0].StepId;
        await database.SqlAsync($"UPDATE LearnerPathSteps SET Status = 'available' WHERE StepId = {stepId};");

        var startStepResult = await pathService.StartStepAsync(learnerId, stepId);
        Assert.NotNull(startStepResult);
        Assert.Equal(StepRoute.ItemSelector, startStepResult.Route);
        Assert.NotNull(startStepResult.SessionId);

        // 7. Complete the step: #160 removed the manual "mark done" route with CompleteStepAsync, so
        // the step is closed by the one closer the three activity writers use.
        await PathStepCompletion.CloseStepAsync(db, await db.LearnerPathSteps.SingleAsync(s => s.StepId == stepId), score: 90m, accuracyPct: 90m);

        // 8. Verify the step's practice session has LearnerPathStepId set and is protected from pruning
        await using var dbVerify = DbContextFactory.Create(database.ConnectionString);
        var step = await dbVerify.LearnerPathSteps.SingleAsync(s => s.StepId == stepId);
        Assert.Equal("done", step.Status);

        var stepSession = await dbVerify.PracticeSessions.FirstOrDefaultAsync(ps => ps.LearnerPathStepId == stepId);
        Assert.NotNull(stepSession);
        Assert.Equal(stepId, stepSession.LearnerPathStepId);
    }
}
