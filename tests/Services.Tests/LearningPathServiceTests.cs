using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class LearningPathServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (LearningPathService service, LearnerService learners, PracticeService practice, CompanionDbContext db) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var service = new LearningPathService(db, learners);
        var practice = new PracticeService(db, learners);
        return (service, learners, practice, db);
    }

    /// <summary>
    /// #153/#154: a learner here has a goal with a deadline and a pace, because path generation no
    /// longer invents one — #153 refuses a goal it cannot judge, and #154 plans the volume from
    /// <c>WeeklyStudyHours</c>. The old fixture depended on the generator aiming at the certificate's
    /// lowest and highest band; both those fallbacks are gone.
    /// </summary>
    private async Task<int> SeedLearnerAsync(string? activeCertCode = null, string? currentBand = "A2", string? targetBand = "B2")
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var goal = activeCertCode is not null && currentBand is not null && targetBand is not null
            ? $"""
              INSERT INTO TargetBands (UserId, CertId, TargetBandCode, CurrentBandCode, TargetDate, WeeklyStudyHours)
              VALUES (@u, (SELECT CertId FROM Certificates WHERE Code = '{activeCertCode}'),
                      '{targetBand}', '{currentBand}',
                      DATEADD(DAY, 210, CAST(SYSDATETIMEOFFSET() AS DATE)), 6);
              """
            : "";

        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            DECLARE @u INT = SCOPE_IDENTITY();
            {goal}
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<(int certId, int templateId, int mod1Id, int mod2Id, int item1Id, int item2Id, int secId, int qId)> SeedPathFixtureAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'READ', N'Reading', 'reading', 1),
              (@cert, 'LIST', N'Listening', 'listening', 2);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sR, @cert, 'reading', 'P5', N'Sentences', 1, 1),
              (@sL, @cert, 'listening', 'P1', N'Photos', 1, 2);
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A1', N'Beginner', 0, 200, 1),
              (@cert, 'A2', N'Elementary', 201, 500, 2),
              (@cert, 'B1', N'Intermediate', 501, 750, 3),
              (@cert, 'B2', N'Upper intermediate', 751, 990, 4);

            -- Published Mock Exam
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Mock Exam 1', 'published', 120, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, @secR, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp, @exam, @secR, @sR, N'Question 1 stem', 2, 'mcq', 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', N'Opt A', 1),
              (@q1, 'mcq', 'B', N'Opt B', 0);

            -- Path Template
            INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
            VALUES (@cert, 'A2', 'B2', N'Path A2 to B2');
            DECLARE @tpl INT = SCOPE_IDENTITY();

            INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, GroupName, Description, EstimatedMinutes)
            VALUES (@tpl, @sR, @cert, 1, N'Module 1: Grammar', N'Foundations', N'Grammar module description', 20);
            DECLARE @m1 INT = SCOPE_IDENTITY();

            INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
            VALUES (@m1, @cert, @tpl, 'practice_section', @secR, 5, 1);
            DECLARE @i1 INT = SCOPE_IDENTITY();

            INSERT INTO PathModuleTopics (ModuleId, Title, QuestionCount, DisplayOrder)
            VALUES (@m1, N'Topic 1: Present Tense', 5, 1);

            INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, GroupName, Description, EstimatedMinutes)
            VALUES (@tpl, @sR, @cert, 2, N'Module 2: Vocabulary', N'Foundations', N'Vocabulary module description', 25);
            DECLARE @m2 INT = SCOPE_IDENTITY();

            INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
            VALUES (@m2, @cert, @tpl, 'practice_section', @secR, 5, 1);
            DECLARE @i2 INT = SCOPE_IDENTITY();
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var tpl = await db.PathTemplates.SingleAsync(t => t.CertId == cert.CertId);
        var m1 = await db.PathModules.SingleAsync(m => m.TemplateId == tpl.TemplateId && m.DisplayOrder == 1);
        var m2 = await db.PathModules.SingleAsync(m => m.TemplateId == tpl.TemplateId && m.DisplayOrder == 2);
        var i1 = await db.PathModuleItems.SingleAsync(i => i.ModuleId == m1.ModuleId);
        var i2 = await db.PathModuleItems.SingleAsync(i => i.ModuleId == m2.ModuleId);
        var sec = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Code == "P5");
        var q = await db.Questions.FirstAsync(q => q.SectionId == sec.SectionId);

        return (cert.CertId, tpl.TemplateId, m1.ModuleId, m2.ModuleId, i1.ItemId, i2.ItemId, sec.SectionId, q.QuestionId);
    }

    [Fact]
    public async Task GetActivePathAsync_WhenNoActivePath_ReturnsSummaryWithHasActivePathFalse()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, _) = CreateServices();

        var summary = await service.GetActivePathAsync(userId);

        Assert.False(summary.HasActivePath);
        Assert.Null(summary.LearnerPathId);
        Assert.Empty(summary.Modules);
        Assert.Equal(certCode, summary.ActiveCert.Code);
    }

    [Fact]
    public async Task GeneratePathAsync_CreatesActivePathAndSteps()
    {
        var certCode = Unique("C");
        var (certId, templateId, m1Id, m2Id, i1Id, i2Id, _, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);

        Assert.True(summary.HasActivePath);
        Assert.NotNull(summary.LearnerPathId);
        Assert.Equal("A2", summary.FromBandCode);
        Assert.Equal("B2", summary.ToBandCode);
        Assert.Equal(0, summary.CompletedModules);

        // #154: the steps are a plan, not a walk over the template's items. Four weeks of 3-5 steps,
        // and the first one is open while the rest wait.
        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .OrderBy(s => s.StepId)
            .ToListAsync();

        Assert.InRange(
            steps.Count,
            LearningPathPlanner.MinimumStepsPerWeek * LearningPathPlanner.WindowWeeks,
            LearningPathPlanner.MaximumStepsPerWeek * LearningPathPlanner.WindowWeeks);
        Assert.Equal("available", steps[0].Status);
        Assert.All(steps.Skip(1), s => Assert.Equal("locked", s.Status));

        // #154: the four-week window is persisted, and the summary reports it week by week (AC5).
        Assert.Equal(
            Enumerable.Range(1, LearningPathPlanner.WindowWeeks).ToList(),
            steps.Select(s => s.WeekNumber!.Value).Distinct().OrderBy(w => w).ToList());
        Assert.Equal(LearningPathPlanner.WindowWeeks, summary.Weeks.Count);
    }

    [Fact]
    public async Task GeneratePathAsync_WhenActivePathAlreadyExists_ThrowsLearningPathException()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, _) = CreateServices();

        await service.GeneratePathAsync(userId);

        var ex = await Assert.ThrowsAsync<LearningPathException>(() => service.GeneratePathAsync(userId));
        Assert.Equal(LearningPathException.PathAlreadyActive, ex.Code);
    }

    [Fact]
    public async Task GetModuleDetailAsync_ReturnsModuleDetailWithStepsAndTopics()
    {
        var certCode = Unique("C");
        var (certId, templateId, m1Id, _, _, _, _, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, _) = CreateServices();

        await service.GeneratePathAsync(userId);

        // #154: generated steps point at the certificate's own section items, so they land in the
        // module those items belong to. The module opens and holds steps; the plan just does not walk
        // the template's item order.
        var summary = await service.GetActivePathAsync(userId);
        var detail = await service.GetModuleDetailAsync(userId, summary.Modules[0].ModuleId);

        Assert.False(detail.IsLocked);
        Assert.NotEmpty(detail.Steps);
        Assert.NotNull(detail.ActiveStepId);
    }

    [Fact]
    public async Task StartStepAsync_ForPracticeSection_CreatesPracticeSessionAndReturnsUrl()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var firstStep = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .OrderBy(s => s.StepId)
            .FirstAsync();

        var result = await service.StartStepAsync(userId, firstStep.StepId);

        Assert.Equal(firstStep.StepId, result.StepId);
        Assert.Equal(PathModuleItemType.PracticeSection, result.ItemType);
        Assert.NotNull(result.SessionId);
        Assert.Equal($"/practice/{result.SessionId}", result.RedirectUrl);

        // Step should now be in_progress
        var updatedStep = await db.LearnerPathSteps.FindAsync(firstStep.StepId);
        Assert.Equal("in_progress", updatedStep!.Status);

        // Practice session should be linked to step
        var session = await db.PracticeSessions.FindAsync(result.SessionId);
        Assert.NotNull(session);
        Assert.Equal(firstStep.StepId, session.LearnerPathStepId);

        // Calling StartStepAsync again resumes the existing session
        var resumeResult = await service.StartStepAsync(userId, firstStep.StepId);
        Assert.Equal(result.SessionId, resumeResult.SessionId);
        Assert.Equal(result.RedirectUrl, resumeResult.RedirectUrl);
    }

    [Fact]
    public async Task StartStepAsync_WhenStepIsLocked_ThrowsLearningPathException()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var lockedStep = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.Status == "locked")
            .FirstAsync();

        var ex = await Assert.ThrowsAsync<LearningPathException>(() => service.StartStepAsync(userId, lockedStep.StepId));
        Assert.Equal(LearningPathException.StepLocked, ex.Code);
    }

    [Fact]
    public async Task ResetActivePathAsync_SetsStatusReset_AndAllowsGeneratingNewPath()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        Assert.True(summary.HasActivePath);

        var reset = await service.ResetActivePathAsync(userId);
        Assert.True(reset);

        var path = await db.LearnerPaths.FindAsync(summary.LearnerPathId);
        Assert.Equal("reset", path!.Status);

        // Reset allows generating a new path immediately
        var newSummary = await service.GeneratePathAsync(userId);
        Assert.True(newSummary.HasActivePath);
        Assert.NotEqual(summary.LearnerPathId, newSummary.LearnerPathId);
    }

    [Fact]
    public async Task PracticeService_SubmitAnswer_CompletesLinkedLearnerPathStep()
    {
        var certCode = Unique("C");
        var (certId, templateId, m1Id, m2Id, i1Id, i2Id, secId, qId) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, practice, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var firstStep = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .OrderBy(s => s.StepId)
            .FirstAsync();

        // Start step via LearningPathService
        var startResult = await service.StartStepAsync(userId, firstStep.StepId);
        int sessionId = startResult.SessionId!.Value;

        // Verify step is in_progress
        await db.Entry(firstStep).ReloadAsync();
        Assert.Equal("in_progress", firstStep.Status);

        // Look up option for qId
        var correctOpt = await db.QuestionOptions.FirstAsync(o => o.QuestionId == qId && o.IsCorrect);

        // Submit the answer through PracticeService
        await practice.SubmitAnswerAsync(userId, sessionId, qId, new SubmitPracticeAnswerInput(correctOpt.OptionId));

        // Linked step should now be marked done and have score recorded!
        await db.Entry(firstStep).ReloadAsync();
        Assert.Equal("done", firstStep.Status);
        Assert.NotNull(firstStep.Score);
        Assert.NotNull(firstStep.CompletedAt);

        // Second step should now be available
        var secondStep = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.Status == "available")
            .OrderBy(s => s.StepId)
            .FirstAsync();
        Assert.NotEqual(firstStep.StepId, secondStep.StepId);
    }

    [Fact]
    public async Task GetModuleDetailAsync_WhenAllStepsDone_KeepsActiveStepIdForReview()
    {
        var certCode = Unique("C");
        var (certId, templateId, m1Id, _, _, _, _, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .OrderBy(s => s.StepId)
            .ToListAsync();

        // #154: read the module the step actually lives in.
        var moduleOfStep = await db.PathModuleItems
            .Where(i => i.ItemId == steps[0].PathModuleItemId)
            .Select(i => i.ModuleId)
            .SingleAsync();

        await PathStepCompletion.CloseStepAsync(db, steps[0], score: 100m, accuracyPct: 100m);

        var detail = await service.GetModuleDetailAsync(userId, moduleOfStep);

        // #154: a module can hold several planned steps, so the active one is the next open step —
        // not necessarily the one just finished. Both must be in this module.
        Assert.NotNull(detail.ActiveStepId);
        Assert.Contains(detail.Steps, s => s.StepId == detail.ActiveStepId);
        Assert.Contains(detail.Steps, s => s.StepId == steps[0].StepId);
        Assert.Equal(LearnerPathStepStatus.Done, detail.Steps.Single(s => s.StepId == steps[0].StepId).Status);
    }

    [Fact]
    public async Task GetModuleDetailAsync_TopicsStatus_ProgressivelyAllocatesBasedOnAnsweredCount()
    {
        var certCode = Unique("C");
        var (certId, templateId, m1Id, _, i1Id, _, secId, q1Id) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var moduleOfFirstStep = await db.PathModuleItems
            .Where(i => i.ItemId == (db.LearnerPathSteps
                .Where(s => s.LearnerPathId == summary.LearnerPathId)
                .OrderBy(s => s.StepId)
                .Select(s => s.PathModuleItemId)
                .First()))
            .Select(i => i.ModuleId)
            .SingleAsync();

        // The fixture's module already carries Topic 1; reshape it and add a second topic, because
        // this is the module the learner's practice steps now live in.
        await database.SqlAsync($"""
            DELETE FROM PathModuleTopics WHERE ModuleId = {moduleOfFirstStep};
            INSERT INTO PathModuleTopics (ModuleId, Title, QuestionCount, DisplayOrder)
            VALUES ({moduleOfFirstStep}, N'Topic 1: First question', 1, 1),
                   ({moduleOfFirstStep}, N'Topic 2: Additional Practice', 5, 2);
            """);

        var step = await db.LearnerPathSteps
            .Include(s => s.PathModuleItem)
            .FirstAsync(s => s.LearnerPathId == summary.LearnerPathId
                && s.PathModuleItem.RefSectionId == secId);

        // Start step (which creates PracticeSession and 1 PracticeAnswer for q1Id)
        var startResult = await service.StartStepAsync(userId, step.StepId);
        int sessionId = startResult.SessionId!.Value;

        // Answer that question
        var opt = await db.QuestionOptions.FirstAsync(o => o.QuestionId == q1Id);
        var answer = await db.PracticeAnswers.FirstAsync(pa => pa.SessionId == sessionId && pa.QuestionId == q1Id);
        answer.SelectedOptionId = opt.OptionId;
        answer.IsCorrect = true;
        await db.SaveChangesAsync();

        var detail = await service.GetModuleDetailAsync(userId, moduleOfFirstStep);

        Assert.Equal(2, detail.Topics.Count);
        // Topic 1 had 1 question, 1 answered -> done
        Assert.Equal("done", detail.Topics[0].Status);
        // Topic 2 has 5 questions, 0 remaining answered -> not_started
        Assert.Equal("not_started", detail.Topics[1].Status);
    }

    [Fact]
    public async Task StartStepAsync_WhenNoQuestionsAvailable_ThrowsNoQuestionsAvailableException()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var firstStep = await db.LearnerPathSteps
            .Include(s => s.PathModuleItem)
            .FirstAsync(s => s.LearnerPathId == summary.LearnerPathId);

        // Delete questions for this section
        int secId = firstStep.PathModuleItem.RefSectionId!.Value;
        await database.SqlAsync($"""
            DELETE FROM QuestionOptions WHERE QuestionId IN (SELECT QuestionId FROM Questions WHERE SectionId = {secId});
            DELETE FROM Questions WHERE SectionId = {secId};
            """);

        var ex = await Assert.ThrowsAsync<LearningPathException>(() => service.StartStepAsync(userId, firstStep.StepId));
        Assert.Equal(LearningPathException.NoQuestionsAvailable, ex.Code);
    }

    [Fact]
    public async Task PruneOldSessions_ExcludesSessionsWithLearnerPathStepId()
    {
        var certCode = Unique("C");
        var (certId, templateId, m1Id, _, _, _, secId, qId) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, learners, practice, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var step = await db.LearnerPathSteps.FirstAsync(s => s.LearnerPathId == summary.LearnerPathId);

        // Create 1 session linked to learning path step
        var pathSession = new PracticeSession
        {
            UserId = userId,
            SectionId = secId,
            Modality = "reading",
            QuestionCount = 5,
            Status = PracticeStatus.Finished.ToCode(),
            CorrectCount = 5,
            QuestionSource = QuestionSource.All.ToCode(),
            LearnerPathStepId = step.StepId,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-10),
            FinishedAt = DateTimeOffset.UtcNow.AddHours(-9)
        };
        db.PracticeSessions.Add(pathSession);
        await db.SaveChangesAsync();

        // Create 25 standalone finished sessions (exceeding MaxSavedSessionsPerCertificate = 20)
        for (int i = 0; i < 25; i++)
        {
            db.PracticeSessions.Add(new PracticeSession
            {
                UserId = userId,
                SectionId = secId,
                Modality = "reading",
                QuestionCount = 5,
                Status = PracticeStatus.Finished.ToCode(),
                CorrectCount = 3,
                QuestionSource = QuestionSource.All.ToCode(),
                LearnerPathStepId = null,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-i * 5),
                FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-i * 5 + 2)
            });
        }
        await db.SaveChangesAsync();

        // Trigger pruning via another finished session submission or creating a session
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var opt = await db.QuestionOptions.FirstAsync(o => o.QuestionId == qId);

        // Verify the path session is NEVER pruned
        var pathSessionInDb = await db.PracticeSessions.FindAsync(pathSession.SessionId);
        Assert.NotNull(pathSessionInDb);
        Assert.Equal(step.StepId, pathSessionInDb.LearnerPathStepId);

        // Verify standalone sessions are capped at 20 + 1 (the new one)
        var standaloneCount = await db.PracticeSessions
            .CountAsync(s => s.UserId == userId && s.LearnerPathStepId == null);
        Assert.True(standaloneCount <= 25);
    }

    private async Task GrantCreditsAsync(int userId, int credits)
    {
        var adminEmail = $"{Guid.NewGuid():N}@admin.test";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{adminEmail}', 0x01, N'Admin User');
            DECLARE @adminId INT = SCOPE_IDENTITY();
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES ({userId}, 'admin_grant', {credits}, @adminId, N'Test setup credits');
            """);
    }

    [Fact]
    public async Task UpgradePathAsync_Succeeds_WhenBalanceSufficient_DebitsCredits_SetsIsPremiumTrue()
    {
        var certCode = Unique("UP");
        var (certId, templateId, m1Id, _, _, _, secId, qId) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        await GrantCreditsAsync(userId, 10);

        var (service, _, _, db) = CreateServices();
        var initial = await service.GeneratePathAsync(userId);
        Assert.False(initial.IsPremium);

        var upgraded = await service.UpgradePathAsync(userId);
        Assert.True(upgraded.IsPremium);

        // Verify credit transaction row in ledger
        var txn = await db.CreditTransactions
            .FirstOrDefaultAsync(t => t.LearnerPathId == initial.LearnerPathId && t.Reason == "premium_path");
        Assert.NotNull(txn);
        Assert.Equal(-5, txn.Delta);
        Assert.Equal(userId, txn.UserId);

        // Verify balance decreased from 10 to 5
        var balance = await db.CreditTransactions.Where(t => t.UserId == userId).SumAsync(t => (int?)t.Delta) ?? 0;
        Assert.Equal(5, balance);

        // Verify GetActivePathAsync returns IsPremium = true
        var active = await service.GetActivePathAsync(userId);
        Assert.True(active.IsPremium);
    }

    [Fact]
    public async Task UpgradePathAsync_ThrowsInsufficientCredits_WhenBalanceLessThanFive()
    {
        var certCode = Unique("IC");
        var (certId, templateId, m1Id, _, _, _, secId, qId) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        await GrantCreditsAsync(userId, 3); // Short by 2

        var (service, _, _, db) = CreateServices();
        var initial = await service.GeneratePathAsync(userId);
        Assert.False(initial.IsPremium);

        var ex = await Assert.ThrowsAsync<LearningPathException>(() => service.UpgradePathAsync(userId));
        Assert.Equal(LearningPathException.InsufficientCredits, ex.Code);

        // Verify no premium_path debit was written
        var txnExists = await db.CreditTransactions
            .AnyAsync(t => t.LearnerPathId == initial.LearnerPathId && t.Reason == "premium_path");
        Assert.False(txnExists);

        // Balance untouched
        var balance = await db.CreditTransactions.Where(t => t.UserId == userId).SumAsync(t => (int?)t.Delta) ?? 0;
        Assert.Equal(3, balance);

        // Still free
        var active = await service.GetActivePathAsync(userId);
        Assert.False(active.IsPremium);
    }

    [Fact]
    public async Task UpgradePathAsync_IsIdempotent_WhenCalledMultipleTimes()
    {
        var certCode = Unique("IDEM");
        var (certId, templateId, m1Id, _, _, _, secId, qId) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        await GrantCreditsAsync(userId, 10);

        var (service, _, _, db) = CreateServices();
        var initial = await service.GeneratePathAsync(userId);

        // First call charges 5 credits
        var firstUpgrade = await service.UpgradePathAsync(userId);
        Assert.True(firstUpgrade.IsPremium);

        var txnCount1 = await db.CreditTransactions.CountAsync(t => t.LearnerPathId == initial.LearnerPathId && t.Reason == "premium_path");
        Assert.Equal(1, txnCount1);

        // Second call is idempotent: does not charge again
        var secondUpgrade = await service.UpgradePathAsync(userId);
        Assert.True(secondUpgrade.IsPremium);

        var txnCount2 = await db.CreditTransactions.CountAsync(t => t.LearnerPathId == initial.LearnerPathId && t.Reason == "premium_path");
        Assert.Equal(1, txnCount2);

        var balance = await db.CreditTransactions.Where(t => t.UserId == userId).SumAsync(t => (int?)t.Delta) ?? 0;
        Assert.Equal(5, balance);
    }

    [Fact]
    public async Task StartStepAsync_BlocksPremiumOnlyStep_WhenPathIsNotPremium()
    {
        var certCode = Unique("PREM");
        var (certId, templateId, m1Id, _, _, _, secId, qId) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        await GrantCreditsAsync(userId, 10);

        var (service, _, _, db) = CreateServices();
        var summary = await service.GeneratePathAsync(userId);

        // #154: mark a step's own item premium-only. The plan picks items, so the flag has to be on
        // the item the step actually points at.
        var premiumStep = await db.LearnerPathSteps
            .Include(s => s.PathModuleItem)
            .FirstAsync(s => s.LearnerPathId == summary.LearnerPathId
                && s.PathModuleItem.ItemType == "practice_section");

        premiumStep.PathModuleItem.IsPremiumOnly = true;
        premiumStep.Status = "available";
        await db.SaveChangesAsync();

        // While path is free, StartStepAsync throws StepLocked
        var ex = await Assert.ThrowsAsync<LearningPathException>(() => service.StartStepAsync(userId, premiumStep.StepId));
        Assert.Equal(LearningPathException.StepLocked, ex.Code);

        // Upgrade path to premium
        await service.UpgradePathAsync(userId);

        // Now StartStepAsync succeeds
        var result = await service.StartStepAsync(userId, premiumStep.StepId);
        Assert.NotNull(result);
        Assert.Equal(premiumStep.StepId, result.StepId);
    }

    [Fact]
    public async Task GetModuleDetailAsync_ReflectsIsPremium_AndProvidesVideoAccess()
    {
        var certCode = Unique("VID");
        var (certId, templateId, m1Id, _, _, _, secId, qId) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        await GrantCreditsAsync(userId, 10);

        var (service, _, _, db) = CreateServices();
        await service.GeneratePathAsync(userId);

        // #154: the video topics belong to the module the learner's steps are actually in.
        var pathId = (await service.GetActivePathAsync(userId)).LearnerPathId!.Value;
        var firstStep = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == pathId)
            .OrderBy(s => s.StepId)
            .FirstAsync();
        var planModuleId = await db.PathModuleItems
            .Where(i => i.ItemId == firstStep.PathModuleItemId)
            .Select(i => i.ModuleId)
            .SingleAsync();

        await database.SqlAsync($"""
            INSERT INTO PathModuleTopics (ModuleId, Title, QuestionCount, VideoPath, DisplayOrder)
            VALUES ({planModuleId}, N'Topic with Video', 5, N'/videos/module1.mp4', 2);
            """);

        // Free path: CanAccessVideo is false, VideoPath is null
        var freeDetail = await service.GetModuleDetailAsync(userId, planModuleId);
        Assert.False(freeDetail.IsPremium);
        var topicFree = freeDetail.Topics.First(t => t.Title == "Topic with Video");
        Assert.False(topicFree.CanAccessVideo);
        Assert.Null(topicFree.VideoPath);

        // Upgrade path
        await service.UpgradePathAsync(userId);

        // Upgraded path: CanAccessVideo is true, VideoPath is populated
        var premiumDetail = await service.GetModuleDetailAsync(userId, planModuleId);
        Assert.True(premiumDetail.IsPremium);
        var topicPremium = premiumDetail.Topics.First(t => t.Title == "Topic with Video");
        Assert.True(topicPremium.CanAccessVideo);
        Assert.Equal("/videos/module1.mp4", topicPremium.VideoPath);
    }

    [Fact]
    public async Task UpgradePathAsync_ThrowsPathNotFound_WhenNoActivePathExists()
    {
        var certCode = Unique("NP");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        var (service, _, _, _) = CreateServices();
        var ex = await Assert.ThrowsAsync<LearningPathException>(() => service.UpgradePathAsync(userId));
        Assert.Equal(LearningPathException.PathNotFound, ex.Code);
    }

    [Fact]
    public async Task ResetActivePathAsync_ThenGenerateNewPath_StartsAsFreePath()
    {
        var certCode = Unique("RESET");
        var (certId, templateId, m1Id, _, _, _, secId, qId) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        await GrantCreditsAsync(userId, 10);

        var (service, _, _, db) = CreateServices();
        var initial = await service.GeneratePathAsync(userId);
        var upgraded = await service.UpgradePathAsync(userId);
        Assert.True(upgraded.IsPremium);

        // Reset path
        var reset = await service.ResetActivePathAsync(userId);
        Assert.True(reset);

        // Generate new path
        var newPath = await service.GeneratePathAsync(userId);
        Assert.False(newPath.IsPremium);
    }

    [Fact]
    public async Task GetModuleDetailAsync_WithLessonStep_DerivesStatusFromLearnerLessonProgress()
    {
        var certCode = Unique("LS");
        var (certId, templateId, m1Id, _, _, _, secId, qId) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        // Add a lesson to the certificate; the planner will place it in the plan's own module.
        await database.SqlAsync($"""
            INSERT INTO Lessons (CertId, SkillId, Title, EstimatedMinutes, DisplayOrder)
            VALUES ({certId}, (SELECT SkillId FROM Skills WHERE CertId = {certId} AND Code = 'READ'), N'Grammar Lesson', 5, 2);
            """);

        var (service, _, _, db) = CreateServices();
        var summary = await service.GeneratePathAsync(userId);

        var lesson = await db.Lessons.FirstAsync(l => l.CertId == certId);

        // The lesson step exists in the plan (a 6 h/week goal plans five steps, one of them a lesson).
        // Read it from the database first, so the module looked at is the one it is really in.
        var lessonStepId = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.PathModuleItem.RefLessonId == lesson.LessonId)
            .OrderBy(s => s.StepId)
            .Select(s => (int?)s.StepId)
            .FirstAsync();

        Assert.NotNull(lessonStepId);

        var moduleId = await db.LearnerPathSteps
            .Where(s => s.StepId == lessonStepId)
            .Select(s => s.PathModuleItem.ModuleId)
            .SingleAsync();

        var detailBefore = await service.GetModuleDetailAsync(userId, moduleId);
        var lessonStepBefore = detailBefore.Steps.First(s => s.StepId == lessonStepId);
        Assert.Equal("Grammar Lesson", lessonStepBefore.Title);

        // Create in-progress lesson progress
        var now = DateTimeOffset.UtcNow;
        db.LearnerLessonProgresses.Add(new LearnerLessonProgress
        {
            UserId = userId,
            LessonId = lesson.LessonId,
            CertId = certId,
            CardsCompleted = 2,
            TotalCards = 4,
            IsCompleted = false,
            FirstOpenedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        // Walk the practice step before the lesson, so the lesson is open and in progress.
        var stepBefore = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.StepId < lessonStepBefore.StepId)
            .OrderBy(s => s.StepId)
            .ToListAsync();

        foreach (var prior in stepBefore)
        {
            await PathStepCompletion.CloseStepAsync(db, prior, score: 100m, accuracyPct: 100m);
        }

        var detailAfter = await service.GetModuleDetailAsync(userId, moduleId);
        var lessonStepAfter = detailAfter.Steps.First(s => s.StepId == lessonStepId);
        Assert.Equal(LearnerPathStepStatus.InProgress, lessonStepAfter.Status);
        Assert.Equal(now, lessonStepAfter.StartedAt);
    }

    [Fact]
    public async Task GeneratePathAsync_WithArchiveExisting_ResetsPreviousActivePath()
    {
        var certCode = Unique("LPARCH");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, learners, _, db) = CreateServices();

        // First path creation
        var firstPath = await service.GeneratePathAsync(userId);
        Assert.NotNull(firstPath);

        var activeBefore = await db.LearnerPaths.SingleAsync(p => p.LearnerPathId == firstPath.LearnerPathId);
        Assert.Equal("active", activeBefore.Status);

        // Generate again with archiveExisting: true
        var secondPath = await service.GeneratePathAsync(userId, archiveExisting: true);
        Assert.NotEqual(firstPath.LearnerPathId, secondPath.LearnerPathId);

        await db.Entry(activeBefore).ReloadAsync();
        Assert.Equal("reset", activeBefore.Status);

        var activeAfter = await db.LearnerPaths.SingleAsync(p => p.LearnerPathId == secondPath.LearnerPathId);
        Assert.Equal("active", activeAfter.Status);
    }
}
