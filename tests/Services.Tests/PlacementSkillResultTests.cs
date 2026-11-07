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
    /// A certificate with the named skills, one section and one question per skill, a band table, and a
    /// placement exam with one question per skill. Returns the ids the tests drive.
    /// </summary>
    private async Task<Fixture> SeedAsync(string code, params (string Code, string Modality, string QuestionType)[] skills)
    {
        await _database.SqlCmdFileToDatabaseAsync(_dbName, WriteScript(code, skills));
        return await ReadFixtureAsync(code);
    }

    /// <summary>
    /// The fixture as a .sql file rather than one <c>-Q</c> batch, because it declares variables that a
    /// later batch in the same script refers to (<c>GO</c> would split them, so it is one batch and one
    /// file). The exam is declared first: the questions reference it.
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
            "DECLARE @exam INT = SCOPE_IDENTITY();",
        };

        var index = 0;
        foreach (var (skillCode, modality, questionType) in skills)
        {
            index++;
            lines.Add($"""
                INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
                  VALUES (@cert, '{skillCode}', N'{skillCode}', '{modality}', {index}, 0, 100, 1);
                DECLARE @{skillCode} INT = SCOPE_IDENTITY();
                INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder)
                  VALUES (@{skillCode}, @cert, '{modality}', 'S{index}', N'Section {index}', {(questionType == "free_response" ? 2 : 1)}, {index});
                DECLARE @sec{skillCode} INT = SCOPE_IDENTITY();
                INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec{skillCode}, {index});
                DECLARE @grp{skillCode} INT = SCOPE_IDENTITY();
                INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
                  VALUES (@grp{skillCode}, @exam, @sec{skillCode}, @{skillCode}, N'Q{index}', 'B1', '{questionType}', {index});
                DECLARE @q{skillCode} INT = SCOPE_IDENTITY();
                """);

            if (questionType == "mcq")
            {
                lines.Add($"""
                INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect)
                  VALUES (@q{skillCode}, 'mcq', 'A', N'Right', 1), (@q{skillCode}, 'mcq', 'B', N'Wrong', 0);
                """);
            }
            else
            {
                // D6: a productive skill gets TWO tasks. The second is what makes "estimated from one
                // task" a real state rather than a hypothetical one - with one task, the first grading
                // would be a full measurement and the estimated status unreachable.
                lines.Add($"""
                INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
                  VALUES (@grp{skillCode}, @exam, @sec{skillCode}, @{skillCode}, N'Q{index}b', 'B1', 'free_response', {index * 100});
                """);
            }
        }

        lines.Add("""
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A2', N'Elementary', 0, 200, 1), (@cert, 'B1', N'Intermediate', 201, 500, 2);
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @cert, SkillId, NULL, 0, 0 FROM Skills WHERE CertId = @cert;
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @cert, SkillId, NULL, 1, 250 FROM Skills WHERE CertId = @cert;
            """);

        File.WriteAllText(path, string.Join("\n", lines));
        return path;
    }

    private async Task<Fixture> ReadFixtureAsync(string code)
    {
        await using var db = Db();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync();
        var skillIds = await db.Skills.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code, s => s.SkillId);
        var questionIds = await db.Questions.Where(q => q.ExamId == examId)
            .Join(db.Skills, q => q.SkillId, s => s.SkillId, (q, s) => new { s.Code, q.QuestionId, q.DisplayOrder })
            .ToListAsync();
        var first = questionIds
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DisplayOrder).First().QuestionId);
        // the second task a productive skill carries (D6); absent for an objective skill, which the
        // two-task test does not ask for
        var second = questionIds
            .GroupBy(x => x.Code)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DisplayOrder).ElementAt(1).QuestionId);
        var optionIds = await db.QuestionOptions.Where(o => o.IsCorrect == true)
            .Join(db.Questions, o => o.QuestionId, q => q.QuestionId, (o, q) => new { q.SkillId, o.OptionId })
            .ToDictionaryAsync(x => x.SkillId, x => x.OptionId);
        return new Fixture(certId, examId, skillIds, first, second, optionIds);
    }

    private sealed record Fixture(
        int CertId,
        int ExamId,
        IReadOnlyDictionary<string, int> SkillIds,
        IReadOnlyDictionary<string, int> QuestionIds,
        IReadOnlyDictionary<string, int> SecondQuestionIds,
        IReadOnlyDictionary<int, int> CorrectOptionBySkillId);

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
            await SqlAsync($"""
                UPDATE AttemptAnswers SET SelectedOptionId = {fixture.CorrectOptionBySkillId[fixture.SkillIds[skill]]}
                  WHERE AttemptId = {attemptId} AND QuestionId = {fixture.QuestionIds[skill]};
                """);
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
        await SqlAsync($"""
            UPDATE AttemptAnswers SET SelectedOptionId = {fixture.CorrectOptionBySkillId[fixture.SkillIds["LIST"]]}
              WHERE AttemptId = {attemptId} AND QuestionId = {fixture.QuestionIds["LIST"]};
            """);
        await AdvanceToSkillOfAsync(attemptId, fixture.QuestionIds["WRIT"]);

        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SaveDraftAsync(userId, placementId, fixture.QuestionIds["WRIT"],
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
        var attemptId = await SubmitWithDraftAsync(userId, placementId, fixture.QuestionIds["WRIT"]);

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

        // nothing measured anywhere: the placement is done only because the failure is recorded
        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal("done", placement.Status);
        Assert.Equal(PlacementFailureReason.AiGradingFailed, placement.CompletionFailureReason);
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
        await SqlAsync($"""
            UPDATE AttemptAnswers SET SelectedOptionId = {fixture.CorrectOptionBySkillId[fixture.SkillIds["LIST"]]}
              WHERE AttemptId = {attemptId} AND QuestionId = {fixture.QuestionIds["LIST"]};
            """);

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
        var attemptId = await SubmitWithDraftAsync(userId, placementId, fixture.QuestionIds["WRIT"]);
        var skillId = fixture.SkillIds["WRIT"];

        // D6: two tasks on the one writing skill. The engine pre-creates one draft per free-response
        // question when the sitting starts, so the second task's row already exists - it is the GRADING
        // that is inserted here. Had this fixture carried one task, the first grading would have been a
        // full measurement and the estimated status unreachable.
        await SqlAsync($"""
            DECLARE @fr1 INT = (SELECT FreeResponseId FROM FreeResponses
                                WHERE AttemptId = {attemptId} AND QuestionId = {fixture.QuestionIds["WRIT"]});
            UPDATE AiGradings SET Status = 'done', CompletedAt = SYSDATETIMEOFFSET(), OverallScaled = 4.5
              WHERE FreeResponseId = @fr1;
            UPDATE FreeResponses SET Status = 'graded', SubmittedAt = SYSDATETIMEOFFSET()
              WHERE FreeResponseId = @fr1;

            DECLARE @fr2 INT = (SELECT FreeResponseId FROM FreeResponses
                                WHERE AttemptId = {attemptId} AND QuestionId = {fixture.SecondQuestionIds["WRIT"]});
            UPDATE FreeResponses SET Status = 'graded', SubmittedAt = SYSDATETIMEOFFSET(),
                   ResponseText = N'Second response.' WHERE FreeResponseId = @fr2;
            UPDATE AiGradings SET Status = 'failed', CompletedAt = SYSDATETIMEOFFSET(), FailureReason = N'The model timed out.'
              WHERE FreeResponseId = @fr2;
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

        // two versions with different B1 ranges, so the same position yields different scores
        await SqlAsync($"""
            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
              VALUES ({fixture.CertId}, 1, 0, N'v1', '2026-01-01'), ({fixture.CertId}, 2, 0, N'v2', '2026-02-01');
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled)
              VALUES ({fixture.CertId}, 1, {skillId}, 'B1', 'cut', 200, 300),
                     ({fixture.CertId}, 2, {skillId}, 'B1', 'cut', 250, 350);
            """);

        var placementId = await StartPlacementAsync(userId, fixture);
        var attemptId = await AttemptIdAsync(placementId);
        await SqlAsync($"""
            UPDATE AttemptAnswers SET SelectedOptionId = {fixture.CorrectOptionBySkillId[skillId]}
              WHERE AttemptId = {attemptId} AND QuestionId = {fixture.QuestionIds["LIST"]};
            """);

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
            await service.SaveDraftAsync(userId, placementId, fixture.QuestionIds["WRIT"],
                "A written response that is long enough to grade.");
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
        await SqlAsync($"""
            UPDATE AttemptAnswers SET SelectedOptionId = {fixture.CorrectOptionBySkillId[skillId]}
              WHERE AttemptId = {attemptId} AND QuestionId = {fixture.QuestionIds["LIST"]};
            """);

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
    /// The sitting's one-way skill lock: a placement paper serves one skill's questions until the
    /// learner crosses into the next. This advances the attempt to the skill holding
    /// <paramref name="questionId"/>, which is what the take screen does when the objective half is
    /// finished. Without it, writing to a later skill's question is refused - correctly.
    /// </summary>
    private Task AdvanceToSkillOfAsync(int attemptId, int questionId) =>
        SqlAsync($"""
            UPDATE a
            SET a.ActiveSkillId = (SELECT q.SkillId FROM Questions q WHERE q.QuestionId = {questionId}),
                a.PhaseDeadlineAt = DATEADD(minute, 30, GETUTCDATE())
            FROM ExamAttempts a
            WHERE a.AttemptId = {attemptId};
            """);

    /// <summary>
    /// Submits the placement with one written draft, which is what creates the attempt's free-response
    /// row and its pending AiGrading. Returns the attempt id.
    /// </summary>
    private async Task<int> SubmitWithDraftAsync(int userId, int placementId, int questionId)
    {
        var attemptId = await AttemptIdAsync(placementId);
        await AdvanceToSkillOfAsync(attemptId, questionId);

        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SaveDraftAsync(userId, placementId, questionId,
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
