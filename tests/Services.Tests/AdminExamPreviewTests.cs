using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #158: an admin reviews a 200-question exam in the learner's own sitting view without publishing
/// it. The invariant these tests pin is the one a preview can most easily break: it must write
/// nothing, and it must not become a door onto a draft for a learner.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminExamPreviewTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private AdminExamService CreateService() =>
        new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    /// <summary>
    /// A certificate with two skills and a <b>draft</b> exam of <paramref name="questionCount"/>
    /// questions spread across them — the primary case, an exam nobody has published.
    /// </summary>
    private async Task<(int CertId, int ExamId)> SeedDraftExamAsync(string certCode, int questionCount = 20)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Preview Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep, DurationMinutes)
            VALUES
              (@cert, 'LST', N'Listening', 'listening', 1, 5, 495, 5, 45),
              (@cert, 'RDT', N'Reading', 'reading', 2, 5, 495, 5, 75);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LST');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'RDT');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, NavigationMode)
            VALUES
              (@sL, @cert, 'listening', 'P1', N'Photographs', 4, 10, 1, 'FULL_SANDBOX'),
              (@sR, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 10, 2, 'FULL_SANDBOX');
            DECLARE @secL INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P1');
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1),
              (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Preview ' + '{certCode}', 'draft', 120, 'mock', {questionCount});
            DECLARE @exam INT = SCOPE_IDENTITY();

            DECLARE @i INT = 1;
            WHILE @i <= {questionCount}
            BEGIN
              DECLARE @section INT = CASE WHEN @i <= {questionCount / 2} THEN @secL ELSE @secR END;
              DECLARE @skill INT = CASE WHEN @i <= {questionCount / 2} THEN @sL ELSE @sR END;

              INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
              VALUES (@exam, @cert, @section, @i);
              DECLARE @grp INT = SCOPE_IDENTITY();

              INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
              VALUES (@grp, @exam, @section, @skill, N'Preview stem ' + CAST(@i AS NVARCHAR(10)), 1, 'mcq',
                      N'Because option A is the only grammatical fit.', @i);
              DECLARE @q INT = SCOPE_IDENTITY();

              INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
                (@q, 'mcq', 'A', N'Correct ' + CAST(@i AS NVARCHAR(10)), 1),
                (@q, 'mcq', 'B', N'Wrong ' + CAST(@i AS NVARCHAR(10)), 0),
                (@q, 'mcq', 'C', N'Also wrong', 0),
                (@q, 'mcq', 'D', N'Still wrong', 0);

              SET @i = @i + 1;
            END
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        return (cert.CertId, exam.ExamId);
    }

    private async Task<int> SeedLearnerAsync(int certId)
    {
        var email = $"{Guid.NewGuid():N}@preview.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Preview Learner', {certId});
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<int> CountAttemptsAsync(int examId, DatabaseFixture fixture)
    {
        await using var db = DbContextFactory.Create(fixture.ConnectionString);
        return await db.ExamAttempts.CountAsync(a => a.ExamId == examId);
    }

    [Fact]
    public async Task A_draft_exam_opens_in_the_sitting_projection_and_every_question_is_reachable()
    {
        var (_, examId) = await SeedDraftExamAsync(Unique("PRV"));
        var service = CreateService();

        var preview = await service.GetExamPreviewTakeAsync(examId, questionDisplayOrder: null);

        Assert.Equal(examId, preview.Take.ExamId);
        Assert.Equal(20, preview.Take.TotalQuestions);
        Assert.Equal(20, preview.Take.Questions!.Count);
        Assert.Equal("draft", preview.ExamStatus);
        Assert.Equal(120, preview.DurationMinutes);
        // The sitting's own phase strip: one per skill, exactly as a learner sees it.
        Assert.Equal(2, preview.Take.Phases!.Count);
        // Nothing is gated: a preview has nothing to protect, and the admin must reach all 20.
        Assert.All(preview.Take.Navigator, item => Assert.False(item.IsLocked));
    }

    [Fact]
    public async Task Any_question_of_the_draft_can_be_jumped_to_directly()
    {
        var (_, examId) = await SeedDraftExamAsync(Unique("PRV"));
        var service = CreateService();

        // First, middle and last — the acceptance criterion's question 1, 10 and 20 — plus a
        // question in the second skill, which a real attempt would refuse without a transition.
        foreach (var order in new[] { 1, 10, 20, 15 })
        {
            var preview = await service.GetExamPreviewTakeAsync(examId, order);
            Assert.Equal(order, preview.Take.CurrentQuestion.DisplayOrder);
        }
    }

    [Fact]
    public async Task The_answer_key_carries_the_correct_option_and_the_explanation()
    {
        var (_, examId) = await SeedDraftExamAsync(Unique("PRV"));
        var service = CreateService();

        var preview = await service.GetExamPreviewTakeAsync(examId, 3);

        Assert.Equal(20, preview.Answers.Count);
        var answer = Assert.Single(preview.Answers, item => item.QuestionId == preview.Take.CurrentQuestion.QuestionId);
        Assert.Single(answer.CorrectOptionIds);
        Assert.Equal(
            preview.Take.CurrentQuestion.Options.Single(option => option.OptionId == answer.CorrectOptionIds[0]).OptionId,
            answer.CorrectOptionIds[0]);
        Assert.Equal("Because option A is the only grammatical fit.", answer.Explanation);
    }

    [Fact]
    public async Task The_section_jumps_cover_each_section_in_sitting_order()
    {
        var (_, examId) = await SeedDraftExamAsync(Unique("PRV"));
        var service = CreateService();

        var preview = await service.GetExamPreviewTakeAsync(examId, null);

        Assert.Equal(2, preview.Sections.Count);
        Assert.Equal(["P1", "P5"], preview.Sections.Select(s => s.SectionCode));
        Assert.Equal(1, preview.Sections[0].FirstDisplayOrder);
        Assert.Equal(10, preview.Sections[0].LastDisplayOrder);
        Assert.Equal(11, preview.Sections[1].FirstDisplayOrder);
        Assert.Equal(20, preview.Sections[1].LastDisplayOrder);
    }

    /// <summary>
    /// AC4, the one that must be proven rather than eyeballed: walking all 20 questions writes no
    /// <c>ExamAttempt</c>. A preview that quietly started an attempt would also mark the exam as
    /// having been sat, and would not survive #157's publish gate.
    /// </summary>
    [Fact]
    public async Task A_full_walk_writes_no_exam_attempt_and_no_answer()
    {
        var (_, examId) = await SeedDraftExamAsync(Unique("PRV"));
        var service = CreateService();
        var before = await CountAttemptsAsync(examId, database);

        for (var order = 1; order <= 20; order++)
        {
            await service.GetExamPreviewTakeAsync(examId, order);
        }

        Assert.Equal(before, await CountAttemptsAsync(examId, database));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(0, await db.AttemptAnswers.CountAsync(a => a.ExamId == examId));
        Assert.Equal(0, await db.FreeResponses.CountAsync(r => r.ExamId == examId));
        // And #157's gate is untouched: the exam is still a draft, not published by a preview.
        Assert.Equal("draft", await db.Exams.Where(e => e.ExamId == examId).Select(e => e.Status).SingleAsync());
    }

    /// <summary>
    /// AC5: the same URL a learner could type must not open the draft. The learner's service is
    /// what the learner path calls, and it still filters on <c>Status = 'published'</c>.
    /// </summary>
    [Fact]
    public async Task A_learner_cannot_open_the_draft_exam_through_the_learner_path()
    {
        var (certId, examId) = await SeedDraftExamAsync(Unique("PRV"));
        var userId = await SeedLearnerAsync(certId);
        var (mockExams, _) = (new MockExamService(DbContextFactory.Create(database.ConnectionString),
            new LearnerService(DbContextFactory.Create(database.ConnectionString))), 0);

        var refused = await Assert.ThrowsAsync<MockExamException>(
            () => mockExams.GetExamInstructionsAsync(userId, examId));
        Assert.Equal(MockExamException.ExamNotFound, refused.Code);

        // The admin path on the very same exam is what preview unlocks — this is the whole ticket.
        var preview = await CreateService().GetExamPreviewTakeAsync(examId, null);
        Assert.Equal(20, preview.Take.TotalQuestions);
    }

    [Fact]
    public async Task A_published_exam_previews_too_and_reports_its_status()
    {
        var (_, examId) = await SeedDraftExamAsync(Unique("PRV"));
        await database.SqlAsync($"UPDATE Exams SET Status = 'published' WHERE ExamId = {examId};");

        var preview = await CreateService().GetExamPreviewTakeAsync(examId, null);

        Assert.Equal("published", preview.ExamStatus);
        Assert.Equal(20, preview.Take.TotalQuestions);
    }

    [Fact]
    public async Task An_unknown_or_deleted_exam_is_not_previewable()
    {
        var service = CreateService();

        var missing = await Assert.ThrowsAsync<ExamNotFoundException>(
            () => service.GetExamPreviewTakeAsync(int.MaxValue, null));
        Assert.Equal(ApiError.Codes.ExamNotFound, missing.Code);
    }
}
