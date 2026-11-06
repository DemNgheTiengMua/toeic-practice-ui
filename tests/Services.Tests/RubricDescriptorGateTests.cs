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
/// #192: a criterion with no descriptor text is not graded. The grading is Failed, the reason says so,
/// and the criterion's code is logged - never an inferred descriptor, never a filled-in score.
/// <para>
/// These run against the real schema, because the descriptor grid is a real table with real triggers
/// now, and because the defect being removed was a number appearing where no standard existed.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RubricDescriptorGateTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>Records warnings so a test can assert on what the log says, not only on the row.</summary>
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

    /// <summary>
    /// A writing-practice exam whose one criterion is <paramref name="code"/> with
    /// <paramref name="descriptorSqlGrid"/> rows of grid. An empty grid means "none authored", which is
    /// the legacy shape.
    /// </summary>
    private async Task<(int CertId, int ExamId, int QuestionId, int CriterionId)> SeedAsync(
        string certCode, string code, string? rubricDescription, int gridRows, bool completeText = true,
        (string Code, string? Rubric, int GridRows)? second = null)
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

            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, RubricDescription, MaxScore, Weight, DisplayOrder)
            VALUES (@cert, @sW, '{code}', N'{code} criterion',
                    {(rubricDescription is null ? "NULL" : $"N'{rubricDescription.Replace("'", "''")}'")}, 6.0, 1.0, 1);
            DECLARE @crit INT = SCOPE_IDENTITY();
            {(second is { } s2
                ? $"INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, RubricDescription, MaxScore, Weight, DisplayOrder) VALUES (@cert, @sW, '{s2.Code}', N'{s2.Code} criterion', {(s2.Rubric is null ? "NULL" : $"N'{s2.Rubric.Replace("'", "''")}'")}, 6.0, 1.0, 2);"
                : "")}

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Writing Practice {certCode}', 'published', 60, 'writing_practice', 1);
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@grp, @exam, @sec, @sW, N'Write about your week.', 'A2', 'free_response', 5, 200, 1);
            """);

        if (gridRows > 0)
        {
            // gridRows = 7 gives the full grid; anything less is a half-authored one. The text is
            // deliberately a threshold, so a test that passes is not passing on "at least 40 chars".
            // Scoped to the FIRST criterion by display order, so a two-criterion fixture can leave the
            // second deliberately unauthored.
            await database.SqlAsync($"""
                INSERT INTO GradingCriterionDescriptors (CriterionId, Band, Descriptor, BinaryGates)
                  SELECT TOP {gridRows} c.CriterionId, b.Band,
                         {(completeText
                             ? "N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N': speech rate at least ' + CAST(60 + b.Band*20 AS NVARCHAR(4)) + N' wpm.'"
                             : "N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N': writes clearly.'")},
                         N'Error-free T-units at least ' + CAST(10 + b.Band*12 AS NVARCHAR(3)) + N'%.'
                    FROM GradingCriteria c
                    CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
                   WHERE c.CriterionId = (SELECT TOP 1 c2.CriterionId FROM GradingCriteria c2
                                            JOIN Certificates cc ON cc.CertId = c2.CertId
                                           WHERE cc.Code = '{certCode}' ORDER BY c2.DisplayOrder)
                   ORDER BY b.Band;
                """);

            if (second is { } grid2 && grid2.GridRows > 0)
            {
                await database.SqlAsync($"""
                    INSERT INTO GradingCriterionDescriptors (CriterionId, Band, Descriptor, BinaryGates)
                      SELECT TOP {grid2.GridRows} c.CriterionId, b.Band,
                             N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N': lexical diversity ratio at least 0.' + CAST(40 + b.Band*5 AS NVARCHAR(2)) + N'.',
                             N'At most ' + CAST(6 - b.Band AS NVARCHAR(2)) + N' repeated content words per 100 tokens.'
                        FROM GradingCriteria c
                        CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
                       WHERE c.CriterionId = (SELECT TOP 1 c2.CriterionId FROM GradingCriteria c2
                                                JOIN Certificates cc ON cc.CertId = c2.CertId
                                               WHERE cc.Code = '{certCode}' ORDER BY c2.DisplayOrder DESC)
                       ORDER BY b.Band;
                    """);
            }

            await using var dbGrid = DbContextFactory.Create(database.ConnectionString);
            var criterionId = await dbGrid.GradingCriteria
                .Where(c => c.Cert.Code == certCode)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => c.CriterionId)
                .FirstAsync();
            var actualRows = await dbGrid.GradingCriterionDescriptors.CountAsync(d => d.CriterionId == criterionId);
            Assert.Equal(gridRows, actualRows);

            await using var check = DbContextFactory.Create(database.ConnectionString);
            var cert = await check.Certificates.SingleAsync(c => c.Code == certCode);
            var exam = await check.Exams.SingleAsync(e => e.CertId == cert.CertId);
            var question = await check.Questions.SingleAsync(q => q.ExamId == exam.ExamId);
            return (cert.CertId, exam.ExamId, question.QuestionId, criterionId);
        }

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var c2 = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var e2 = await db.Exams.SingleAsync(e => e.CertId == c2.CertId);
        var q2 = await db.Questions.SingleAsync(q => q.ExamId == e2.ExamId);
        var crit2 = await db.GradingCriteria
            .Where(c => c.Cert.Code == certCode)
            .OrderBy(c => c.DisplayOrder)
            .FirstAsync();
        return (c2.CertId, e2.ExamId, q2.QuestionId, crit2.CriterionId);
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

    /// <summary>Answers the one question, submits, and returns the attempt id.</summary>
    private async Task<int> SubmitAsync(int userId, int examId, int questionId, string answer)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var writing = new WritingPracticeService(db, new LearnerService(db));
        var attempt = await writing.StartAttemptAsync(userId, examId);
        await writing.SaveDraftAsync(userId, attempt.AttemptId, questionId, answer);
        await writing.SubmitAttemptAsync(userId, attempt.AttemptId);
        return attempt.AttemptId;
    }

    [Fact]
    public async Task A_criterion_with_no_descriptor_at_all_is_failed_and_the_reason_names_its_code()
    {
        // The ticket's acceptance case. No rubric text and no grid: there is nothing to score
        // against, so nothing is scored - not a zero, not a MaxScore x 0.7, not an inferred band.
        var certCode = Unique("RD01");
        var (_, examId, questionId, _) = await SeedAsync(certCode, "W_TR", rubricDescription: null, gridRows: 0);
        var userId = await SeedLearnerAsync(certCode);
        var attemptId = await SubmitAsync(userId, examId, questionId, "A short but real answer to the task.");

        var logger = new RecordingLogger();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var processor = new AiGradingProcessor(db, new FakeAiGrader(), logger);
        await processor.ProcessPendingGradingsAsync(10);

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var grading = await check.AiGradings.Include(g => g.FreeResponse)
            .SingleAsync(g => g.FreeResponse.AttemptId == attemptId);

        Assert.Equal("failed", grading.Status);
        Assert.Null(grading.OverallScaled);
        Assert.Null(grading.BandCode);
        Assert.Contains("W_TR", grading.FailureReason, StringComparison.Ordinal);
        Assert.Empty(await check.AiGradingScores.Where(s => s.GradingId == grading.GradingId).ToListAsync());

        // The log carries the code too: an administrator reading the log has to be able to find the
        // row, and the reason column is read by a learner who cannot act on it. Scoped to this
        // grading id, because the fixture shares one database and other gradings may be in the batch.
        Assert.Contains(logger.Warnings,
            w => w.Contains($"GradingId {grading.GradingId}", StringComparison.Ordinal)
                 && w.Contains("W_TR", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_criterion_with_six_of_seven_bands_is_failed_rather_than_graded_on_six()
    {
        // The half-authored grid. The criterion has text at bands 0-5 and nothing at 6, so a model
        // asked to score against it would have to invent C2. That is exactly the inference the ticket
        // forbids, so the grading is Failed instead.
        var certCode = Unique("RD02");
        var (_, examId, questionId, _) = await SeedAsync(
            certCode, "W_CC", rubricDescription: "Six bands are authored; the seventh is missing.", gridRows: 6);
        var userId = await SeedLearnerAsync(certCode);
        var attemptId = await SubmitAsync(userId, examId, questionId, "A short but real answer to the task.");

        var logger = new RecordingLogger();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        await new AiGradingProcessor(db, new FakeAiGrader(), logger).ProcessPendingGradingsAsync(10);

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var grading = await check.AiGradings.Include(g => g.FreeResponse)
            .SingleAsync(g => g.FreeResponse.AttemptId == attemptId);

        Assert.Equal("failed", grading.Status);
        Assert.Contains("W_CC", grading.FailureReason, StringComparison.Ordinal);
        Assert.Empty(await check.AiGradingScores.Where(s => s.GradingId == grading.GradingId).ToListAsync());

        // The band that is missing is named, not merely "incomplete". Scoped to this grading id so a
        // sibling test's warnings cannot satisfy the assertion.
        Assert.Contains(logger.Warnings,
            w => w.Contains($"GradingId {grading.GradingId}", StringComparison.Ordinal)
                 && w.Contains("W_CC", StringComparison.Ordinal)
                 && w.Contains("band(s) 6", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_complete_seven_band_grid_is_graded_and_the_prompt_carries_each_band()
    {
        // The accept direction, and the one that keeps the guard above from being a blanket refusal:
        // seven bands with thresholds is a rubric, so it scores.
        var certCode = Unique("RD03");
        var (_, examId, questionId, criterionId) = await SeedAsync(
            certCode, "W_TR", rubricDescription: null, gridRows: 7);
        var userId = await SeedLearnerAsync(certCode);
        var attemptId = await SubmitAsync(userId, examId, questionId, "A short but real answer to the task.");

        var grader = new CapturingGrader();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var freeResponseId = await db.FreeResponses
            .Where(r => r.AttemptId == attemptId)
            .Select(r => r.FreeResponseId)
            .SingleAsync();
        await new AiGradingProcessor(db, grader).ProcessPendingGradingsAsync(10);

        // Scoped to this response: the fixture shares one database, so the batch may hold siblings.
        var sent = Assert.Single(grader.Requests, r => r.FreeResponseId == freeResponseId)
            .Criteria.Single(c => c.CriterionId == criterionId);
        Assert.NotNull(sent.Descriptors);
        Assert.Equal(7, sent.Descriptors!.Count);
        Assert.Equal(Enumerable.Range(0, 7).Select(b => (byte)b), sent.Descriptors!.Select(d => d.Band));
        Assert.All(sent.Descriptors!, d => Assert.Contains("wpm", d.Descriptor, StringComparison.Ordinal));

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var grading = await check.AiGradings.Include(g => g.FreeResponse)
            .SingleAsync(g => g.FreeResponse.AttemptId == attemptId);
        Assert.Equal("done", grading.Status);
        Assert.NotNull(grading.OverallScaled);
    }

    [Fact]
    public async Task One_complete_criterion_does_not_rescue_a_rubric_with_one_unauthored_criterion()
    {
        // The input that would pass a lazy guard: three of four criteria written, one blank. A guard
        // that checks "does ANY criterion have text" says yes and grades the three, producing a
        // composite over 3 of 4 criteria - which is not the mean D0.6 defines, and hides the gap from
        // everyone. The rubric is failed whole, and both codes are named so the missing one is findable.
        var certCode = Unique("RD04");
        var (_, examId, questionId, _) = await SeedAsync(
            certCode, "W_TR", rubricDescription: "Complete prose for task fulfilment.", gridRows: 7,
            second: ("W_CC", null, 0));
        var userId = await SeedLearnerAsync(certCode);
        var attemptId = await SubmitAsync(userId, examId, questionId, "A short but real answer to the task.");

        var logger = new RecordingLogger();
        var grader = new CapturingGrader();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var freeResponseId = await db.FreeResponses
            .Where(r => r.AttemptId == attemptId)
            .Select(r => r.FreeResponseId)
            .SingleAsync();
        await new AiGradingProcessor(db, grader, logger).ProcessPendingGradingsAsync(10);

        // No model call for THIS response: there is no complete rubric to send it.
        Assert.DoesNotContain(grader.Requests, r => r.FreeResponseId == freeResponseId);

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var grading = await check.AiGradings.Include(g => g.FreeResponse)
            .SingleAsync(g => g.FreeResponse.AttemptId == attemptId);

        Assert.Equal("failed", grading.Status);
        Assert.Null(grading.OverallScaled);
        Assert.Contains("W_CC", grading.FailureReason, StringComparison.Ordinal);
        Assert.Empty(await check.AiGradingScores.Where(s => s.GradingId == grading.GradingId).ToListAsync());
    }

    [Fact]
    public async Task EF_can_insert_and_update_a_descriptor_row_which_only_works_with_HasTrigger()
    {
        // ADR-0001: a table with a trigger needs HasTrigger declared, or every EF save against it
        // fails with "The target table ... has a trigger ... OUTPUT clause is not supported". The
        // grading tests read descriptors but never write them through EF, so without this test a
        // missing declaration would ship green and fail on the first authoring save.
        var certCode = Unique("RD05");
        var (_, _, _, criterionId) = await SeedAsync(
            certCode, "W_TR", rubricDescription: "Prose for task fulfilment.", gridRows: 7);

        await using var db = DbContextFactory.Create(database.ConnectionString);

        // UPDATE through EF: the trigger on this table fires on UPDATE.
        var band4 = await db.GradingCriterionDescriptors
            .SingleAsync(d => d.CriterionId == criterionId && d.Band == 4);
        band4.Descriptor = "Band 4: speech rate at least 110 wpm, under 6 pauses over 1500 ms per minute.";
        await db.SaveChangesAsync();

        // INSERT through EF: the trigger also fires on INSERT, and this row is on a rubric that is
        // not locked, so the write is allowed - the point is that EF can send it at all.
        await database.SqlAsync($"""
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, RubricDescription, MaxScore, Weight, DisplayOrder)
              SELECT c.CertId, c.SkillId, 'W_ZZ', N'Fresh', N'Prose.', 6, 1.0, 9
                FROM GradingCriteria c WHERE c.CriterionId = {criterionId};
            """);
        await using var db2 = DbContextFactory.Create(database.ConnectionString);
        var freshId = await db2.GradingCriteria
            .Where(c => c.Cert.Code == certCode && c.Code == "W_ZZ")
            .Select(c => c.CriterionId)
            .SingleAsync();
        db2.GradingCriterionDescriptors.Add(new GradingCriterionDescriptor
        {
            CriterionId = freshId,
            Band = 0,
            Descriptor = "Band 0: no assessable language beyond single words.",
            BinaryGates = "Fewer than 20 running words."
        });
        await db2.SaveChangesAsync();

        await using var check = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(
            "Band 4: speech rate at least 110 wpm, under 6 pauses over 1500 ms per minute.",
            await check.GradingCriterionDescriptors
                .Where(d => d.CriterionId == criterionId && d.Band == 4)
                .Select(d => d.Descriptor)
                .SingleAsync());
        Assert.Equal(1, await check.GradingCriterionDescriptors.CountAsync(d => d.CriterionId == freshId));
    }

    /// <summary>Answers every criterion at its own band, so a pass is not an accident of clamping.</summary>
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
