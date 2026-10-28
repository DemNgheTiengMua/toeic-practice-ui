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

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert, 'L_' + CAST(@cert AS VARCHAR(10)), N'Listening', 'listening', 1, 45),
              (@cert, 'R_' + CAST(@cert AS VARCHAR(10)), N'Reading', 'reading', 2, 75);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Modality = 'listening');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Modality = 'reading');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@sL, @cert, 'listening', 'SEC_L_' + CAST(@cert AS VARCHAR(10)), N'Listening Part 1', 1),
              (@sR, @cert, 'reading', 'SEC_R_' + CAST(@cert AS VARCHAR(10)), N'Reading Part 5', 2);
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

            -- Group & Question for Listening
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secL, 1);
            DECLARE @grpL INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grpL, @exam, @secL, @sL, N'Listening Question 1', 1, 'mcq', 1);
            DECLARE @qL INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@qL, 'mcq', 'A', N'Listening Option A (Correct)', 1),
              (@qL, 'mcq', 'B', N'Listening Option B (Wrong)', 0);

            -- Group & Question for Reading
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secR, 2);
            DECLARE @grpR INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grpR, @exam, @secR, @sR, N'Reading Question 1', 1, 'mcq', 1);
            DECLARE @qR INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@qR, 'mcq', 'A', N'Reading Option A (Wrong)', 0),
              (@qR, 'mcq', 'B', N'Reading Option B (Correct)', 1);
            """);

        var learnerId = await SeedLearnerWithCreditsAsync(10, certCode);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).ToListAsync();
        var qListening = questions.Single(q => q.Stem.Contains("Listening"));
        var qReading = questions.Single(q => q.Stem.Contains("Reading"));

        var optLCorrect = await db.QuestionOptions.Where(o => o.QuestionId == qListening.QuestionId && o.IsCorrect).Select(o => o.OptionId).SingleAsync();
        var optRCorrect = await db.QuestionOptions.Where(o => o.QuestionId == qReading.QuestionId && o.IsCorrect).Select(o => o.OptionId).SingleAsync();

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
            new(qListening.QuestionId, optLCorrect, false)
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
            new(qReading.QuestionId, optRCorrect, false)
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

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'R_' + @certStr, N'Reading', 'reading', 1);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@sR, @cert, 'reading', 'SR_' + @certStr, N'Reading Part 5', 1);
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

        // 2. Add question group
        var group = await adminService.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secR.SectionId));
        Assert.NotNull(group);

        // 3. Add question with valid MCQ options
        var question = await adminService.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "What is the correct answer?",
            3,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Correct Option", true, null),
                new AdminQuestionOptionInput(null, "B", "Distractor Option", false, null)
            ]));
        Assert.NotNull(question);
        Assert.Equal(2, question.Options.Count);

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
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            DECLARE @certStr VARCHAR(10) = CAST(@cert AS VARCHAR(10));

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert, 'R_' + @certStr, N'Reading', 'reading', 1, 30);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@sR, @cert, 'reading', 'SR_' + @certStr, N'Reading Diagnostic', 1);
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

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secR, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp, @exam, @secR, @sR, N'Placement Diagnostic Question 1', 2, 'mcq', 1);
            DECLARE @q INT = SCOPE_IDENTITY();

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q, 'mcq', 'A', N'Placement Answer A', 1),
              (@q, 'mcq', 'B', N'Placement Answer B', 0);

            -- Guided Learning Path Template
            INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
            VALUES (@cert, 'A2', 'B1', N'Diagnostic Guided Path A2 to B1');
            DECLARE @tpl INT = SCOPE_IDENTITY();

            INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, GroupName, Description, EstimatedMinutes)
            VALUES (@tpl, @sR, @cert, 1, N'Module 1: Remediation', N'Core', N'Core Remediation', 30);
            DECLARE @m1 INT = SCOPE_IDENTITY();

            INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
            VALUES (@m1, @cert, @tpl, 'practice_section', @secR, 5, 1);
            """);

        var learnerId = await SeedLearnerWithCreditsAsync(0, certCode);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId && e.Purpose == "placement");
        var question = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId);
        var correctOption = await db.QuestionOptions.Where(o => o.QuestionId == question.QuestionId && o.IsCorrect).Select(o => o.OptionId).SingleAsync();

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
            question.QuestionId,
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
        Assert.NotEmpty(pathSummary.Modules);

        // 6. Retrieve active module details and start the first step
        var moduleId = pathSummary.Modules[0].ModuleId;
        var moduleDetail = await pathService.GetModuleDetailAsync(learnerId, moduleId);
        Assert.NotEmpty(moduleDetail.Steps);
        var stepId = moduleDetail.Steps[0].StepId;

        var startStepResult = await pathService.StartStepAsync(learnerId, stepId);
        Assert.NotNull(startStepResult);
        Assert.NotNull(startStepResult.SessionId);

        // 7. Complete the step with a passing score
        var completed = await pathService.CompleteStepAsync(learnerId, stepId, score: 90.0m);
        Assert.True(completed);

        // 8. Verify the step's practice session has LearnerPathStepId set and is protected from pruning
        await using var dbVerify = DbContextFactory.Create(database.ConnectionString);
        var step = await dbVerify.LearnerPathSteps.SingleAsync(s => s.StepId == stepId);
        Assert.Equal("done", step.Status);

        var stepSession = await dbVerify.PracticeSessions.FirstOrDefaultAsync(ps => ps.LearnerPathStepId == stepId);
        Assert.NotNull(stepSession);
        Assert.Equal(stepId, stepSession.LearnerPathStepId);
    }
}
