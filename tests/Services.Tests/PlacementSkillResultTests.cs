using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>
/// #212: the per-skill placement result, driven through the real services to each of the five
/// statuses the ticket names.
///
/// <b>The tests drive a real placement to each status and assert the derivation.</b> A test that only
/// asserted the enum's five members exist would pass against a writer that hardcoded `measured` on
/// every row, because every row would be a legal value. Each case below therefore creates the
/// condition that MUST produce the status - a pending grading, a failed one, an unanswered skill, a
/// timed-out sitting - and then reads back the row the writer produced.
///
/// Isolation: every test seeds its own certificate, learner, exam and attempt, and runs against a
/// database created for this class alone and dropped afterwards. Seeding the shared catalog has
/// broken the full suite three times in this rebuild.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementSkillResultTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EE212_{Guid.NewGuid():N}"[..24];
    private string _connectionString = null!;

    public PlacementSkillResultTests(DatabaseFixture database) => _database = database;

    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    /// <summary>
    /// Runs a batch against THIS class's database, not the shared fixture catalog. The fixture's own
    /// <c>SqlAsync</c> targets the run-wide catalog, which is exactly the isolation mistake the ticket
    /// warns about: a seed there would leak into every other test class.
    /// </summary>
    private Task SqlAsync(string batch) =>
        _database.SqlCmdFileToDatabaseAsync(_dbName, WriteBatch(batch));

    private static string WriteBatch(string batch)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ee212-{Guid.NewGuid():N}.sql");
        File.WriteAllText(path, batch);
        return path;
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    // ---------------------------------------------------------------------------------------------
    // fixtures
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <summary>
    /// A certificate with the named skills, one section per skill, a band table, a cut-score table and
    /// a placement exam with one position per skill (two for a productive one). Returns the ids the
    /// tests drive.
    /// <para>
    /// #269: the paper is authored on the pool - a stimulus-less item per position, bound to the
    /// section. The legacy QuestionGroups/Questions/QuestionOptions rows this used to insert are the
    /// tables being dropped.
    /// </para>
    /// </summary>
    private async Task<Fixture> SeedAsync(string code, params (string Code, string Modality, string QuestionType)[] skills)
    {
        await _database.SqlCmdFileToDatabaseAsync(_dbName, WriteScript(code, skills));

        await using var db = Db();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync();
        var skillIds = await db.Skills.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code, s => s.SkillId);

        foreach (var (skillCode, modality, questionType) in skills)
        {
            var sectionId = await db.Sections
                .Where(s => s.CertId == certId && s.SkillId == skillIds[skillCode])
                .Select(s => s.SectionId).SingleAsync();

            if (questionType == "free_response")
            {
                // D6: a productive skill gets TWO tasks. The second is what makes "estimated from one
                // task" a real state rather than a hypothetical one - with one task, the first grading
                // would be a full measurement and the estimated status unreachable.
                await PoolFixture.AddProductiveAsync(
                    db, examId, certId, sectionId, modality, $"Q{skillCode}", displayOrder: 1);
                await PoolFixture.AddProductiveAsync(
                    db, examId, certId, sectionId, modality, $"Q{skillCode}b", displayOrder: 2);
            }
            else
            {
                await PoolFixture.AddMcqAsync(
                    db, examId, certId, sectionId, modality, $"Q{skillCode}", displayOrder: 1);
            }
        }

        return await ReadFixtureAsync(code, examId);
    }

    /// <summary>
    /// The fixture as a .sql file rather than one <c>-Q</c> batch, because it declares variables that a
    /// later batch in the same script refers to (<c>GO</c> would split them, so it is one batch and one
    /// file). The exam is declared first: the sections reference it.
    /// </summary>
    private static string WriteScript(string code, (string Code, string Modality, string QuestionType)[] skills)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ee212-{Guid.NewGuid():N}.sql");
        var lines = new List<string>
        {
            $"INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Cert {code}', 1);",
            "DECLARE @cert INT = SCOPE_IDENTITY();",
            "INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)",
            "  VALUES (@cert, N'Placement', 'published', 30, 'placement', 90);",
        };

        var index = 0;
        foreach (var (skillCode, modality, questionType) in skills)
        {
            index++;
            lines.Add($"""
                INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
                  VALUES (@cert, '{skillCode}', N'{skillCode}', '{modality}', {index}, 0, 100, 1);
                INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder)
                  VALUES ((SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = '{skillCode}'),
                          @cert, '{modality}', 'S{index}', N'Section {index}', {(questionType == "free_response" ? 2 : 1)}, {index});
                """);
        }

        lines.Add("""
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A2', N'Elementary', 0, 200, 1), (@cert, 'B1', N'Intermediate', 201, 500, 2);
            -- #259 / PRN-03: the cut-score table is what places a score, and it must span BOTH scales a
            -- paper can produce: an objective skill's scaled score (0-500) and a productive skill's
            -- rubric total, which the writer stores on the 0-10 rubric scale. The A2 row starts at 0 so
            -- a graded task lands in a range rather than being judged "no cut row".
            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
              VALUES (@cert, 1, 0, N'fixture', '2026-01-01');
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled)
              SELECT @cert, 1, SkillId, 'A2', 'cut', 0, 200 FROM Skills WHERE CertId = @cert;
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled)
              SELECT @cert, 1, SkillId, 'B1', 'cut', 201, 500 FROM Skills WHERE CertId = @cert;
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @cert, SkillId, NULL, 0, 0 FROM Skills WHERE CertId = @cert;
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @cert, SkillId, NULL, 1, 250 FROM Skills WHERE CertId = @cert;
            """);

        File.WriteAllText(path, string.Join("\n", lines));
        return path;
    }

    private async Task<Fixture> ReadFixtureAsync(string code, int examId)
    {
        await using var db = Db();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var skillIds = await db.Skills.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code, s => s.SkillId);

        // #269: the paper is the roster, so a position is an ExamItemId and its skill is the section's.
        var positions = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == examId)
            .Join(db.Sections.AsNoTracking(), i => i.SectionId, s => s.SectionId, (i, s) => new { i, s })
            .Join(db.Skills.AsNoTracking(), x => x.s.SkillId, sk => sk.SkillId, (x, sk) => new { x.i, Code = sk.Code })
            .OrderBy(x => x.i.DisplayOrder)
            .Select(x => new { Code = x.Code, x.i.ExamItemId, x.i.DisplayOrder })
            .ToListAsync();

        var first = positions
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DisplayOrder).First().ExamItemId);
        var second = positions
            .GroupBy(x => x.Code)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DisplayOrder).ElementAt(1).ExamItemId);

        return new Fixture(certId, examId, skillIds, first, second);
    }

    private sealed record Fixture(
        int CertId,
        int ExamId,
        IReadOnlyDictionary<string, int> SkillIds,
        IReadOnlyDictionary<string, int> PositionIds,
        IReadOnlyDictionary<string, int> SecondPositionIds);

    private async Task<int> SeedLearnerAsync(string certCode)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01,
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            """);
        await using var db = Db();
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private PlacementResultWriter Writer(CompanionDbContext db) => new(db);

    /// <summary>
    /// Answers one objective position with the label its pinned version marks correct. #269: a pool
    /// row is addressed by <c>ExamItemId</c> and records <c>SelectedLabel</c> - the version stores
    /// labels A-D, not option rows, so there is no legacy option id to point at.
    /// </summary>
    private async Task AnswerCorrectlyAsync(int attemptId, int examItemId)
    {
        await using var db = Db();
        var answer = await db.AttemptAnswers
            .SingleAsync(a => a.AttemptId == attemptId && a.ExamItemId == examItemId);
        answer.SelectedLabel = "A";
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Starts a placement through the real service, so the attempt's answer sheet exists. A hand-written
    /// attempt row would carry no <c>AttemptAnswers</c>, and the objective half would then read as
    /// "nothing attempted" - a fixture producing the very state the test means to observe.
    /// </summary>
    private async Task<int> StartPlacementAsync(int userId, Fixture fixture)
    {
        await using var db = Db();
        var service = new PlacementService(db, new LearnerService(db));
        var info = await service.StartPlacementTestAsync(userId, fixture.ExamId);
        return info.PlacementId;
    }

    // ---------------------------------------------------------------------------------------------
    // the five statuses, each derived from a real condition
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The objective half: every answered objective skill is `measured` with a level, a position and a
    /// coordinate, and the placement is done. This is the baseline the other four differ from.
    /// </summary>
    [Fact]
    public async Task An_answered_objective_skill_is_measured_and_carries_a_level_position_and_coordinate()
    {
        var code = Unique("T212A");
        var fixture = await SeedAsync(code, ("LIST", "listening", "mcq"), ("READ", "reading", "mcq"));
        var userId = await SeedLearnerAsync(code);
        var placementId = await StartPlacementAsync(userId, fixture);

        var attemptId = await AttemptIdAsync(placementId);
        foreach (var skill in new[] { "LIST", "READ" })
        {
            await AnswerCorrectlyAsync(attemptId, fixture.PositionIds[skill]);
        }

        await using (var engineDb = Db())
        {
            var engine = new AttemptEngine(engineDb);
            await engine.SubmitAttemptAsync(attemptId, userId, isAutoSubmit: false);
        }

        await using var db = Db();
        var rows = await db.PlacementSkillResults.AsNoTracking()
            .Where(r => r.PlacementId == placementId).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(PlacementResultStatus.Measured, r.ResultStatus);
            Assert.NotNull(r.CefrLevel);
            Assert.InRange(r.PositionInBand!.Value, 0, 100);
            Assert.InRange(r.ContinuousCoordinate!.Value, 1.00m, 6.99m);
            Assert.NotNull(r.MeasuredAt);
            Assert.Equal(r.MeasuredAt.Value.AddDays(90), r.ExpiresAt);
            Assert.Null(r.FailureReason);
            Assert.False(r.IsEstimatedFromOneTask);
            Assert.Equal(DerivedFactWriters.PlacementSkillResult, r.WrittenByService);
        });

        var placement = await db.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal("done", placement.Status);
    }

    /// <summary>
    /// #259 / PRN-03: <b>a band is not a CEFR level.</b> A score the cut-score table cannot place must
    /// not acquire a level from its certificate's band name.
    /// <para>
    /// <b>The measured trigger.</b> The seeded TOEIC cut table has real gaps — 56–59, 106–109, 271–274,
    /// 396–399, 486–489 — and 396–399 sits inside band <c>A2</c> (225–545). So a learner scoring 397 on
    /// Listening hits no cut row, and <c>PlacementResultWriter</c> used to call
    /// <c>LevelFromBand("A2")</c>, which succeeds because the band code is spelled like a level. The row
    /// then reads "measured, A2" for a score the table never placed.
    /// </para>
    /// <para>
    /// <b>The wrong input this refuses.</b> A test asserting "the level is not null" passes on the
    /// fabricated A2. A test asserting "the position is in 0–100" passes too. The only assertion that
    /// distinguishes a placed level from a band wearing a level's name is that an unplaceable score
    /// carries <b>no level at all</b> and a reason saying why.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_score_inside_a_cut_score_gap_does_not_borrow_the_band_name_as_its_level()
    {
        var code = Unique("T259A");
        var fixture = await SeedAsync(code, ("LIST", "listening", "mcq"));
        var userId = await SeedLearnerAsync(code);
        var skillId = fixture.SkillIds["LIST"];

        // A cut table with a hole around the score the engine will produce. The scale maps raw 1 -> 250,
        // so a learner answering the single item correctly scores 250; the only cut row covers 400-500,
        // and the band table puts 250 in band 'B1'. That is the trap: the band code is spelled exactly
        // like a level, so a reader that parses it reports CEFR B1 for a score no cut row placed.
        await SqlAsync($"""
            -- The shared fixture seeds a complete table; this test needs the GAP, so that table goes.
            DELETE FROM CefrCutScores WHERE CertId = {fixture.CertId};
            DELETE FROM CefrCutScoreVersions WHERE CertId = {fixture.CertId};
            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
              VALUES ({fixture.CertId}, 1, 0, N'gapped', '2026-01-01');
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled)
              VALUES ({fixture.CertId}, 1, {skillId}, 'C1', 'cut', 400, 500);
            """);

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await AttemptIdAsync(placementId);

        await AnswerCorrectlyAsync(attemptId, fixture.PositionIds["LIST"]);

        // One writer pass, through the real engine. The gap is present from the start, so no done
        // placement is ever flipped - the guard in trg_PlacementSkillResults_CompletionIsEarned exists
        // to stop exactly that, and a fixture that provoked it would be testing the guard, not this.
        await using (var db = Db())
        {
            await new AttemptEngine(db).SubmitAttemptAsync(attemptId, userId, isAutoSubmit: false);
        }

        await using (var db = Db())
        {
            await Writer(db).WriteAsync(placementId);
        }

        await using var verify = Db();
        var row = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == skillId);

        // No cut row covers 250, so there is no level. 'B1' here would be the certificate's band name
        // worn as a CEFR level - the crosswalk AGENTS.md §9 forbids without an argued case.
        Assert.Null(row.CefrLevel);
        Assert.Null(row.PositionInBand);
        Assert.Null(row.ContinuousCoordinate);
        Assert.Equal(PlacementResultStatus.NotMeasured, row.ResultStatus);
        Assert.Equal(PlacementFailureReason.NoCutScoreRow, row.FailureReason);

        // The band itself is still a real fact about the attempt, and it is recorded as a band.
        var attempt = await verify.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
        Assert.Equal("B1", attempt.BandCode);
        Assert.NotEqual(PlacementResultStatus.Measured, row.ResultStatus);
    }

    /// <summary>
    /// #259 / PRN-03, the sibling case of the gap above: <b>a certificate that publishes no cut-score
    /// table at all.</b> <c>TOEIC_CAT</c> is the deployed example - it has bands and scales but ships no
    /// table - and the pre-#259 writer reported every learner who sat it as <c>PreA1</c>, because
    /// <see cref="CefrLevel"/>'s default is the floor and the level came from a failed parse.
    /// <para>
    /// <b>Why this is a separate test from the gap.</b> The gap exercises a table that exists and does
    /// not cover the score; this exercises the table's absence. Both route through the same branch now,
    /// but they arrived there by different paths before, and a fix that special-cased "empty projection
    /// result" while still reading a table version would pass only one of them.
    /// </para>
    /// <para>
    /// <b>What would pass while being wrong:</b> asserting <c>CefrLevel is null</c> alone, which a row
    /// that was never written also satisfies. The status and the reason are asserted too, and the
    /// placement is asserted to still have a result to show - the skill is unmeasured, not missing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_certificate_with_no_cut_score_table_reports_no_level_rather_than_the_floor()
    {
        var code = Unique("T259B");
        var fixture = await SeedAsync(code, ("LIST", "listening", "mcq"));
        var userId = await SeedLearnerAsync(code);
        var skillId = fixture.SkillIds["LIST"];

        // No table, no rows: the shape TOEIC_CAT ships in today.
        await SqlAsync($"""
            DELETE FROM CefrCutScores WHERE CertId = {fixture.CertId};
            DELETE FROM CefrCutScoreVersions WHERE CertId = {fixture.CertId};
            """);

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await AttemptIdAsync(placementId);

        await AnswerCorrectlyAsync(attemptId, fixture.PositionIds["LIST"]);

        await using (var engineDb = Db())
        {
            await new AttemptEngine(engineDb).SubmitAttemptAsync(attemptId, userId, isAutoSubmit: false);
        }

        await using (var writerDb = Db())
        {
            await Writer(writerDb).WriteAsync(placementId);
        }

        await using var verify = Db();
        var row = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == skillId);

        // PreA1 is a claim about the learner. A missing table is a claim about the content.
        Assert.Null(row.CefrLevel);
        Assert.NotEqual(CefrLevel.PreA1, row.CefrLevel);
        Assert.Equal(PlacementResultStatus.NotMeasured, row.ResultStatus);
        Assert.Equal(PlacementFailureReason.NoCutScoreRow, row.FailureReason);
        Assert.Null(row.CutScoreTableVersion);

        // The row exists, so the result screen has something to explain. "No level" is not "no result".
        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.NotEqual("done", placement.Status);
    }

    /// <summary>
    /// A productive skill whose grading is still pending is `grading`, and the placement stays in
    /// progress. This is the ticket's sixth criterion and the confirmed defect's shape: before #212 the
    /// row was `done` with a null result.
    ///
    /// <b>The input that would pass a weaker test:</b> a writer that mapped "no score" to
    /// `not_measured` produces a row this test's first assertion rejects, and one that mapped it to
    /// `measured` produces a row the second rejects.
    /// </summary>
    [Fact]
    public async Task A_pending_productive_task_leaves_the_placement_in_progress_with_a_grading_row()
    {
        var code = Unique("T212B");
        var fixture = await SeedAsync(code, ("LIST", "listening", "mcq"), ("WRIT", "writing", "free_response"));
        var userId = await SeedLearnerAsync(code);

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await AttemptIdAsync(placementId);

        // the objective half is answered, then the sitting crosses into the productive skill
        await AnswerCorrectlyAsync(attemptId, fixture.PositionIds["LIST"]);
        await AdvanceToSkillOfAsync(attemptId, fixture.PositionIds["WRIT"]);

        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SaveDraftAsync(userId, placementId, fixture.PositionIds["WRIT"],
                "A written response that is long enough to grade.");
        }

        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using var verify = Db();
        var writing = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.SkillIds["WRIT"]);

        Assert.Equal(PlacementResultStatus.Grading, writing.ResultStatus);
        Assert.Null(writing.CefrLevel);
        Assert.Null(writing.FailureReason);          // queued is not failed
        Assert.Null(writing.MeasuredAt);

        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal("in_progress", placement.Status);
        Assert.Null(placement.CompletionFailureReason);

        // the objective half is already visible: results appear progressively (PLC-34)
        var listening = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.SkillIds["LIST"]);
        Assert.Equal(PlacementResultStatus.Measured, listening.ResultStatus);
    }

    /// <summary>
    /// Every productive task failed: `not_measured` with <c>ai_grading_failed</c>. Treating an AI
    /// failure as A1 is the fabricated result PLC-35 refuses.
    /// </summary>
    [Fact]
    public async Task A_failed_grading_is_not_measured_with_the_grading_failure_reason()
    {
        var code = Unique("T212C");
        var fixture = await SeedAsync(code, ("WRIT", "writing", "free_response"));
        var userId = await SeedLearnerAsync(code);

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await SubmitWithDraftAsync(userId, placementId, fixture.PositionIds["WRIT"]);

        // the worker ran and the model failed on BOTH tasks this productive skill carries
        await SqlAsync($"""
            UPDATE AiGradings SET Status = 'failed', CompletedAt = SYSDATETIMEOFFSET(), FailureReason = N'The model timed out.'
              WHERE FreeResponseId IN (SELECT FreeResponseId FROM FreeResponses WHERE AttemptId = {attemptId});
            UPDATE FreeResponses SET Status = 'graded' WHERE AttemptId = {attemptId};
            """);

        await using (var db = Db())
        {
            await Writer(db).WriteAsync(placementId);
        }

        await using var verify = Db();
        var row = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.SkillIds["WRIT"]);

        Assert.Equal(PlacementResultStatus.NotMeasured, row.ResultStatus);
        Assert.Equal(PlacementFailureReason.AiGradingFailed, row.FailureReason);
        Assert.Null(row.CefrLevel);

        // nothing measured anywhere: #217 / PLC-19 records this as ABANDONED with the reason, not done.
        // `done` was #212's shape and it rendered a result page whose every skill was not-measured;
        // the ticket asks for the sitting to be abandoned with no result instead.
        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal("abandoned", placement.Status);
        Assert.Equal(PlacementFailureReason.AiGradingFailed, placement.CompletionFailureReason);
        Assert.Null(placement.TotalScore);
        Assert.Null(placement.ResultBandCode);
    }

    /// <summary>
    /// The learner skipped a skill: `not_measured` with <c>candidate_skipped</c>, and the placement is
    /// still a partial result (PLC-19) rather than abandoned.
    /// </summary>
    [Fact]
    public async Task A_skipped_skill_is_not_measured_with_the_candidate_skipped_reason()
    {
        var code = Unique("T212D");
        var fixture = await SeedAsync(code, ("LIST", "listening", "mcq"), ("READ", "reading", "mcq"));
        var userId = await SeedLearnerAsync(code);

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await AttemptIdAsync(placementId);

        // one skill answered, the other untouched
        await AnswerCorrectlyAsync(attemptId, fixture.PositionIds["LIST"]);

        await using (var db = Db())
        {
            await new AttemptEngine(db).SubmitAttemptAsync(attemptId, userId, isAutoSubmit: false);
        }

        await using var verify = Db();
        var skipped = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.SkillIds["READ"]);

        Assert.Equal(PlacementResultStatus.NotMeasured, skipped.ResultStatus);
        Assert.Equal(PlacementFailureReason.CandidateSkipped, skipped.FailureReason);
        Assert.Null(skipped.CefrLevel);

        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal("done", placement.Status);
        Assert.Null(placement.CompletionFailureReason);   // one skill was measured: a partial result

        var measured = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.SkillIds["LIST"]);
        Assert.Equal(PlacementResultStatus.Measured, measured.ResultStatus);
    }

    /// <summary>
    /// The sitting timed out with nothing attempted: <c>inactivity_timeout</c>, not
    /// <c>candidate_skipped</c>. The two are different facts and the ticket asks them to be told apart.
    /// </summary>
    [Fact]
    public async Task A_timed_out_sitting_reports_the_timeout_reason_not_a_skip()
    {
        var code = Unique("T212E");
        var fixture = await SeedAsync(code, ("LIST", "listening", "mcq"));
        var userId = await SeedLearnerAsync(code);

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await AttemptIdAsync(placementId);

        await using (var db = Db())
        {
            await new AttemptEngine(db).SubmitAttemptAsync(attemptId, userId, isAutoSubmit: true);
        }

        await using var verify = Db();
        var row = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.SkillIds["LIST"]);

        Assert.Equal(PlacementResultStatus.NotMeasured, row.ResultStatus);
        Assert.Equal(PlacementFailureReason.InactivityTimeout, row.FailureReason);

        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal(PlacementFailureReason.InactivityTimeout, placement.CompletionFailureReason);
    }

    /// <summary>
    /// One of two productive tasks graded: `estimated_from_one_task`, and the boolean agrees with the
    /// status. D7: one task is not nothing, and calling it a full measurement would overstate it.
    /// </summary>
    [Fact]
    public async Task One_graded_task_of_two_is_estimated_from_one_task()
    {
        var code = Unique("T212F");
        var fixture = await SeedAsync(code, ("WRIT", "writing", "free_response"));
        var userId = await SeedLearnerAsync(code);

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await SubmitWithDraftAsync(userId, placementId, fixture.PositionIds["WRIT"]);
        var skillId = fixture.SkillIds["WRIT"];

        // D6: two tasks on the one writing skill. The engine pre-creates one draft per free-response
        // position when the sitting starts, so the second task's row already exists - it is the GRADING
        // that is inserted here. Had this fixture carried one task, the first grading would have been a
        // full measurement and the estimated status unreachable.
        await SqlAsync($"""
            DECLARE @fr1 INT = (SELECT FreeResponseId FROM FreeResponses
                                WHERE AttemptId = {attemptId} AND ExamItemId = {fixture.PositionIds["WRIT"]});
            UPDATE AiGradings SET Status = 'done', CompletedAt = SYSDATETIMEOFFSET(), OverallScaled = 4.5
              WHERE FreeResponseId = @fr1;
            UPDATE FreeResponses SET Status = 'graded', SubmittedAt = SYSDATETIMEOFFSET()
              WHERE FreeResponseId = @fr1;

            DECLARE @fr2 INT = (SELECT FreeResponseId FROM FreeResponses
                                WHERE AttemptId = {attemptId} AND ExamItemId = {fixture.SecondPositionIds["WRIT"]});
            UPDATE FreeResponses SET Status = 'graded', SubmittedAt = SYSDATETIMEOFFSET(),
                   ResponseText = N'Second response.' WHERE FreeResponseId = @fr2;
            UPDATE AiGradings SET Status = 'failed', CompletedAt = SYSDATETIMEOFFSET(), FailureReason = N'The model timed out.'
              WHERE FreeResponseId = @fr2;
            """);

        // The engine scored nothing at submit time because both tasks were ungraded then, so there is no
        // AttemptSkillScores row to project. The one graded task is worth 4.5 on the rubric scale, which
        // the engine would store as 5; writing it is what a real settled grading produces. The attempt's
        // writer declaration is in the statement (ADR-0012 / PRN-01), and the total is nulled first
        // because trg_Attempts_TotalMatchesSkills compares a non-null total against the skill rows.
        await SqlAsync($"""
            UPDATE ExamAttempts
               SET TotalScore = NULL, WrittenByService = 'AttemptEngine',
                   ProjectionVersion = ProjectionVersion + 1
             WHERE AttemptId = {attemptId};
            INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore)
              VALUES ({attemptId}, {skillId}, {fixture.CertId}, 5, 5);
            UPDATE ExamAttempts
               SET TotalScore = 5, WrittenByService = 'AttemptEngine',
                   ProjectionVersion = ProjectionVersion + 1
             WHERE AttemptId = {attemptId};
            """);

        await using (var db = Db())
        {
            await Writer(db).WriteAsync(placementId);
        }

        await using var verify = Db();
        var row = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == skillId);

        Assert.Equal(PlacementResultStatus.EstimatedFromOneTask, row.ResultStatus);
        Assert.True(row.IsEstimatedFromOneTask);
        Assert.NotNull(row.CefrLevel);
        Assert.Null(row.FailureReason);

        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal("done", placement.Status);
        Assert.Null(placement.CompletionFailureReason);
    }

    /// <summary>
    /// The cut-score version is stored on the row AND is the version the projection was computed from.
    /// Two versions with different ranges: the score recorded must be reproducible from the version the
    /// row names, which is what #222 could not do at read time.
    ///
    /// <b>The input that would pass a weaker test:</b> a row that stored `TableVersion = 1` while
    /// projecting against version 2 passes a "the column is populated" assertion. This one recomputes
    /// from the stored version and compares the number.
    /// </summary>
    [Fact]
    public async Task The_stored_cut_score_version_is_the_one_the_projection_read()
    {
        var code = Unique("T212G");
        var fixture = await SeedAsync(code, ("LIST", "listening", "mcq"));
        var userId = await SeedLearnerAsync(code);
        var skillId = fixture.SkillIds["LIST"];

        // two versions with different B1 ranges, so the same position yields different scores. The
        // fixture now seeds version 1 itself (#259), so it is cleared first and rebuilt here.
        await SqlAsync($"""
            DELETE FROM CefrCutScores WHERE CertId = {fixture.CertId};
            DELETE FROM CefrCutScoreVersions WHERE CertId = {fixture.CertId};
            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
              VALUES ({fixture.CertId}, 1, 0, N'v1', '2026-01-01'), ({fixture.CertId}, 2, 0, N'v2', '2026-02-01');
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled)
              VALUES ({fixture.CertId}, 1, {skillId}, 'B1', 'cut', 200, 300),
                     ({fixture.CertId}, 2, {skillId}, 'B1', 'cut', 250, 350);
            """);

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await AttemptIdAsync(placementId);
        await AnswerCorrectlyAsync(attemptId, fixture.PositionIds["LIST"]);

        await using (var db = Db())
        {
            await new AttemptEngine(db).SubmitAttemptAsync(attemptId, userId, isAutoSubmit: false);
        }

        await using var verify = Db();
        var row = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == skillId);

        // the newest published table is the one a fresh projection wants
        Assert.Equal(2, row.CutScoreTableVersion);
        Assert.NotNull(row.ProjectedScore);

        // recompute from the version the row names, the way CefrProjectionService.Interpolate does
        var cut = await verify.CefrCutScores.AsNoTracking().SingleAsync(c =>
            c.CertId == fixture.CertId && c.TableVersion == row.CutScoreTableVersion
            && c.SkillId == skillId && c.CefrLevel == row.CefrLevel!.Value.Code);
        var expected = cut.MinScaled!.Value
            + (int)Math.Round((cut.MaxScaled - cut.MinScaled!.Value) * Math.Clamp(row.PositionInBand!.Value, 0, 100) / 100m,
                MidpointRounding.AwayFromZero);
        Assert.Equal(expected, row.ProjectedScore);

        // and version 1 would have produced a different number, so this is not a coincidence
        var v1 = await verify.CefrCutScores.AsNoTracking().SingleAsync(c =>
            c.CertId == fixture.CertId && c.TableVersion == 1 && c.SkillId == skillId && c.RowKind == "cut");
        var fromV1 = v1.MinScaled!.Value
            + (int)Math.Round((v1.MaxScaled - v1.MinScaled!.Value) * Math.Clamp(row.PositionInBand!.Value, 0, 100) / 100m,
                MidpointRounding.AwayFromZero);
        Assert.NotEqual(fromV1, row.ProjectedScore);
    }

    /// <summary>
    /// A second write after grading settles promotes `grading` to `measured` and finishes the placement.
    /// This is the whole free-response defect's path: submit leaves it in progress, the worker finishes
    /// it. It also proves the writer is idempotent rather than insert-only.
    /// </summary>
    [Fact]
    public async Task A_settled_grading_turns_the_grading_row_into_a_measurement_and_finishes_the_placement()
    {
        var code = Unique("T212H");
        var fixture = await SeedAsync(code, ("WRIT", "writing", "free_response"));
        var userId = await SeedLearnerAsync(code);
        var skillId = fixture.SkillIds["WRIT"];

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await AttemptIdAsync(placementId);

        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SaveDraftAsync(userId, placementId, fixture.PositionIds["WRIT"],
                "A written response that is long enough to grade.");
            // #217 / PLC-35: BOTH tasks this paper carries must be answered for the settled result to be
            // a full measurement. It used to be one: the blank second task was given a grading row of its
            // own at submission, and "2 of 2 gradings done" read as `measured`. Counting the grading
            // rows rather than the tasks the paper DELIVERED is exactly the fabricated denominator D7
            // forbids, so the fixture now answers both - which is what this test's name claims.
            await service.SaveDraftAsync(userId, placementId, fixture.SecondPositionIds["WRIT"],
                "A second written response, also long enough to be graded.");
        }

        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using (var settled = Db())
        {
            // both tasks this productive skill carries must settle before Finalize accepts the attempt
            await settled.AiGradings
                .Where(g => g.FreeResponse.AttemptId == attemptId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.Status, AiGradingStatus.Done.ToCode())
                    .SetProperty(g => g.OverallScaled, 4.5m)
                    .SetProperty(g => g.CompletedAt, DateTimeOffset.UtcNow));
            await settled.FreeResponses.Where(fr => fr.AttemptId == attemptId)
                .ExecuteUpdateAsync(s => s.SetProperty(fr => fr.Status, FreeResponseStatus.Graded.ToCode()));

            await new AttemptEngine(settled).FinalizeAiGradedAttemptAsync(attemptId);
        }

        await using var verify = Db();
        var row = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == skillId);
        Assert.Equal(PlacementResultStatus.Measured, row.ResultStatus);
        Assert.NotNull(row.CefrLevel);

        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal("done", placement.Status);
        Assert.Null(placement.CompletionFailureReason);
    }

    /// <summary>
    /// A second write of the same result does not re-measure it into a different level. The measurement
    /// time does move forward - AttemptEngine's derive-and-write is not "reuse", which is #213's job -
    /// but a rewrite that produced a different level from the same evidence would be the two-writers bug
    /// in a new place.
    /// </summary>
    [Fact]
    public async Task A_second_write_of_the_same_result_keeps_the_same_level()
    {
        var code = Unique("T212I");
        var fixture = await SeedAsync(code, ("LIST", "listening", "mcq"));
        var userId = await SeedLearnerAsync(code);
        var skillId = fixture.SkillIds["LIST"];

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await AttemptIdAsync(placementId);
        await AnswerCorrectlyAsync(attemptId, fixture.PositionIds["LIST"]);

        await using (var db = Db())
        {
            await new AttemptEngine(db).SubmitAttemptAsync(attemptId, userId, isAutoSubmit: false);
        }

        PlacementSkillResult first;
        await using (var db = Db())
        {
            first = await db.PlacementSkillResults.AsNoTracking()
                .SingleAsync(r => r.PlacementId == placementId && r.SkillId == skillId);
        }

        await using (var db = Db())
        {
            await Writer(db).WriteAsync(placementId);
        }

        await using var verify = Db();
        var second = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == skillId);
        Assert.Equal(PlacementResultStatus.Measured, second.ResultStatus);
        Assert.Equal(first.CefrLevel, second.CefrLevel);
        Assert.Equal(first.PositionInBand, second.PositionInBand);
        Assert.Equal(first.ProjectedScore, second.ProjectedScore);
        Assert.Equal(first.CutScoreTableVersion, second.CutScoreTableVersion);
        // the version moved forward, which is what a legitimate re-derivation does
        Assert.True(second.ProjectionVersion > first.ProjectionVersion);
    }

    /// <summary>
    /// The sitting's one-way skill lock: a placement paper serves one skill's positions until the
    /// learner crosses into the next. This advances the attempt to the skill holding
    /// <paramref name="examItemId"/>, which is what the take screen does when the objective half is
    /// finished. Without it, writing to a later skill's position is refused - correctly.
    /// </summary>
    private Task AdvanceToSkillOfAsync(int attemptId, int examItemId) =>
        SqlAsync($"""
            UPDATE a
            SET a.ActiveSkillId = (SELECT s.SkillId
                                   FROM ExamItems ei JOIN Sections s ON s.SectionId = ei.SectionId
                                   WHERE ei.ExamItemId = {examItemId}),
                a.PhaseDeadlineAt = DATEADD(minute, 30, GETUTCDATE())
            FROM ExamAttempts a
            WHERE a.AttemptId = {attemptId};
            """);

    /// <summary>
    /// Submits the placement with one written draft, which is what creates the attempt's free-response
    /// row and its pending AiGrading. Returns the attempt id.
    /// </summary>
    private async Task<int> SubmitWithDraftAsync(int userId, int placementId, int examItemId)
    {
        var attemptId = await AttemptIdAsync(placementId);
        await AdvanceToSkillOfAsync(attemptId, examItemId);

        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SaveDraftAsync(userId, placementId, examItemId,
                "A written response that is long enough to grade.");
        }

        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        return attemptId;
    }

    private async Task<int> AttemptIdAsync(int placementId)
    {
        await using var db = Db();
        return await db.PlacementTests.Where(p => p.PlacementId == placementId)
            .Select(p => p.AttemptId!.Value).SingleAsync();
    }
}
