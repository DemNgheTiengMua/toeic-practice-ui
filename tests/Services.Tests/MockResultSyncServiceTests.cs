#pragma warning disable EF1002
using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #222 / D-26 / OQ-TR-01 / PRN-03: a mock exam keeps its own certificate score and gains a separately
/// labelled projected level beside it.
/// <para>
/// Every test here seeds its own certificate, learner and attempt on a fresh catalog and drops it in
/// <c>finally</c>. That is not tidiness: a certificate inserted into the shared catalog stays for every
/// later test in the run, and this rebuild has already had two full-suite failures from exactly that.
/// </para>
/// <para>
/// The guards are written as <b>the wrong input that would still pass</b>, because all four defects this
/// ticket exists to prevent are silent: a level derived from a band code, a read that writes, a sync
/// that writes when nothing differs, and a historical attempt that gains a projection retroactively.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MockResultSyncServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// A certificate with two skills whose correct answers land on <b>different</b> levels while the
    /// paper's total lands on <b>one</b> band - the shape a band→level lookup cannot reproduce.
    /// <para>
    /// LIST one correct answer scales to 400 (B2 on this table); READ scales to 350 (B1). The band
    /// table calls any 750+ total <c>HIGH</c>, so "the band is HIGH, therefore the level is X" answers
    /// one level for two skills. §4.6's cut scores answer two.
    /// </para>
    /// </summary>
    private async Task<(int certId, int listSkillId, int readSkillId, int examId, int listQuestionId, int readQuestionId, int listOptionId, int readOptionId, int userId)>
        SeedAsync(string connStr, string certCode, DateOnly cutTablePublishedAt, params (string Level, int Min, int Max)[] readRows)
    {
        await using var db = DbContextFactory.Create(connStr);

        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner'), ('admin', N'Admin');
            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule)
              VALUES ('{certCode}', N'Cert {certCode}', 1, 'sum');
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1, 5, 495, 5),
              (@cert, 'READ', N'Reading',   'reading',   2, 5, 495, 5);
            DECLARE @list INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @read INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@list, @cert, 'listening', 'L1', N'Listening', 1, 1),
              (@read, @cert, 'reading',   'R1', N'Reading',   1, 2);
            DECLARE @secL INT = (SELECT SectionId FROM Certificates c JOIN Sections s ON s.CertId = c.CertId
                                 WHERE c.CertId = @cert AND s.Code = 'L1');
            DECLARE @secR INT = (SELECT SectionId FROM Certificates c JOIN Sections s ON s.CertId = c.CertId
                                 WHERE c.CertId = @cert AND s.Code = 'R1');

            -- One band covering the whole paper: a band lookup answers one level for two skills.
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours)
              VALUES (@cert, 'HIGH', N'High', 0, 990, 1, 100);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
              VALUES (@cert, N'Mock {certCode}', 'published', 120, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- LIST 1 correct -> 400; READ 1 correct -> 350.
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @list, @exam, 0, 5), (@cert, @list, @exam, 1, 400),
              (@cert, @read, @exam, 0, 5), (@cert, @read, @exam, 1, 350);

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secL, 1);
            DECLARE @grpL INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
              VALUES (@grpL, @exam, @secL, @list, N'Listening Q1', 'A2', 'mcq', 1);
            DECLARE @qL INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@qL, 'mcq', 'A', N'Right', 1), (@qL, 'mcq', 'B', N'Wrong', 0);

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secR, 2);
            DECLARE @grpR INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
              VALUES (@grpR, @exam, @secR, @read, N'Reading Q1', 'A2', 'mcq', 2);
            DECLARE @qR INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@qR, 'mcq', 'A', N'Right', 1), (@qR, 'mcq', 'B', N'Wrong', 0);

            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
              VALUES (@cert, 1, 0, N'test standard-setting study', '{cutTablePublishedAt:yyyy-MM-dd}');

            -- Listening: 400 is B2. Reading: the caller supplies its rows.
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled) VALUES
              (@cert, 1, @list, 'A2', 'cut', 110, 270),
              (@cert, 1, @list, 'B1', 'cut', 275, 395),
              (@cert, 1, @list, 'B2', 'cut', 400, 485),
              (@cert, 1, @list, 'C2', 'display_ceiling', NULL, 495);
            """);

        foreach (var (level, min, max) in readRows)
        {
            await db.Database.ExecuteSqlRawAsync($"""
                DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
                DECLARE @read INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');
                INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled)
                  VALUES (@cert, 1, @read, '{level}', 'cut', {min}, {max});
                """);
        }

        var email = $"{Guid.NewGuid():N}@test.local";
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
              VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Learner',
                      (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            DECLARE @user INT = SCOPE_IDENTITY();
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
              VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@test.local', 0x01, N'Admin', SYSDATETIMEOFFSET());
            DECLARE @admin INT = SCOPE_IDENTITY();
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
              VALUES (@user, 'admin_grant', 5, @admin, N'#222 test setup');
            """);

        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var skills = await db.Skills.Where(s => s.CertId == cert.CertId).OrderBy(s => s.DisplayOrder).ToListAsync();
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();
        var options = await db.QuestionOptions.Where(o => o.IsCorrect).ToListAsync();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        return (
            cert.CertId,
            skills[0].SkillId,
            skills[1].SkillId,
            exam.ExamId,
            questions[0].QuestionId,
            questions[1].QuestionId,
            options.Single(o => o.QuestionId == questions[0].QuestionId).OptionId,
            options.Single(o => o.QuestionId == questions[1].QuestionId).OptionId,
            userId);
    }

    /// <summary>Starts, answers both questions correctly and submits - a real completed mock.</summary>
    private static async Task<int> SitMockAsync(string connStr, int userId, int examId, int listQ, int listOpt, int readQ, int readOpt)
    {
        await using var db = DbContextFactory.Create(connStr);
        var service = new MockExamService(db, new LearnerService(db));
        var attempt = await service.StartAttemptAsync(userId, examId);
        await service.SaveAnswerAsync(userId, attempt.AttemptId, listQ, listOpt, isFlagged: false);

        // The sitting locks one skill at a time; moving to the second section is the engine's own
        // transition, not a shortcut around it.
        await service.TransitionToNextSkillAsync(userId, attempt.AttemptId);
        await service.SaveAnswerAsync(userId, attempt.AttemptId, readQ, readOpt, isFlagged: false);

        await service.SubmitAttemptAsync(userId, attempt.AttemptId, isAutoSubmit: false);
        return attempt.AttemptId;
    }

    private static MockResultSyncService CreateService(string connStr)
    {
        var db = DbContextFactory.Create(connStr);
        return new MockResultSyncService(db, new CefrProjectionService(db), new SkillStateWriter(db));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // =============================================================================================
    // The criterion the ticket names: the state table is untouched after a mock completes.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> a state table the test itself just
    /// filled, so "rows before == rows after" holds because nothing ever had a chance to change. This
    /// test seeds a row through the real writer <i>before</i> the mock, records the row's version and
    /// measurement time, sits the paper, and then compares the row field by field. A mock that wrote
    /// state through <c>SkillStateWriter</c> would move the version off 1 and re-date the measurement,
    /// which a row-count assertion alone would not notice.
    /// </summary>
    [Fact]
    public async Task Completing_a_mock_leaves_the_learner_skill_state_rows_untouched()
    {
        var dbName = $"EEMockProj_{Guid.NewGuid():N}"[..28];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Unique("MPS"), new DateOnly(2024, 1, 1),
                ("B1", 275, 360), ("B2", 400, 450));

            // A pre-existing measurement, written by the only writer allowed to write it.
            var measuredAt = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
            await using (var setupDb = DbContextFactory.Create(connStr))
            {
                var writer = new SkillStateWriter(setupDb, new FixedClock(measuredAt));
                await writer.RecordAsync(seed.userId, seed.certId, seed.listSkillId,
                    CefrLevel.A1, 10, 1.10m, LearnerSkillStateSource.Placement);
            }

            int rowsBefore;
            (CefrLevel Level, int Position, decimal Coordinate, string Source, DateTimeOffset Measured, int Version) before;
            await using (var checkDb = DbContextFactory.Create(connStr))
            {
                var rows = await checkDb.LearnerSkillStates
                    .Where(s => s.UserId == seed.userId && s.CertId == seed.certId)
                    .ToListAsync();
                rowsBefore = rows.Count;
                var row = rows.Single(s => s.SkillId == seed.listSkillId);
                before = (row.CefrLevel, row.PositionInBand, row.ContinuousCoordinate, row.Source, row.MeasuredAt, row.ProjectionVersion);
            }

            var attemptId = await SitMockAsync(connStr, seed.userId, seed.examId,
                seed.listQuestionId, seed.listOptionId, seed.readQuestionId, seed.readOptionId);

            await using var db = DbContextFactory.Create(connStr);
            var service = new MockResultSyncService(db, new CefrProjectionService(db), new SkillStateWriter(db));
            var result = await service.GetResultAsync(seed.userId, attemptId);

            // The read produced a projection (so this is not a vacuous "nothing happened" pass)...
            Assert.NotNull(result.Projected);
            Assert.Equal(ProjectionOutcome.Projected, result.Projected!.Outcome);

            await using var afterDb = DbContextFactory.Create(connStr);
            var afterRows = await afterDb.LearnerSkillStates
                .Where(s => s.UserId == seed.userId && s.CertId == seed.certId)
                .ToListAsync();

            Assert.Equal(rowsBefore, afterRows.Count);
            var after = afterRows.Single(s => s.SkillId == seed.listSkillId);
            Assert.Equal(before.Level, after.CefrLevel);
            Assert.Equal(before.Position, after.PositionInBand);
            Assert.Equal(before.Coordinate, after.ContinuousCoordinate);
            Assert.Equal(before.Source, after.Source);
            Assert.Equal(before.Measured, after.MeasuredAt);
            Assert.Equal(before.Version, after.ProjectionVersion);

            // The READ never writes, however many times it is called.
            await service.GetResultAsync(seed.userId, attemptId);
            await using var thirdDb = DbContextFactory.Create(connStr);
            Assert.Equal(rowsBefore, await thirdDb.LearnerSkillStates.CountAsync(s => s.UserId == seed.userId));
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // =============================================================================================
    // The projected level comes from the cut-score table, never from the band.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> one band (<c>HIGH</c>, 0-990) over a
    /// paper whose two skills land at 400 and 350. A band→level lookup answers a single level for the
    /// whole paper and every "the page shows a level" test still passes. The assertion that catches it
    /// is that the two per-skill levels <i>differ</i> - no total-or-band lookup can produce that - and
    /// that the weakest is the lower of the two, which is #211's rule rather than the paper's band.
    /// </summary>
    [Fact]
    public async Task The_projected_level_comes_from_the_cut_scores_so_two_skills_can_differ()
    {
        var dbName = $"EEMockProj_{Guid.NewGuid():N}"[..28];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Unique("MPD"), new DateOnly(2024, 1, 1),
                ("A2", 115, 270), ("B1", 275, 395), ("B2", 400, 450));

            var attemptId = await SitMockAsync(connStr, seed.userId, seed.examId,
                seed.listQuestionId, seed.listOptionId, seed.readQuestionId, seed.readOptionId);

            var result = await CreateService(connStr).GetResultAsync(seed.userId, attemptId);

            var listening = result.Projected!.Skills.Single(s => s.SkillId == seed.listSkillId);
            var reading = result.Projected.Skills.Single(s => s.SkillId == seed.readSkillId);

            Assert.Equal(CefrLevel.B2, listening.AwardedLevel);
            Assert.Equal(CefrLevel.B1, reading.AwardedLevel);
            Assert.NotEqual(listening.AwardedLevel, reading.AwardedLevel);

            // The paper's certificate score and band are their own facts and are both still present.
            Assert.Equal(750, result.CertificateScore);
            Assert.Equal("HIGH", result.BandCode);

            // The paper's weakest projected skill is the Reading one - the certificate's band cannot
            // name it, because the band is one row for the whole total.
            Assert.Equal(CefrLevel.B1, result.Projected.WeakestProjectedLevel);
            Assert.Equal(seed.readSkillId, result.Projected.WeakestProjectedSkillId);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // =============================================================================================
    // The gap: a score between two ranges has no level, and offers no sync.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> Reading 350 with rows <c>B1 275-345</c>
    /// and <c>B2 400-450</c>. A nearest-row fallback answers B1 (or B2) and reads entirely reasonable;
    /// the table never stated a level for 350. And the sync offer must not appear: "your level changed"
    /// is a lie when the honest answer is "this score sits between two bands".
    /// </summary>
    [Fact]
    public async Task A_score_between_two_cut_ranges_projects_no_level_and_offers_no_sync()
    {
        var dbName = $"EEMockProj_{Guid.NewGuid():N}"[..28];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Unique("MPG"), new DateOnly(2024, 1, 1),
                ("B1", 275, 345), ("B2", 400, 450));

            var attemptId = await SitMockAsync(connStr, seed.userId, seed.examId,
                seed.listQuestionId, seed.listOptionId, seed.readQuestionId, seed.readOptionId);

            var result = await CreateService(connStr).GetResultAsync(seed.userId, attemptId);

            var reading = result.Projected!.Skills.Single(s => s.SkillId == seed.readSkillId);
            Assert.Equal(ProjectionOutcome.CannotJudge, reading.Outcome);
            Assert.Null(reading.AwardedLevel);
            Assert.Equal(CefrProjection.NoCutScoreRowForLevel, reading.CannotJudgeReason);
            Assert.False(reading.CanSync);

            // A skill that cannot be judged refuses the whole paper's projection rather than answering
            // from a partial vector.
            Assert.Equal(ProjectionOutcome.CannotJudge, result.Projected.Outcome);
            Assert.False(result.SyncOffered);

            // And the sync refuses, naming why, instead of writing the skill it could judge.
            var refused = await Assert.ThrowsAsync<MockExamException>(
                () => CreateService(connStr).SyncAsync(seed.userId, attemptId));
            Assert.Equal(MockExamException.NothingToSync, refused.Code);

            await using var db = DbContextFactory.Create(connStr);
            Assert.Equal(0, await db.LearnerSkillStates.CountAsync(s => s.UserId == seed.userId));
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // =============================================================================================
    // The sync is explicit, and it writes only what differs.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> a sync that writes on every click. It
    /// looks correct - the level ends up right - but it re-dates the learner's evidence and moves the
    /// projection version for a measurement that has not changed, and a stale-looking row becomes
    /// "measured today". The assertions are the row count, the source, the version <i>and</i> the
    /// unchanged-skill's measurement time.
    /// </summary>
    [Fact]
    public async Task Sync_writes_only_the_skills_whose_projected_level_differs()
    {
        var dbName = $"EEMockProj_{Guid.NewGuid():N}"[..28];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Unique("MPS2"), new DateOnly(2024, 1, 1),
                ("A2", 115, 270), ("B1", 275, 395), ("B2", 400, 450));

            // Listening is already measured at B2 - the same level this paper projects for it. Reading
            // has no row at all. A sync that writes both is wrong; one that skips Listening is right.
            var measuredAt = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
            await using (var setupDb = DbContextFactory.Create(connStr))
            {
                await new SkillStateWriter(setupDb, new FixedClock(measuredAt)).RecordAsync(
                    seed.userId, seed.certId, seed.listSkillId, CefrLevel.B2, 20, 4.20m,
                    LearnerSkillStateSource.Placement);
            }

            var attemptId = await SitMockAsync(connStr, seed.userId, seed.examId,
                seed.listQuestionId, seed.listOptionId, seed.readQuestionId, seed.readOptionId);

            var service = CreateService(connStr);
            var before = await service.GetResultAsync(seed.userId, attemptId);
            Assert.True(before.SyncOffered);
            Assert.Equal([seed.readSkillId], before.Projected!.SkillIdsDifferingFrom(before.Stored));

            await service.SyncAsync(seed.userId, attemptId);

            await using var db = DbContextFactory.Create(connStr);
            var rows = await db.LearnerSkillStates
                .Where(s => s.UserId == seed.userId && s.CertId == seed.certId)
                .ToListAsync();

            Assert.Equal(2, rows.Count);

            var untouched = rows.Single(s => s.SkillId == seed.listSkillId);
            Assert.Equal(measuredAt, untouched.MeasuredAt);
            Assert.Equal(LearnerSkillStateSource.Placement, untouched.Source);
            Assert.Equal(1, untouched.ProjectionVersion);

            var synced = rows.Single(s => s.SkillId == seed.readSkillId);
            Assert.Equal(CefrLevel.B1, synced.CefrLevel);
            Assert.Equal(LearnerSkillStateSource.MockSection, synced.Source);

            // After the sync the offer is gone: the same comparison drives the offer and the write.
            var after = await CreateService(connStr).GetResultAsync(seed.userId, attemptId);
            Assert.False(after.SyncOffered);
            Assert.Empty(after.Projected!.SkillIdsDifferingFrom(after.Stored));
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> "a sync writes only when the level
    /// differs" tested with two <i>equal</i> levels passes vacuously - the branch is never taken. Here
    /// every skill already matches, so the correct behaviour is a refusal, and a sync that writes
    /// unconditionally moves the version while a test that only checks the final level would not see it.
    /// </summary>
    [Fact]
    public async Task Sync_refuses_and_writes_nothing_when_every_projected_level_already_matches()
    {
        var dbName = $"EEMockProj_{Guid.NewGuid():N}"[..28];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Unique("MPS3"), new DateOnly(2024, 1, 1),
                ("A2", 115, 270), ("B1", 275, 395), ("B2", 400, 450));

            var measuredAt = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
            await using (var setupDb = DbContextFactory.Create(connStr))
            {
                var writer = new SkillStateWriter(setupDb, new FixedClock(measuredAt));
                await writer.RecordAsync(seed.userId, seed.certId, seed.listSkillId,
                    CefrLevel.B2, 10, 4.10m, LearnerSkillStateSource.Placement);
                await writer.RecordAsync(seed.userId, seed.certId, seed.readSkillId,
                    CefrLevel.B1, 10, 3.10m, LearnerSkillStateSource.Placement);
            }

            var attemptId = await SitMockAsync(connStr, seed.userId, seed.examId,
                seed.listQuestionId, seed.listOptionId, seed.readQuestionId, seed.readOptionId);

            var service = CreateService(connStr);
            var result = await service.GetResultAsync(seed.userId, attemptId);
            Assert.False(result.SyncOffered);

            var refused = await Assert.ThrowsAsync<MockExamException>(
                () => service.SyncAsync(seed.userId, attemptId));
            Assert.Equal(MockExamException.NothingToSync, refused.Code);

            await using var db = DbContextFactory.Create(connStr);
            var versions = await db.LearnerSkillStates
                .Where(s => s.UserId == seed.userId)
                .Select(s => s.ProjectionVersion)
                .ToListAsync();
            Assert.All(versions, version => Assert.Equal(1, version));
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // =============================================================================================
    // A preserved historical attempt never gains a projection.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> projecting against the newest table
    /// regardless of when the attempt was sat. The result then shows a level for an attempt that was
    /// never measured against it, which is the retroactive invention TRN-05 forbids. The cut-score
    /// version here is published in 2030, so at submission time none existed; the correct answer is
    /// "historical, native score and band only, no projection".
    /// </summary>
    [Fact]
    public async Task An_attempt_that_predates_every_cut_score_version_gets_no_projection()
    {
        var dbName = $"EEMockProj_{Guid.NewGuid():N}"[..28];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Unique("MPH"), new DateOnly(2030, 1, 1),
                ("B1", 275, 395), ("B2", 400, 450));

            var attemptId = await SitMockAsync(connStr, seed.userId, seed.examId,
                seed.listQuestionId, seed.listOptionId, seed.readQuestionId, seed.readOptionId);

            var service = CreateService(connStr);
            var result = await service.GetResultAsync(seed.userId, attemptId);

            Assert.True(result.Historical);
            Assert.Null(result.Projected);
            Assert.False(result.SyncOffered);

            // Its native score and band still render: the attempt is a real fact.
            Assert.Equal(750, result.CertificateScore);
            Assert.Equal("HIGH", result.BandCode);

            var refused = await Assert.ThrowsAsync<MockExamException>(
                () => service.SyncAsync(seed.userId, attemptId));
            Assert.Equal(MockExamException.NothingToSync, refused.Code);

            await using var db = DbContextFactory.Create(connStr);
            Assert.Equal(0, await db.LearnerSkillStates.CountAsync(s => s.UserId == seed.userId));
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // =============================================================================================
    // The other two of the four answers, in the score → level direction.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> a score above every cut range on a
    /// certificate whose top row is a display ceiling. A projector that reads "the highest row's level"
    /// answers C1 (or invents C2); the table's C2 row is a ceiling and awards nothing. The assertion is
    /// null level + <c>DisplayCeiling</c> + the scale's top, which no real level can satisfy at once.
    /// </summary>
    [Fact]
    public async Task A_score_above_every_cut_range_projects_the_ceiling_and_never_a_level()
    {
        var dbName = $"EEMockProj_{Guid.NewGuid():N}"[..28];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            // Reading: B2 stops at 450, so 495 is above every range and the ceiling row answers.
            var certCode = Unique("MPC");
            var seed = await SeedAsync(connStr, certCode, new DateOnly(2024, 1, 1),
                ("B2", 400, 450));
            await using (var setup = DbContextFactory.Create(connStr))
            {
                await setup.Database.ExecuteSqlRawAsync($"""
                    DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
                    DECLARE @read INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');
                    INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled)
                      VALUES (@cert, 1, @read, 'C2', 'display_ceiling', NULL, 495);
                    """);
            }

            var projection = await new CefrProjectionService(DbContextFactory.Create(connStr))
                .ProjectScoresAsync(seed.certId, new Dictionary<int, int>
                {
                    [seed.listSkillId] = 400,
                    [seed.readSkillId] = 495,
                });

            var reading = projection.Skills.Single(s => s.SkillId == seed.readSkillId);
            Assert.Null(reading.AwardedLevel);
            Assert.Equal(ProjectionOutcome.DisplayCeiling, reading.Outcome);
            Assert.Equal(495, reading.DisplayCeiling);
            Assert.False(reading.CanSync);

            Assert.Equal(ProjectionOutcome.DisplayCeiling, projection.Outcome);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> a score inside an <c>uncertified</c>
    /// range (IELTS/VSTEP below A1) projected as that row's level. The certificate scores there and
    /// certifies nothing, so the level is null and the position is still real - the score is not a
    /// thrown-away number.
    /// </summary>
    [Fact]
    public async Task A_score_inside_an_uncertified_range_projects_a_position_and_no_level()
    {
        var dbName = $"EEMockProj_{Guid.NewGuid():N}"[..28];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var certCode = Unique("MPU");
            var seed = await SeedAsync(connStr, certCode, new DateOnly(2024, 1, 1),
                ("B1", 275, 395), ("B2", 400, 450));
            await using (var setup = DbContextFactory.Create(connStr))
            {
                await setup.Database.ExecuteSqlRawAsync($"""
                    DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
                    DECLARE @read INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');
                    INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled)
                      VALUES (@cert, 1, @read, 'pre_a1', 'uncertified', 0, 100);
                    """);
            }

            var projection = await new CefrProjectionService(DbContextFactory.Create(connStr))
                .ProjectScoresAsync(seed.certId, new Dictionary<int, int>
                {
                    [seed.listSkillId] = 400,
                    [seed.readSkillId] = 50,
                });

            var reading = projection.Skills.Single(s => s.SkillId == seed.readSkillId);
            Assert.Null(reading.AwardedLevel);
            Assert.Equal(ProjectionOutcome.Uncertified, reading.Outcome);
            Assert.Equal(50, reading.PositionInBand);
            Assert.False(reading.CanSync);

            Assert.Equal(ProjectionOutcome.Uncertified, projection.Outcome);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // =============================================================================================
    // PRN-03, structurally: the result type has nowhere to put a level on the certificate score.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> a <c>CefrLevel</c> or a
    /// "ProficiencyLevel" member added to the result beside the certificate score - the exact shape
    /// #210's <c>CertificateProjection</c> refuses. The projection type may carry per-skill levels and
    /// the weakest of them, but nothing that labels the paper's total.
    /// </summary>
    [Fact]
    public void The_certificate_score_has_no_level_member_beside_or_on_it()
    {
        var members = typeof(MockResultProjection).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("Level", members, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CefrLevel", members, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("AwardedLevel", members, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProficiencyLevel", members, StringComparer.OrdinalIgnoreCase);

        // And the read model cannot reach a writer: a read that can write is the defect this ticket
        // exists to prevent.
        Assert.DoesNotContain(typeof(MockResultProjection).GetProperties(),
            p => p.PropertyType == typeof(ISkillStateWriter));
    }
}
