using System;
using System.IO;
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
public sealed class AttemptEngineTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private AttemptEngine CreateEngine()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new AttemptEngine(db);
    }

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<(int certId, int examId, int q1Id, int q2Id, int opt1CorrectId, int opt2CorrectId)> SeedExamFixtureAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'READ', N'Reading', 'reading', 2);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@sL, @cert, 'listening', 'P1', N'Photos', 1),
              (@sR, @cert, 'reading', 'P5', N'Sentences', 2);
            DECLARE @secL INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P1');
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1),
              (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 120, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- Exam-specific Scale:
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @sL, @exam, 0, 100),
              (@cert, @sL, @exam, 1, 450),
              (@cert, @sR, @exam, 0, 100),
              (@cert, @sR, @exam, 1, 450);

            -- Question 1: Listening
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secL, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp1, @exam, @secL, @sL, N'Listening Q1', 1, 'mcq', 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', N'Opt A', 1),
              (@q1, 'mcq', 'B', N'Opt B', 0);

            -- Question 2: Reading
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secR, 2);
            DECLARE @grp2 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp2, @exam, @secR, @sR, N'Reading Q2', 1, 'mcq', 2);
            DECLARE @q2 INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q2, 'mcq', 'A', N'Opt A', 0),
              (@q2, 'mcq', 'B', N'Opt B', 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();
        var q1 = questions[0];
        var q2 = questions[1];

        var opt1 = await db.QuestionOptions.SingleAsync(o => o.QuestionId == q1.QuestionId && o.IsCorrect);
        var opt2 = await db.QuestionOptions.SingleAsync(o => o.QuestionId == q2.QuestionId && o.IsCorrect);

        return (cert.CertId, exam.ExamId, q1.QuestionId, q2.QuestionId, opt1.OptionId, opt2.OptionId);
    }

    private async Task<int> AddSpeakingQuestionAsync(int certId, int examId)
    {
        await database.SqlAsync($"""
            DECLARE @cert INT = {certId};
            DECLARE @exam INT = {examId};
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'SPEA', N'Speaking', 'speaking', 4, 20);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, NavigationMode)
            VALUES (@skill, @cert, 'speaking', 'SPK', N'Speaking Pacer Section', 1, 'SYSTEM_PACED_AUTO');
            DECLARE @section INT = SCOPE_IDENTITY();

            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder)
            VALUES (@cert, @skill, 'PRON', N'Pronunciation', 5.0, 1.0, 1);

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, @section, 4);
            DECLARE @group INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
            VALUES (@group, @exam, @section, @skill, N'Read the text aloud.', 1, 'free_response', 45, 45, 4);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Questions
            .Where(q => q.ExamId == examId && q.Section.Modality == "speaking")
            .Select(q => q.QuestionId)
            .SingleAsync();
    }

    private async Task<int> AddWritingQuestionAsync(int certId, int examId)
    {
        await database.SqlAsync($"""
            DECLARE @cert INT = {certId};
            DECLARE @exam INT = {examId};
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'WRIT', N'Writing', 'writing', 3, 30);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder)
            VALUES (@skill, @cert, 'writing', 'WR', N'Writing', 1);
            DECLARE @section INT = SCOPE_IDENTITY();

            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder)
            VALUES (@cert, @skill, 'TASK', N'Task Achievement', 9.0, 1.0, 1);

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, @section, 3);
            DECLARE @group INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@group, @exam, @section, @skill, N'Describe the image.', 1, 'free_response', 10, 100, 3);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Questions
            .Where(q => q.ExamId == examId && q.QuestionType == QuestionType.FreeResponse.ToCode())
            .Select(q => q.QuestionId)
            .SingleAsync();
    }

    private Task AdvanceAttemptToSecondPhaseAsync(int attemptId) =>
        database.SqlAsync($"""
            UPDATE a
            SET a.StartedAt = DATEADD(minute, -31, SYSDATETIMEOFFSET()),
                a.ExpiresAt = DATEADD(minute, 29, SYSDATETIMEOFFSET()),
                a.ActiveSkillId = (
                    SELECT TOP 1 s.SkillId
                    FROM Skills s
                    JOIN Sections sec ON sec.SkillId = s.SkillId
                    JOIN Questions q ON q.SectionId = sec.SectionId
                    WHERE q.ExamId = a.ExamId AND s.SkillId <> a.ActiveSkillId
                    ORDER BY s.DisplayOrder
                ),
                a.PhaseDeadlineAt = DATEADD(minute, 30, GETUTCDATE())
            FROM ExamAttempts a
            WHERE a.AttemptId = {attemptId};
            """);

    [Fact]
    public async Task CreateAttempt_Seeds_Answers_And_Sets_LastQuestionId()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, q2Id, _, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        Assert.NotNull(attempt);
        Assert.Equal(AttemptStatus.InProgress.ToCode(), attempt.Status);
        Assert.Equal(q1Id, attempt.LastQuestionId);
        Assert.True(attempt.ExpiresAt > DateTimeOffset.UtcNow);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var answers = await db.AttemptAnswers.Where(a => a.AttemptId == attempt.AttemptId).ToListAsync();
        Assert.Equal(2, answers.Count);
        Assert.All(answers, a =>
        {
            Assert.Null(a.SelectedOptionId);
            Assert.Null(a.IsCorrect);
            Assert.False(a.IsFlagged);
        });
    }

    [Fact]
    public async Task CreateAttempt_Seeds_Mcq_Answers_And_FreeResponse_Drafts_Separately()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, q2Id, _, _) = await SeedExamFixtureAsync(certCode);
        var freeResponseQuestionId = await AddWritingQuestionAsync(certId, examId);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 120);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var answers = await db.AttemptAnswers.Where(a => a.AttemptId == attempt.AttemptId).ToListAsync();
        var drafts = await db.FreeResponses.Where(r => r.AttemptId == attempt.AttemptId).ToListAsync();

        Assert.Equal(new[] { q1Id, q2Id }.OrderBy(id => id), answers.Select(a => a.QuestionId).OrderBy(id => id));
        var draft = Assert.Single(drafts);
        Assert.Equal(freeResponseQuestionId, draft.QuestionId);
        Assert.Equal(FreeResponseStatus.Draft.ToCode(), draft.Status);
        Assert.Equal(string.Empty, draft.ResponseText);
        Assert.Null(draft.SubmittedAt);
        Assert.NotNull(draft.DraftSavedAt);
    }

    [Fact]
    public async Task GetAttemptTake_Includes_FreeResponse_Prompt_Draft_And_Word_Limits()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var freeResponseQuestionId = await AddWritingQuestionAsync(certId, examId);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 120);

        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, null);

        Assert.Equal(3, take.TotalQuestions);
        var question = Assert.Single(take.Questions!, q => q.QuestionId == freeResponseQuestionId);
        Assert.Equal(QuestionType.FreeResponse.ToCode(), question.QuestionType);
        Assert.Equal(string.Empty, question.ResponseText);
        Assert.Equal(10, question.MinWords);
        Assert.Equal(100, question.MaxWords);
        Assert.Contains(take.Navigator, item => item.QuestionId == freeResponseQuestionId && !item.IsAnswered);
        var phases = take.Phases!;
        Assert.Equal(new[] { "LIST", "READ", "WRIT" }, phases.Select(phase => phase.SkillCode));
        Assert.Equal(120 * 60, phases.Sum(phase => phase.DurationSeconds));
        Assert.Equal(0, phases[0].StartOffsetSeconds);
        Assert.Equal(phases[0].DurationSeconds, phases[1].StartOffsetSeconds);
        Assert.Equal(phases[1].StartOffsetSeconds + phases[1].DurationSeconds, phases[2].StartOffsetSeconds);
    }

    [Fact]
    public async Task SaveDraft_OnlyAllowsActivePhase_AndPersistsDraftText()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var freeResponseQuestionId = await AddWritingQuestionAsync(certId, examId);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 120);

        var wrongPhase = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveDraftAsync(attempt.AttemptId, userId, freeResponseQuestionId, "First draft."));
        Assert.Equal(AttemptEngineException.WrongPhase, wrongPhase.Code);

        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-90);
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var activeAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
            activeAttempt.StartedAt = startedAt;
            activeAttempt.ExpiresAt = startedAt.AddMinutes(120);
            var writingQ = await db.Questions.SingleAsync(q => q.QuestionId == freeResponseQuestionId);
            activeAttempt.ActiveSkillId = writingQ.SkillId;
            activeAttempt.PhaseDeadlineAt = DateTimeOffset.UtcNow.UtcDateTime.AddMinutes(30);
            await db.SaveChangesAsync();
        }

        var result = await CreateEngine().SaveDraftAsync(
            attempt.AttemptId,
            userId,
            freeResponseQuestionId,
            "A saved exam response with several words.");

        Assert.Equal(freeResponseQuestionId, result.QuestionId);
        Assert.Equal(7, result.WordCount);
        await using var resultDb = DbContextFactory.Create(database.ConnectionString);
        var response = await resultDb.FreeResponses.SingleAsync(r =>
            r.AttemptId == attempt.AttemptId && r.QuestionId == freeResponseQuestionId);
        Assert.Equal("A saved exam response with several words.", response.ResponseText);
        Assert.Equal(result.SavedAt, response.DraftSavedAt);
        Assert.Equal(freeResponseQuestionId,
            await resultDb.ExamAttempts.Where(a => a.AttemptId == attempt.AttemptId).Select(a => a.LastQuestionId).SingleAsync());
    }

    [Fact]
    public async Task SubmitAttempt_Flushes_Final_Drafts_And_Queues_Ai_Gradings()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var freeResponseQuestionId = await AddWritingQuestionAsync(certId, examId);
        var userId = await SeedLearnerAsync(certCode);
        var attempt = await CreateEngine().CreateAttemptAsync(userId, certId, examId, 120);

        await using (var draftDb = DbContextFactory.Create(database.ConnectionString))
        {
            var writingService = new WritingPracticeService(draftDb, new LearnerService(draftDb));
            await writingService.SaveDraftAsync(userId, attempt.AttemptId, freeResponseQuestionId, "Background autosaved text.");
        }

        var engine = CreateEngine();
        var finalText = "Last-second text from the active writing editor.";
        var result = await engine.SubmitAttemptAsync(
            attempt.AttemptId,
            userId,
            isAutoSubmit: true,
            finalDrafts: [new AttemptFinalDraft(freeResponseQuestionId, finalText)]);

        Assert.True(result.IsAutoSubmitted);
        Assert.Equal(AttemptStatus.Submitted.ToCode(), result.Attempt.Status);
        var pendingResult = await engine.GetAttemptResultAsync(attempt.AttemptId, userId);
        Assert.Equal(AttemptStatus.Submitted, pendingResult.Status);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var response = await db.FreeResponses.SingleAsync(r =>
            r.AttemptId == attempt.AttemptId && r.QuestionId == freeResponseQuestionId);
        Assert.Equal(finalText, response.ResponseText);
        Assert.Equal(FreeResponseStatus.PendingAi.ToCode(), response.Status);
        Assert.NotNull(response.SubmittedAt);
        Assert.NotNull(response.DraftSavedAt);

        var grading = await db.AiGradings.SingleAsync(g => g.FreeResponseId == response.FreeResponseId);
        Assert.Equal(AiGradingStatus.Pending.ToCode(), grading.Status);
    }

    [Fact]
    public async Task AiGradingCompletion_Preserves_Hybrid_Exam_Objective_Score()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var freeResponseQuestionId = await AddWritingQuestionAsync(certId, examId);
        var userId = await SeedLearnerAsync(certCode);
        var attempt = await CreateEngine().CreateAttemptAsync(userId, certId, examId, 120);
        var engine = CreateEngine();
        await database.SqlAsync($"""
            DECLARE @skillId INT = (SELECT SkillId FROM Questions WHERE QuestionId = {freeResponseQuestionId});

            UPDATE a
            SET a.StartedAt = DATEADD(minute, -90, SYSDATETIMEOFFSET()),
                a.ExpiresAt = DATEADD(minute, 30, SYSDATETIMEOFFSET()),
                a.ActiveSkillId = @skillId,
                a.PhaseDeadlineAt = DATEADD(minute, 30, GETUTCDATE())
            FROM ExamAttempts a
            WHERE a.AttemptId = {attempt.AttemptId};
            """);

        await engine.SaveDraftAsync(
            attempt.AttemptId,
            userId,
            freeResponseQuestionId,
            "A complete written response.");
        await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);
        var submitted = await engine.GetAttemptResultAsync(attempt.AttemptId, userId);
        Assert.Equal(AttemptStatus.Submitted, submitted.Status);
        Assert.Equal(200, submitted.TotalScore);

        await using (var gradingDb = DbContextFactory.Create(database.ConnectionString))
        {
            var processor = new AiGradingProcessor(gradingDb, new FakeAiGrader());
            var freeResponseId = await gradingDb.FreeResponses
                .Where(response => response.AttemptId == attempt.AttemptId)
                .Select(response => response.FreeResponseId)
                .SingleAsync();
            Assert.True(await processor.GradeFreeResponseAsync(freeResponseId));
        }

        await using var resultDb = DbContextFactory.Create(database.ConnectionString);
        var completed = await new AttemptEngine(resultDb).GetAttemptResultAsync(attempt.AttemptId, userId);
        Assert.Equal(AttemptStatus.Graded, completed.Status);
        Assert.Equal(204, completed.TotalScore);
        Assert.Equal(100, completed.SkillScores.Single(s => s.SkillCode == "LIST").ScaledScore);
        Assert.Equal(100, completed.SkillScores.Single(s => s.SkillCode == "READ").ScaledScore);
        Assert.Equal(4, completed.SkillScores.Single(s => s.SkillCode == "WRIT").ScaledScore);
        Assert.Equal(FreeResponseStatus.Graded.ToCode(),
            await resultDb.FreeResponses.Where(response => response.AttemptId == attempt.AttemptId)
                .Select(response => response.Status).SingleAsync());
    }

    [Fact]
    public async Task FinalizeAiGradedAttempt_UnderLength_Writing_Records_Penalty_Flag_And_Weighted_Score()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var freeResponseQuestionId = await AddWritingQuestionAsync(certId, examId);
        var userId = await SeedLearnerAsync(certCode);
        var attempt = await CreateEngine().CreateAttemptAsync(userId, certId, examId, 120);
        var engine = CreateEngine();
        await database.SqlAsync($"""
            UPDATE a
            SET a.StartedAt = DATEADD(minute, -90, SYSDATETIMEOFFSET()),
                a.ExpiresAt = DATEADD(minute, 30, SYSDATETIMEOFFSET()),
                a.ActiveSkillId = (SELECT SkillId FROM Questions WHERE QuestionId = {freeResponseQuestionId}),
                a.PhaseDeadlineAt = DATEADD(minute, 30, GETUTCDATE())
            FROM ExamAttempts a
            WHERE a.AttemptId = {attempt.AttemptId};
            """);

        // Submit under-length essay (3 words < MinWords 10)
        await engine.SaveDraftAsync(
            attempt.AttemptId,
            userId,
            freeResponseQuestionId,
            "Short essay text.");
        await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

        await using (var gradingDb = DbContextFactory.Create(database.ConnectionString))
        {
            var processor = new AiGradingProcessor(gradingDb, new FakeAiGrader());
            var freeResponseId = await gradingDb.FreeResponses
                .Where(response => response.AttemptId == attempt.AttemptId)
                .Select(response => response.FreeResponseId)
                .SingleAsync();
            Assert.True(await processor.GradeFreeResponseAsync(freeResponseId));
        }

        await using var resultDb = DbContextFactory.Create(database.ConnectionString);
        var completed = await new AttemptEngine(resultDb).GetAttemptResultAsync(attempt.AttemptId, userId);
        Assert.Equal(AttemptStatus.Graded, completed.Status);

        // Verify diagnostic length penalty flag recorded in AiGradingScores.Comment
        var fr = await resultDb.FreeResponses.SingleAsync(r => r.AttemptId == attempt.AttemptId);
        var grading = await resultDb.AiGradings.SingleAsync(g => g.FreeResponseId == fr.FreeResponseId);
        var scores = await resultDb.AiGradingScores
            .Where(s => s.GradingId == grading.GradingId)
            .ToListAsync();
        Assert.NotEmpty(scores);
        Assert.All(scores, s => Assert.Contains("IsUnderLength=true", s.Comment));

        // Verify diagnostic length penalty Recommendation with warning severity
        var recommendations = await resultDb.Recommendations
            .Where(r => r.AttemptId == attempt.AttemptId)
            .ToListAsync();
        Assert.Contains(recommendations, r => r.Severity == "minor" && r.Text.Contains("IsUnderLength=true"));
    }

    [Fact]
    public async Task CreateAttempt_Seeds_The_Diagnostic_Benchmark_Hybrid_Question_Set()
    {
        var seedDemoPath = Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql");
        await database.SqlCmdFileToDatabaseAsync("ExtraEfficiencyServicesTest", seedDemoPath);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var exam = await db.Exams.SingleAsync(e => e.Name == "Diagnostic Benchmark 45M");
        var certId = exam.CertId;
        var examId = exam.ExamId;
        var userId = await SeedLearnerAsync("TOEIC");

        var attempt = await CreateEngine().CreateAttemptAsync(userId, certId, examId, exam.DurationMinutes);
        await CreateEngine().VerifyPreflightAsync(attempt.AttemptId, userId);

        await using var resultDb = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(30, await resultDb.AttemptAnswers.CountAsync(a => a.AttemptId == attempt.AttemptId));
        Assert.Equal(2, await resultDb.FreeResponses.CountAsync(r => r.AttemptId == attempt.AttemptId));
        var take = await new AttemptEngine(resultDb).GetAttemptTakeAsync(attempt.AttemptId, userId, null);
        Assert.Equal(32, take.TotalQuestions);
    }

    [Fact]
    public async Task SaveAnswer_Updates_Answer_And_LastQuestionId()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, _, opt1CorrectId, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        var saveResult = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1CorrectId, isFlagged: true);

        Assert.Equal(q1Id, saveResult.QuestionId);
        Assert.Equal(opt1CorrectId, saveResult.SelectedOptionId);
        Assert.True(saveResult.IsFlagged);
        Assert.True(saveResult.RemainingSeconds > 0);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var savedAnswer = await db.AttemptAnswers.SingleAsync(a => a.AttemptId == attempt.AttemptId && a.QuestionId == q1Id);
        Assert.Equal(opt1CorrectId, savedAnswer.SelectedOptionId);
        Assert.True(savedAnswer.IsFlagged);

        var savedAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(q1Id, savedAttempt.LastQuestionId);
    }

    [Fact]
    public async Task SaveAnswer_Rejects_Invalid_Option()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, _, _, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, 999999, isFlagged: false));
        Assert.Equal(AttemptEngineException.InvalidOption, ex.Code);
    }

    [Fact]
    public async Task SaveAnswer_AutoSubmits_When_Expired_And_Throws_AttemptExpired()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, _, opt1CorrectId, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        // Expire attempt manually in DB
        await database.SqlAsync($"""
            UPDATE ExamAttempts
            SET ExpiresAt = DATEADD(minute, -10, SYSDATETIMEOFFSET())
            WHERE AttemptId = {attempt.AttemptId}
            """);

        var engine2 = CreateEngine();
        bool callbackInvoked = false;
        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine2.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1CorrectId, false,
                onAutoSubmitted: (attId, uId) =>
                {
                    callbackInvoked = true;
                    return Task.CompletedTask;
                }));

        Assert.Equal(AttemptEngineException.AttemptExpired, ex.Code);
        Assert.True(callbackInvoked);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reloaded = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(AttemptStatus.Graded.ToCode(), reloaded.Status);
        Assert.True(reloaded.IsAutoSubmitted);
    }

    [Fact]
    public async Task SubmitAttempt_Scores_Answers_Computes_Scales_And_Matches_ScoreBand()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, q2Id, opt1CorrectId, opt2CorrectId) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1CorrectId, false);
        await AdvanceAttemptToSecondPhaseAsync(attempt.AttemptId);
        var secondPhaseEngine = CreateEngine();
        await secondPhaseEngine.SaveAnswerAsync(attempt.AttemptId, userId, q2Id, opt2CorrectId, false);

        await using (var answerDb = DbContextFactory.Create(database.ConnectionString))
        {
            var savedAnswer = await answerDb.AttemptAnswers.SingleAsync(a =>
                a.AttemptId == attempt.AttemptId && a.QuestionId == q2Id);
            Assert.Equal(opt2CorrectId, savedAnswer.SelectedOptionId);
        }

        var result = await secondPhaseEngine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

        // LIST 1 correct -> 450, READ 1 correct -> 450, Total = 900 -> Band 'HIGH' (500-990)
        Assert.Equal(900, result.TotalScore);
        Assert.Equal("HIGH", result.BandCode);
        Assert.Equal(AttemptStatus.Graded.ToCode(), result.Attempt.Status);
        Assert.Equal(2, result.SkillScores.Count);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attemptInDb = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(900, attemptInDb.TotalScore);
        Assert.Equal("HIGH", attemptInDb.BandCode);
        Assert.Equal(AttemptStatus.Graded.ToCode(), attemptInDb.Status);
    }

    [Fact]
    public async Task SubmitAttempt_Is_Idempotent()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, q2Id, opt1CorrectId, opt2CorrectId) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);
        await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1CorrectId, false);
        await AdvanceAttemptToSecondPhaseAsync(attempt.AttemptId);
        var secondPhaseEngine = CreateEngine();
        await secondPhaseEngine.SaveAnswerAsync(attempt.AttemptId, userId, q2Id, opt2CorrectId, false);

        var first = await secondPhaseEngine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);
        var second = await secondPhaseEngine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

        Assert.Equal(first.TotalScore, second.TotalScore);
        Assert.Equal(first.BandCode, second.BandCode);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var skillScoreCount = await db.AttemptSkillScores.CountAsync(s => s.AttemptId == attempt.AttemptId);
        Assert.Equal(2, skillScoreCount);
    }

    [Fact]
    public async Task SubmitAttempt_AutoSyncs_Linked_PlacementTest()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, q2Id, opt1CorrectId, opt2CorrectId) = await SeedExamFixtureAsync(certCode);

        // Set Exam purpose to placement to satisfy FK_Placement_ExamPurpose
        await database.SqlAsync($"""
            UPDATE Exams SET Purpose = 'placement' WHERE ExamId = {examId};
            """);

        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            db.PlacementTests.Add(new PlacementTest
            {
                UserId = userId,
                CertId = certId,
                ExamId = examId,
                AttemptId = attempt.AttemptId,
                Status = PlacementStatus.InProgress.ToCode(),
                ExamPurpose = ExamPurpose.Placement.ToCode(),
                TakenAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var engine2 = CreateEngine();
        await engine2.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1CorrectId, false);
        await AdvanceAttemptToSecondPhaseAsync(attempt.AttemptId);
        var secondPhaseEngine = CreateEngine();
        await secondPhaseEngine.SaveAnswerAsync(attempt.AttemptId, userId, q2Id, opt2CorrectId, false);

        var grading = await secondPhaseEngine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var placement = await db.PlacementTests.SingleAsync(p => p.AttemptId == attempt.AttemptId);
            Assert.Equal(PlacementStatus.Done.ToCode(), placement.Status);
            Assert.Equal(grading.TotalScore, placement.TotalScore);
            Assert.Equal(grading.BandCode, placement.ResultBandCode);
        }
    }

    [Fact]
    public async Task ComputeScaledScore_Falls_Back_Through_Hierarchy()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var engine = CreateEngine();

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var skill = await db.Skills.FirstAsync(s => s.CertId == certId);

        // 1. Exam-specific scale exists for raw score 1 -> returns 450
        var score1 = await engine.ComputeScaledScoreAsync(certId, skill.SkillId, examId, 1, 1);
        Assert.Equal(450, score1);

        // 2. Default scale (ExamId == null): seed a default cert scale for another raw score
        await database.SqlAsync($"""
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
            VALUES ({certId}, {skill.SkillId}, NULL, 5, 300);
            """);

        var scoreDefault = await engine.ComputeScaledScoreAsync(certId, skill.SkillId, 99999, 5, 10);
        Assert.Equal(300, scoreDefault);

        // 3. Proportional fallback when no scale matches: 0/10 -> 5
        var scoreFallback = await engine.ComputeScaledScoreAsync(99999, 99999, 99999, 0, 10);
        Assert.Equal(5, scoreFallback);
    }

    [Fact]
    public async Task AutoSubmitExpiredAttempts_Processes_All_Expired_Attempts()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        // Expire attempt in DB
        await database.SqlAsync($"""
            UPDATE ExamAttempts
            SET ExpiresAt = DATEADD(minute, -30, SYSDATETIMEOFFSET())
            WHERE AttemptId = {attempt.AttemptId}
            """);

        var engine2 = CreateEngine();
        int processed = await engine2.AutoSubmitExpiredAttemptsAsync(userId, certId);
        Assert.Equal(1, processed);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reloaded = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(AttemptStatus.Graded.ToCode(), reloaded.Status);
        Assert.True(reloaded.IsAutoSubmitted);
    }

    [Fact]
    public async Task CancelAttempt_Marks_Attempt_Abandoned()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await engine.CancelAttemptAsync(attempt.AttemptId, userId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reloaded = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(AttemptStatus.Abandoned.ToCode(), reloaded.Status);
    }

    /// <summary>Gives the listening question's group the given audio and, optionally, its section a
    /// studio-uploaded master track.</summary>
    private async Task SetListeningAudioAsync(int examId, int q1Id, string? groupAudioPath, int? startMs, int? endMs, string? masterPath)
    {
        string Sql(string? s) => s is null ? "NULL" : $"N'{s}'";
        string Num(int? n) => n?.ToString() ?? "NULL";
        await database.SqlAsync($"""
            UPDATE QuestionGroups SET AudioPath = {Sql(groupAudioPath)}, AudioStartMs = {Num(startMs)}, AudioEndMs = {Num(endMs)}
            WHERE GroupId = (SELECT GroupId FROM Questions WHERE QuestionId = {q1Id});
            """);
        if (masterPath is not null)
        {
            await database.SqlAsync($"""
                INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
                SELECT {examId}, q.SectionId, e.CertId, N'{masterPath}', 600, 1000
                FROM Questions q JOIN Exams e ON e.ExamId = q.ExamId WHERE q.QuestionId = {q1Id};
                """);
        }
    }

    [Fact]
    public async Task GetAttemptTake_plays_the_section_master_track_limited_to_the_groups_slice()
    {
        var certCode = Unique("AU75");
        var (certId, examId, q1Id, _, _, _) = await SeedExamFixtureAsync(certCode);
        await SetListeningAudioAsync(examId, q1Id, groupAudioPath: null, startMs: 2000, endMs: 5000, masterPath: $"/media/exams/{examId}/master.mp3");
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await engine.VerifyPreflightAsync(attempt.AttemptId, userId);
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, 1);

        Assert.Equal($"/media/exams/{examId}/master.mp3", take.CurrentQuestion.AudioPath);
        Assert.Equal(2000, take.CurrentQuestion.AudioStartMs);
        Assert.Equal(5000, take.CurrentQuestion.AudioEndMs);
    }

    [Fact]
    public async Task GetAttemptTake_prefers_an_uploaded_section_master_over_the_groups_old_audio_file()
    {
        var certCode = Unique("AU75");
        var (certId, examId, q1Id, _, _, _) = await SeedExamFixtureAsync(certCode);
        await SetListeningAudioAsync(examId, q1Id, groupAudioPath: "/audio/toeic/test1-p1.mp3", startMs: null, endMs: null, masterPath: $"exams/{examId}/upload.mp3");
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await engine.VerifyPreflightAsync(attempt.AttemptId, userId);
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, 1);

        Assert.Equal($"/media/exams/{examId}/upload.mp3", take.CurrentQuestion.AudioPath);
        Assert.Null(take.CurrentQuestion.AudioStartMs);
        Assert.Null(take.CurrentQuestion.AudioEndMs);
    }

    [Fact]
    public async Task GetAttemptTake_uses_the_groups_own_audio_when_the_section_has_no_master()
    {
        var certCode = Unique("AU75");
        var (certId, examId, q1Id, _, _, _) = await SeedExamFixtureAsync(certCode);
        await SetListeningAudioAsync(examId, q1Id, groupAudioPath: "/audio/toeic/test1-p1.mp3", startMs: null, endMs: null, masterPath: null);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await engine.VerifyPreflightAsync(attempt.AttemptId, userId);
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, 1);

        Assert.Equal("/audio/toeic/test1-p1.mp3", take.CurrentQuestion.AudioPath);
        Assert.Null(take.CurrentQuestion.AudioStartMs);
    }

    [Fact]
    public async Task GetAttemptReview_replays_the_same_master_slice_the_learner_heard()
    {
        var certCode = Unique("AU75");
        var (certId, examId, q1Id, _, _, _) = await SeedExamFixtureAsync(certCode);
        await SetListeningAudioAsync(examId, q1Id, groupAudioPath: null, startMs: 1500, endMs: 9000, masterPath: $"/media/exams/{examId}/master.mp3");
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);
        await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

        var review = await engine.GetAttemptReviewAsync(attempt.AttemptId, userId);

        var listening = review.Questions.Single(q => q.QuestionId == q1Id);
        Assert.Equal($"/media/exams/{examId}/master.mp3", listening.AudioPath);
        Assert.Equal(1500, listening.AudioStartMs);
        Assert.Equal(9000, listening.AudioEndMs);
    }

    [Fact]
    public async Task GetAttemptTake_populates_all_questions_for_zero_reload_client_swapping()
    {
        var certCode = Unique("AT01");
        var (certId, examId, q1Id, q2Id, _, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await engine.VerifyPreflightAsync(attempt.AttemptId, userId);
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, 1);

        Assert.NotNull(take.Questions);
        Assert.Equal(2, take.Questions.Count);
        Assert.Equal(q1Id, take.Questions[0].QuestionId);
        Assert.Equal(1, take.Questions[0].DisplayOrder);
        Assert.Equal(q2Id, take.Questions[1].QuestionId);
        Assert.Equal(2, take.Questions[1].DisplayOrder);
        Assert.Equal(take.CurrentQuestion.QuestionId, take.Questions[0].QuestionId);
    }

    [Fact]
    public async Task SaveSpeakingAudio_and_submit_queues_ai_grading_cleanly()
    {
        var certCode = Unique("ATSP");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var speakingQuestionId = await AddSpeakingQuestionAsync(certId, examId);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 120);

        // 1. Save speaking audio via Stream overload
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("spoken test recording content"));
        var audioResult = await engine.SaveSpeakingAudioAsync(
            attempt.AttemptId,
            speakingQuestionId,
            userId,
            stream,
            "audio/webm",
            "q4_spoken.webm",
            28000);

        Assert.Equal(speakingQuestionId, audioResult.QuestionId);
        Assert.Equal(28000, audioResult.DurationMs);
        Assert.False(string.IsNullOrWhiteSpace(audioResult.AudioPath));

        await using (var checkDb = DbContextFactory.Create(database.ConnectionString))
        {
            var frDraft = await checkDb.FreeResponses.SingleAsync(r => r.AttemptId == attempt.AttemptId && r.QuestionId == speakingQuestionId);
            Assert.Equal("draft", frDraft.Status);
            Assert.Equal(audioResult.AudioPath, frDraft.AudioPath);
            Assert.Equal(28000, frDraft.DurationMs);
            Assert.NotNull(frDraft.DraftSavedAt);
            Assert.Null(frDraft.SubmittedAt);
        }

        // 2. Submit attempt
        await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

        // 3. Verify attempt status and queued AiGradings
        await using (var gradingDb = DbContextFactory.Create(database.ConnectionString))
        {
            var att = await gradingDb.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
            Assert.Equal("submitted", att.Status);

            var fr = await gradingDb.FreeResponses.SingleAsync(r => r.AttemptId == attempt.AttemptId && r.QuestionId == speakingQuestionId);
            Assert.Equal("pending_ai", fr.Status);
            Assert.NotNull(fr.SubmittedAt);

            var grading = await gradingDb.AiGradings.SingleOrDefaultAsync(g => g.FreeResponseId == fr.FreeResponseId);
            Assert.NotNull(grading);
            Assert.Equal("pending", grading.Status);

            // 4. Drain queue with AiGradingProcessor (sole owner of queue processing per Invariant #6)
            var processor = new AiGradingProcessor(gradingDb, new FakeAiGrader());
            var processed = await processor.ProcessPendingGradingsAsync(10);
            Assert.True(processed >= 1);

            var finishedGrading = await gradingDb.AiGradings.SingleAsync(g => g.FreeResponseId == fr.FreeResponseId);
            Assert.Equal("done", finishedGrading.Status);
        }
    }
}

