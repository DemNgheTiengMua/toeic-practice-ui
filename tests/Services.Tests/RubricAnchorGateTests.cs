using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #193: the three calibrated anchors at bands 2, 3 and 4. They are authorable before the lock, they
/// travel into the grading prompt, and a locked rubric is never graded without them.
/// <para>
/// The ladder's own arithmetic runs against a scripted <see cref="IAiGrader"/> (a fake grader) because
/// the ladder is an authoring check: no learner, no queue, no model. The gate below drives the real
/// processor against the real schema.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RubricAnchorGateTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private static string AnchorText(int band) => $"Anchor response written at band {band}: a whole answer, not a note.";

    /// <summary>A writing-practice exam for one skill, with the given canonical criteria and a grid each.</summary>
    private async Task<(int CertId, int SkillId, int ExamId, int QuestionId, IReadOnlyList<int> CriterionIds)>
        SeedAsync(string certCode, params string[] codes)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'WRIT', N'Writing', 'writing', 1, 60);
            DECLARE @sW INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'WRIT');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder)
            VALUES (@sW, @cert, 'writing', 'W1', N'Essay', 1, 1);
            DECLARE @sec INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'W1');
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Writing Practice {certCode}', 'published', 60, 'writing_practice', 1);
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@grp, @exam, @sec, @sW, N'Write about your week.', 1, 'free_response', 5, 200, 1);
            DECLARE @q INT = SCOPE_IDENTITY();

            {(string.Join("\n", codes.Select((code, i) => $"""
                INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, RubricDescription, MaxScore, Weight, DisplayOrder)
                VALUES (@cert, @sW, '{code}', N'{code} criterion', N'Prose for {code}.', 6.0, 1.0, {i + 1});
                DECLARE @crit{i} INT = SCOPE_IDENTITY();
                INSERT INTO GradingCriterionDescriptors (CriterionId, Band, Descriptor, BinaryGates)
                  SELECT @crit{i}, b.Band,
                         N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N': speech rate at least ' + CAST(60 + b.Band*20 AS NVARCHAR(4)) + N' wpm.',
                         N'Error-free T-units at least ' + CAST(10 + b.Band*12 AS NVARCHAR(3)) + N'%.'
                    FROM (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band);
                """)))}

            -- anchors at bands 2, 3 and 4, authored while the rubric is still open
            {(string.Join("\n", codes.Select((code, i) => $"""
                INSERT INTO GradingCriterionAnchors (CriterionId, Band, ResponseText)
                VALUES (@crit{i}, 2, N'{AnchorText(2)}'), (@crit{i}, 3, N'{AnchorText(3)}'), (@crit{i}, 4, N'{AnchorText(4)}');
                """)))}

            SELECT @cert AS CertId, @sW AS SkillId, @exam AS ExamId, @q AS QuestionId;
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var question = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId);
        var skillId = await db.Skills.Where(s => s.CertId == cert.CertId).Select(s => s.SkillId).SingleAsync();
        var criterionIds = await db.GradingCriteria
            .Where(c => c.CertId == cert.CertId)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => c.CriterionId)
            .ToListAsync();
        return (cert.CertId, skillId, exam.ExamId, question.QuestionId, criterionIds);
    }

    private async Task<int> SeedLearnerAsync(string certCode)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var adminEmail = $"admin-{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{adminEmail}', 0x01, N'Admin', SYSDATETIMEOFFSET());
            DECLARE @adminId INT = (SELECT UserId FROM Users WHERE Email = '{adminEmail}');
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES ((SELECT UserId FROM Users WHERE Email = '{email}'), 'admin_grant', 3, @adminId, N'Test credits');
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<int> SubmitAsync(int userId, int examId, int questionId, string answer)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var writing = new WritingPracticeService(db, new LearnerService(db));
        var attempt = await writing.StartAttemptAsync(userId, examId);
        await writing.SaveDraftAsync(userId, attempt.AttemptId, questionId, answer);
        await writing.SubmitAttemptAsync(userId, attempt.AttemptId);
        return attempt.AttemptId;
    }

    private static readonly string[] CanonicalWriting = ["W_TR", "W_CC", "W_LR", "W_GR"];

    [Fact]
    public async Task Three_anchors_at_two_three_and_four_travel_into_the_grading_call()
    {
        // The accept direction: a locked-or-not rubric whose criteria carry all three anchors is graded,
        // and every anchor is in the request the grader receives. Without this the gate below could be a
        // blanket refusal and still be green.
        var certCode = Unique("AN01");
        var (_, skillId, examId, questionId, criterionIds) = await SeedAsync(certCode, CanonicalWriting);
        await database.SqlAsync(
            $"INSERT INTO GradingRubricLocks (SkillId, CertId) SELECT {skillId}, CertId FROM Skills WHERE SkillId = {skillId};");
        var userId = await SeedLearnerAsync(certCode);
        var attemptId = await SubmitAsync(userId, examId, questionId, "A short but real answer to the task.");

        var grader = new CapturingGrader();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var freeResponseId = await db.FreeResponses
            .Where(r => r.AttemptId == attemptId).Select(r => r.FreeResponseId).SingleAsync();
        await new AiGradingProcessor(db, grader).ProcessPendingGradingsAsync(10);

        var sent = Assert.Single(grader.Requests, r => r.FreeResponseId == freeResponseId);
        Assert.All(sent.Criteria, c =>
        {
            Assert.NotNull(c.Anchors);
            Assert.Equal([2, 3, 4], c.Anchors!.Select(a => a.Band));
            Assert.All(c.Anchors!, a => Assert.Equal(AnchorText(a.Band), a.ResponseText));
            Assert.True(c.IsAnchored);
        });

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var grading = await check.AiGradings.Include(g => g.FreeResponse)
            .SingleAsync(g => g.FreeResponse.AttemptId == attemptId);
        Assert.Equal("done", grading.Status);
    }

    [Fact]
    public async Task A_locked_rubric_with_one_criterion_unanchored_is_failed_and_names_it()
    {
        // The input that would pass a lazy guard: three of four criteria anchored, and a guard that asks
        // "does ANY criterion carry anchors" says yes, so the rubric is graded with one standard missing.
        // The four-criterion composite then averages a criterion the model had no benchmark for.
        var certCode = Unique("AN02");
        var (_, skillId, examId, questionId, criterionIds) = await SeedAsync(certCode, CanonicalWriting);
        await database.SqlAsync($"""
            DELETE FROM GradingCriterionAnchors WHERE CriterionId = {criterionIds[3]};
            INSERT INTO GradingRubricLocks (SkillId, CertId) SELECT {skillId}, CertId FROM Skills WHERE SkillId = {skillId};
            """);
        var userId = await SeedLearnerAsync(certCode);
        var attemptId = await SubmitAsync(userId, examId, questionId, "A short but real answer to the task.");

        var grader = new CapturingGrader();
        var logger = new RecordingLogger();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var freeResponseId = await db.FreeResponses
            .Where(r => r.AttemptId == attemptId).Select(r => r.FreeResponseId).SingleAsync();
        await new AiGradingProcessor(db, grader, logger).ProcessPendingGradingsAsync(10);

        Assert.DoesNotContain(grader.Requests, r => r.FreeResponseId == freeResponseId);

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var grading = await check.AiGradings.Include(g => g.FreeResponse)
            .SingleAsync(g => g.FreeResponse.AttemptId == attemptId);
        Assert.Equal("failed", grading.Status);
        Assert.Null(grading.OverallScaled);
        Assert.Contains("W_GR", grading.FailureReason, StringComparison.Ordinal);
        Assert.Empty(await check.AiGradingScores.Where(s => s.GradingId == grading.GradingId).ToListAsync());
    }

    [Fact]
    public async Task Two_anchors_missing_the_middle_band_are_refused_and_the_missing_band_is_named()
    {
        // The mutation a count guard cannot catch without knowing which bands: bands 2 and 4 are
        // anchored and band 3 is not, so "the rubric has anchors" and "more than one anchor" both pass.
        // Band 3 is the one that matters most - it is where verbosity is mistaken for ability - and the
        // reason has to name the band, or the author cannot tell which row to write.
        var certCode = Unique("AN03");
        var (_, skillId, examId, questionId, criterionIds) = await SeedAsync(certCode, CanonicalWriting);
        await database.SqlAsync(
            $"DELETE FROM GradingCriterionAnchors WHERE CriterionId = {criterionIds[0]} AND Band = 3;");
        await database.SqlAsync(
            $"INSERT INTO GradingRubricLocks (SkillId, CertId) SELECT {skillId}, CertId FROM Skills WHERE SkillId = {skillId};");
        var userId = await SeedLearnerAsync(certCode);
        var attemptId = await SubmitAsync(userId, examId, questionId, "A short but real answer to the task.");

        var grader = new CapturingGrader();
        var logger = new RecordingLogger();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var freeResponseId = await db.FreeResponses
            .Where(r => r.AttemptId == attemptId).Select(r => r.FreeResponseId).SingleAsync();
        await new AiGradingProcessor(db, grader, logger).ProcessPendingGradingsAsync(10);

        Assert.DoesNotContain(grader.Requests, r => r.FreeResponseId == freeResponseId);

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var grading = await check.AiGradings.Include(g => g.FreeResponse)
            .SingleAsync(g => g.FreeResponse.AttemptId == attemptId);
        Assert.Equal("failed", grading.Status);
        // The criterion AND the missing band are both named.
        Assert.Contains("W_TR", grading.FailureReason, StringComparison.Ordinal);
        Assert.Contains("band 3", grading.FailureReason, StringComparison.Ordinal);
        // The log carries the same detail: the reason column is read by a learner who cannot act on it.
        Assert.Contains(logger.Warnings,
            w => w.Contains($"GradingId {grading.GradingId}", StringComparison.Ordinal)
                 && w.Contains("W_TR", StringComparison.Ordinal)
                 && w.Contains("band 3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unlocked_rubric_is_still_graded_without_anchors_so_every_legacy_rubric_keeps_working()
    {
        // The gate is the lock, not the table. Every rubric authored before #193 has no anchors and
        // grades today; refusing them all would be a migration, not a calibration rule.
        var certCode = Unique("AN04");
        var (_, _, examId, questionId, criterionIds) = await SeedAsync(certCode, ["W_TR"]);
        await database.SqlAsync($"DELETE FROM GradingCriterionAnchors WHERE CriterionId = {criterionIds[0]};");
        var userId = await SeedLearnerAsync(certCode);
        var attemptId = await SubmitAsync(userId, examId, questionId, "A short but real answer to the task.");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        await new AiGradingProcessor(db, new FakeAiGrader()).ProcessPendingGradingsAsync(10);

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var grading = await check.AiGradings.Include(g => g.FreeResponse)
            .SingleAsync(g => g.FreeResponse.AttemptId == attemptId);
        Assert.Equal("done", grading.Status);
    }

    [Fact]
    public async Task EF_can_insert_and_update_an_anchor_row_which_only_works_with_HasTrigger()
    {
        // ADR-0001: a table with a trigger needs HasTrigger declared or every EF save fails on the
        // OUTPUT clause. The gate tests above read anchors and never write them through EF, so without
        // this a missing declaration would ship green and fail on the first authoring save.
        var certCode = Unique("AN05");
        var (_, _, _, _, criterionIds) = await SeedAsync(certCode, ["W_TR"]);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var anchor = await db.GradingCriterionAnchors
            .SingleAsync(a => a.CriterionId == criterionIds[0] && a.Band == 3);
        anchor.ResponseText = AnchorText(3) + " Edited while the rubric is open.";
        await db.SaveChangesAsync();

        // INSERT through EF on a fresh, unlocked criterion.
        await database.SqlAsync($"""
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, RubricDescription, MaxScore, Weight, DisplayOrder)
              SELECT c.CertId, c.SkillId, 'W_ZZ', N'Fresh', N'Prose.', 6, 1.0, 9
                FROM GradingCriteria c WHERE c.CriterionId = {criterionIds[0]};
            """);
        await using var db2 = DbContextFactory.Create(database.ConnectionString);
        var freshId = await db2.GradingCriteria
            .Where(c => c.Cert.Code == certCode && c.Code == "W_ZZ")
            .Select(c => c.CriterionId)
            .SingleAsync();
        db2.GradingCriterionAnchors.Add(new GradingCriterionAnchor
        {
            CriterionId = freshId,
            Band = 2,
            ResponseText = AnchorText(2),
        });
        await db2.SaveChangesAsync();

        await using var check = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(AnchorText(3) + " Edited while the rubric is open.",
            await check.GradingCriterionAnchors
                .Where(a => a.CriterionId == criterionIds[0] && a.Band == 3)
                .Select(a => a.ResponseText).SingleAsync());
        Assert.Equal(1, await check.GradingCriterionAnchors.CountAsync(a => a.CriterionId == freshId));
    }

    /// <summary>xUnit reports one test per theory case, so the three bands are asserted together.</summary>
    [Fact]
    public async Task A_band_outside_two_to_four_is_refused_by_the_table_itself()
    {
        var certCode = Unique("AN06");
        var (_, _, _, _, criterionIds) = await SeedAsync(certCode, ["W_TR"]);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attempt = () => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO GradingCriterionAnchors (CriterionId, Band, ResponseText) VALUES ({0}, 5, N'x')",
            criterionIds[0]);

        var error = await Assert.ThrowsAnyAsync<Exception>(attempt);
        Assert.Contains("CK_GCA_Band", Flatten(error), StringComparison.Ordinal);
    }

    private static string Flatten(Exception e)
    {
        var parts = new List<string>();
        for (var x = e; x is not null; x = x.InnerException) parts.Add(x.Message);
        return string.Join(" | ", parts);
    }

    private sealed class RecordingLogger : ILogger<AiGradingProcessor>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }

    private sealed class CapturingGrader : IAiGrader
    {
        public List<AiGradingRequest> Requests { get; } = [];

        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var scores = request.Criteria
                .Select(c => new AiCriterionScoreResult(c.CriterionId, c.MaxScore, "noted"))
                .ToList();
            return Task.FromResult(new AiGradingResult(true, null, "capturing-grader", scores));
        }
    }
}
