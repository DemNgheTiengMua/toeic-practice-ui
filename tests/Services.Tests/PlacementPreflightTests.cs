using System.Data.Common;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Services.Tests;

/// <summary>
/// #213: the placement preflight, the 90-day reuse window, and the 24-hour retake cooldown.
///
/// <b>Every test below names the input that would pass a weaker one.</b> The two defects this ticket is
/// most likely to ship are a cooldown that is really a lifetime cap, and a waiver that fires on any
/// "measured nothing" - so both are driven with the sitting that would slip through: a third placement
/// (a cap refuses it), a learner who skipped everything (a loose waiver allows it), and a timeout (the
/// ambiguous one).
///
/// Isolation: this class owns a database created for it and dropped afterwards. Seeding the shared
/// fixture catalog has broken the full suite three times in this rebuild.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementPreflightTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EE213_{Guid.NewGuid():N}"[..24];
    private string _connectionString = null!;

    public PlacementPreflightTests(DatabaseFixture database) => _database = database;

    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private static string WriteBatch(string batch)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ee213-{Guid.NewGuid():N}.sql");
        File.WriteAllText(path, batch);
        return path;
    }

    private Task SqlAsync(string batch) =>
        _database.SqlCmdFileToDatabaseAsync(_dbName, WriteBatch(batch));

    // -------------------------------------------------------------------------------------------
    // fixtures
    // -------------------------------------------------------------------------------------------

    private sealed record Fixture(
        string CertCode,
        int CertId,
        int ExamId,
        IReadOnlyDictionary<string, int> SkillIds,
        IReadOnlyDictionary<int, int> PositionIds,
        IReadOnlyDictionary<int, int> CorrectOptionBySkillId);

    /// <summary>
    /// A certificate with the named skills, one objective position per skill, a band table, and one
    /// placement exam. <c>freeResponse</c> skills get two tasks, which is what makes a grading failure
    /// representable (both tasks must fail before the skill is <c>not_measured</c>).
    /// <para>
    /// #269: the paper is authored on the pool - the positions are <c>ExamItems</c> rows and the ids a
    /// test addresses are <c>ExamItemId</c>s. The legacy QuestionGroups/Questions/QuestionOptions rows
    /// this fixture used to insert are the tables being dropped.
    /// </para>
    /// </summary>
    private async Task<Fixture> SeedAsync(string code, params (string Code, string Modality, bool FreeResponse)[] skills)
    {
        var lines = new List<string>
        {
            $"INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Cert {code}', 1);",
            "DECLARE @cert INT = SCOPE_IDENTITY();",
            "INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)",
            "  VALUES (@cert, N'Placement', 'published', 30, 'placement', 90);",
        };

        var index = 0;
        foreach (var (skillCode, modality, freeResponse) in skills)
        {
            index++;
            lines.Add($"""
                INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
                  VALUES (@cert, '{skillCode}', N'{skillCode}', '{modality}', {index}, 0, 100, 1);
                INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder)
                  VALUES ((SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = '{skillCode}'),
                          @cert, '{modality}', 'S{index}', N'Section {index}', {(freeResponse ? 2 : 1)}, {index});
                """);
        }

        lines.Add("""
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A2', N'Elementary', 0, 200, 1), (@cert, 'B1', N'Intermediate', 201, 500, 2);
            -- #259 / PRN-03: the cut-score table is what places a score. Without it the writer used to
            -- parse the band code as a level, which is the crosswalk the rule forbids.
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

        await SqlAsync(string.Join("\n", lines));

        await using var db = Db();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync();
        var skillIds = await db.Skills.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code, s => s.SkillId);

        // One objective position per skill; a productive one gets two tasks (D6) and no option, because
        // CK_Items_Modality_RenderFormat pins a productive modality to 'free_response'.
        var positionIds = new Dictionary<int, int>();
        var correctOptionBySkillId = new Dictionary<int, int>();
        foreach (var (skillCode, modality, freeResponse) in skills)
        {
            var sectionId = await db.Sections
                .Where(s => s.CertId == certId && s.SkillId == skillIds[skillCode])
                .Select(s => s.SectionId).SingleAsync();

            if (freeResponse)
            {
                var first = await PoolFixture.AddProductiveAsync(
                    db, examId, certId, sectionId, modality, $"Q{skillCode}", displayOrder: 1);
                await PoolFixture.AddProductiveAsync(
                    db, examId, certId, sectionId, modality, $"Q{skillCode}b", displayOrder: 2);
                positionIds[skillIds[skillCode]] = first.ExamItemId;
            }
            else
            {
                var position = await PoolFixture.AddMcqAsync(
                    db, examId, certId, sectionId, modality, $"Q{skillCode}", displayOrder: 1);
                positionIds[skillIds[skillCode]] = position.ExamItemId;
                correctOptionBySkillId[skillIds[skillCode]] =
                    await PoolFixture.OptionIdAsync(db, examId, position.ExamItemId, "A");
            }
        }

        return new Fixture(code, certId, examId, skillIds, positionIds, correctOptionBySkillId);
    }

    private async Task<int> SeedLearnerAsync(string certCode, int credits = 0)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01,
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            """);
        await using var db = Db();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
        if (credits > 0)
        {
            var adminId = await db.Users.Where(u => u.Email == "preflight-admin@test.local").Select(u => (int?)u.UserId).FirstOrDefaultAsync();
            if (adminId is null)
            {
                await SqlAsync("""
                    INSERT INTO Users (RoleId, Email, PasswordHash)
                    VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'preflight-admin@test.local', 0x01);
                    """);
                adminId = await db.Users.Where(u => u.Email == "preflight-admin@test.local").Select(u => u.UserId).SingleAsync();
            }
            db.CreditTransactions.Add(new BusinessObjects.Entities.CreditTransaction
            {
                UserId = userId,
                Reason = CreditReason.AdminGrant.ToCode(),
                Delta = credits,
                AdminUserId = adminId,
                Note = "Seed test credits",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        return userId;
    }

    private PlacementService Service(CompanionDbContext db, TimeProvider? clock = null) =>
        new(db, new LearnerService(db), attemptEngine: null, clock: clock);

    /// <summary>
    /// Sits a placement end to end: start, answer every objective skill, submit. Returns the placement id
    /// and the attempt id.
    /// </summary>
    private async Task<(int PlacementId, int AttemptId)> SitAsync(int userId, Fixture fixture, bool answerEverything = true)
    {
        int placementId;
        int attemptId;
        await using (var db = Db())
        {
            var info = await Service(db).StartPlacementTestAsync(userId, fixture.ExamId);
            placementId = info.PlacementId;
            attemptId = info.AttemptId;
        }

        if (answerEverything)
        {
            await AnswerAllAsync(userId, placementId, attemptId, fixture);
        }

        await using (var db = Db())
        {
            // #217 / PLC-19: a sitting that measured nothing is recorded as abandoned, and the service
            // says so rather than returning a result. The caller here wants the ROW, so the refusal is
            // expected - and only expected when nothing was answered.
            try
            {
                await Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementAbandoned && !answerEverything)
            {
            }
        }

        return (placementId, attemptId);
    }

    /// <summary>Answers every objective skill, crossing the one-way skill lock as the take screen does.</summary>
    private async Task AnswerAllAsync(int userId, int placementId, int attemptId, Fixture fixture)
    {
        var ordered = fixture.SkillIds.OrderBy(pair => pair.Value).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var skillId = ordered[i].Value;
            if (!fixture.CorrectOptionBySkillId.TryGetValue(skillId, out var optionId))
            {
                continue;
            }

            if (i > 0)
            {
                await AdvanceToSkillAsync(attemptId, fixture.PositionIds[skillId]);
            }

            await using var db = Db();
            await Service(db).SaveAnswerAsync(
                userId, placementId, PositionOf(fixture, skillId), optionId, isFlagged: false);
        }
    }

    private static int PositionOf(Fixture fixture, int skillId) => fixture.PositionIds[skillId];

    /// <summary>
    /// The sitting's one-way skill lock: a placement paper serves one skill's positions until the learner
    /// crosses into the next. This is the same move the take screen makes when the previous skill's
    /// objective half is finished.
    /// </summary>
    private Task AdvanceToSkillAsync(int attemptId, int examItemId) =>
        SqlAsync($"""
            UPDATE a
            SET a.ActiveSkillId = (SELECT s.SkillId
                                   FROM ExamItems ei JOIN Sections s ON s.SectionId = ei.SectionId
                                   WHERE ei.ExamItemId = {examItemId}),
                a.PhaseDeadlineAt = DATEADD(minute, 30, GETUTCDATE())
            FROM ExamAttempts a
            WHERE a.AttemptId = {attemptId};
            """);

    private async Task<PlacementPreflightView> PreflightAsync(int userId, TimeProvider clock)
    {
        await using var db = Db();
        return await Service(db, clock).GetPreflightAsync(userId);
    }

    // -------------------------------------------------------------------------------------------
    // story 51 / 52 / 55: reuse is offered with its original date, and carrying it does not re-stamp
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A skill measured inside the window is offered with its level, its position and the date it was
    /// actually measured.
    ///
    /// <b>The input that would pass a weaker test:</b> an offer that reported <c>DateTimeOffset.UtcNow</c>
    /// as the measurement date - every "the field is populated" assertion accepts it, and the learner is
    /// told a six-week-old result is from today. The date is compared against the row that produced it.
    /// </summary>
    [Fact]
    public async Task A_skill_measured_inside_the_window_is_offered_with_its_level_position_and_original_date()
    {
        var fixture = await SeedAsync(Unique("T213A"), ("LIST", "listening", false), ("READ", "reading", false));
        var userId = await SeedLearnerAsync(fixture.CertCode);

        await SitAsync(userId, fixture);

        DateTimeOffset measuredAt;
        CefrLevel level;
        int position;
        await using (var db = Db())
        {
            var state = await db.LearnerSkillStates.AsNoTracking()
                .SingleAsync(s => s.UserId == userId && s.SkillId == fixture.SkillIds["LIST"]);
            measuredAt = state.MeasuredAt;
            level = state.CefrLevel;
            position = state.PositionInBand;
        }

        // a day later, past the cooldown and well inside the 90-day window
        var view = await PreflightAsync(userId, new TestClock(DateTimeOffset.UtcNow.AddDays(1)));

        var offered = view.Offers.Single(o => o.SkillCode == "LIST");
        Assert.True(offered.CanReuse);
        Assert.Equal(level, offered.Level);
        Assert.Equal(position, offered.PositionInBand);
        Assert.Equal(measuredAt, offered.MeasuredAt);
        Assert.Equal(measuredAt.AddDays(90), offered.ExpiresAt);
        Assert.True(view.HasReusableSkills);
    }

    /// <summary>
    /// The learner chooses to carry the valid skills over, and the carried result keeps the ORIGINAL
    /// measurement date - never today's.
    ///
    /// The carried skill is one this sitting did not reach (a two-skill certificate where only the first
    /// skill was answered): a carry-over fills a gap, it never overwrites evidence the sitting produced.
    /// That is why the second skill's row is the carried one and the first skill's is this sitting's.
    ///
    /// <b>The input that would pass a weaker test:</b> a writer that copies the level and position but
    /// stamps <c>MeasuredAt = now</c>. Every screen reading a level sees the right number; only the date
    /// and <c>ReusedFromPrior</c> tell the learner they are looking at a carried result. The stored
    /// <c>LearnerSkillStates</c> row is also asserted unchanged, which is what catches a reuse routed
    /// through <c>SkillStateWriter.RecordAsync</c> (it would move the date forward).
    /// </summary>
    [Fact]
    public async Task Reusing_a_valid_skill_carries_it_with_its_original_date_and_badges_it()
    {
        var fixture = await SeedAsync(Unique("T213B"), ("LIST", "listening", false), ("READ", "reading", false));
        var userId = await SeedLearnerAsync(fixture.CertCode, credits: 10);
        await SitAsync(userId, fixture);

        DateTimeOffset originalMeasuredAt;
        await using (var db = Db())
        {
            originalMeasuredAt = await db.LearnerSkillStates.AsNoTracking()
                .Where(s => s.UserId == userId && s.SkillId == fixture.SkillIds["READ"])
                .Select(s => s.MeasuredAt).SingleAsync();
        }

        var clock = new TestClock(DateTimeOffset.UtcNow.AddDays(1));
        int placementId;
        int attemptId;
        await using (var db = Db())
        {
            var info = await Service(db, clock).StartPlacementTestAsync(userId, fixture.ExamId, PlacementReuseChoice.ReuseValid);
            placementId = info.PlacementId;
            attemptId = info.AttemptId;
        }

        // only Listening is sat; Reading is the gap the carry-over fills
        await using (var db = Db())
        {
            await Service(db, clock).SaveAnswerAsync(
                userId, placementId, fixture.PositionIds[fixture.SkillIds["LIST"]],
                fixture.CorrectOptionBySkillId[fixture.SkillIds["LIST"]], isFlagged: false);
        }

        await using (var db = Db())
        {
            await Service(db, clock).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using var verify = Db();
        var carriedRow = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.SkillIds["READ"]);

        Assert.True(carriedRow.ReusedFromPrior);
        Assert.Equal(PlacementResultStatus.Measured, carriedRow.ResultStatus);
        Assert.Equal(originalMeasuredAt, carriedRow.MeasuredAt);
        Assert.Equal(originalMeasuredAt.AddDays(90), carriedRow.ExpiresAt);
        Assert.Null(carriedRow.FailureReason);

        // the skill this sitting actually measured is NOT badged as carried, and carries today's date
        var freshRow = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.SkillIds["LIST"]);
        Assert.False(freshRow.ReusedFromPrior);
        Assert.Equal(PlacementResultStatus.Measured, freshRow.ResultStatus);
        Assert.True(freshRow.MeasuredAt > originalMeasuredAt);

        // reuse derives nothing, so the stored state still points at the original measurement
        var state = await verify.LearnerSkillStates.AsNoTracking()
            .SingleAsync(s => s.UserId == userId && s.SkillId == fixture.SkillIds["READ"]);
        Assert.Equal(originalMeasuredAt, state.MeasuredAt);

        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal("done", placement.Status);
    }

    /// <summary>
    /// Story 52: the other answer at the same preflight measures everything again, so the new sitting's
    /// rows are this sitting's, with today's date and no badge.
    ///
    /// <b>The input that would pass a weaker test:</b> a service that ignored the learner's choice and
    /// always reused (or always re-measured). Both tests exist so that the pair pins both branches; only
    /// this one asserts <c>ReusedFromPrior == false</c> for a learner who HAD a reusable result.
    /// </summary>
    [Fact]
    public async Task Choosing_to_remeasure_measures_the_skills_again_instead_of_carrying_them()
    {
        var fixture = await SeedAsync(Unique("T213C"), ("LIST", "listening", false), ("READ", "reading", false));
        var userId = await SeedLearnerAsync(fixture.CertCode, credits: 10);
        await SitAsync(userId, fixture);

        DateTimeOffset originalMeasuredAt;
        await using (var db = Db())
        {
            originalMeasuredAt = await db.LearnerSkillStates.AsNoTracking()
                .Where(s => s.UserId == userId && s.SkillId == fixture.SkillIds["LIST"])
                .Select(s => s.MeasuredAt).SingleAsync();
        }

        var clock = new TestClock(DateTimeOffset.UtcNow.AddDays(1));
        int placementId;
        int attemptId;
        await using (var db = Db())
        {
            var info = await Service(db, clock).StartPlacementTestAsync(userId, fixture.ExamId, PlacementReuseChoice.Remeasure);
            placementId = info.PlacementId;
            attemptId = info.AttemptId;
        }

        await AnswerAllAsync(userId, placementId, attemptId, fixture);

        await using (var db = Db())
        {
            await Service(db, clock).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using var verify = Db();
        var rows = await verify.PlacementSkillResults.AsNoTracking()
            .Where(r => r.PlacementId == placementId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.False(r.ReusedFromPrior);
            Assert.Equal(PlacementResultStatus.Measured, r.ResultStatus);
            Assert.NotNull(r.MeasuredAt);
            Assert.True(r.MeasuredAt > originalMeasuredAt,
                $"a re-measured skill must carry a NEW date; stored {r.MeasuredAt:d} against original {originalMeasuredAt:d}");
        });
    }

    /// <summary>
    /// A stored result past its 90-day window is still shown - it is the learner's answer - but it is not
    /// offered as a carry-over, because D18 does not allow one.
    ///
    /// <b>The input that would pass a weaker test:</b> offering every stored state regardless of
    /// <c>ExpiresAt</c>. That passes every reuse test above, because those sit one day later. Only a
    /// clock past the window tells the two apart.
    /// </summary>
    [Fact]
    public async Task A_result_past_the_90_day_window_is_shown_but_not_offered_for_reuse()
    {
        var fixture = await SeedAsync(Unique("T213D"), ("LIST", "listening", false));
        var userId = await SeedLearnerAsync(fixture.CertCode);
        await SitAsync(userId, fixture);

        var view = await PreflightAsync(userId, new TestClock(DateTimeOffset.UtcNow.AddDays(91)));

        var offered = view.Offers.Single(o => o.SkillCode == "LIST");
        Assert.False(offered.CanReuse);
        Assert.NotNull(offered.MeasuredAt);
        Assert.NotNull(offered.Level);
        Assert.False(view.HasReusableSkills);
    }

    // -------------------------------------------------------------------------------------------
    // story 53: a 24-hour cooldown with NO lifetime cap
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A retake immediately after a finished sitting is refused with its own error code, not a generic
    /// failure, so the screen can say when it opens.
    /// </summary>
    [Fact]
    public async Task A_retake_inside_24_hours_is_refused()
    {
        var fixture = await SeedAsync(Unique("T213E"), ("LIST", "listening", false));
        var userId = await SeedLearnerAsync(fixture.CertCode);
        await SitAsync(userId, fixture);

        await using var db = Db();
        var error = await Assert.ThrowsAsync<PlacementException>(() =>
            Service(db).StartPlacementTestAsync(userId, fixture.ExamId));
        Assert.Equal(PlacementException.PlacementCooldown, error.Code);

        var view = await Service(db).GetPreflightAsync(userId);
        Assert.False(view.IsStartable);
        Assert.NotNull(view.CooldownUntil);
    }

    /// <summary>
    /// <b>No lifetime cap.</b> Three finished sittings, each a day apart, and the third starts. This is
    /// the test a "refuse if they have already placed twice" guard fails, and it is the reason the rule
    /// reads only the most recent row.
    ///
    /// <b>The input that would pass a weaker test:</b> counting finished placements instead of measuring
    /// elapsed time - it behaves identically for the first two sittings, which is all the cooldown test
    /// above exercises.
    /// </summary>
    [Fact]
    public async Task A_third_placement_is_allowed_once_its_cooldown_has_elapsed()
    {
        var fixture = await SeedAsync(Unique("T213F"), ("LIST", "listening", false));
        var userId = await SeedLearnerAsync(fixture.CertCode, credits: 15);
        await SitAsync(userId, fixture);

        // second sitting: 25 hours later
        var second = new TestClock(DateTimeOffset.UtcNow.AddHours(25));
        int secondPlacement;
        await using (var db = Db())
        {
            secondPlacement = (await Service(db, second).StartPlacementTestAsync(userId, fixture.ExamId)).PlacementId;
        }

        await using (var db = Db())
        {
            // #217 / PLC-19: an untouched second sitting is abandoned with no result. That is still a
            // FINISHED sitting, so the cooldown applies exactly as it did when it was stored as `done`
            // - which is what the third start below proves, since it must still be allowed.
            await Assert.ThrowsAsync<PlacementException>(
                () => Service(db, second).SubmitPlacementTestAsync(userId, secondPlacement, isAutoSubmit: false));
        }

        // third sitting: another 25 hours later
        var third = new TestClock(DateTimeOffset.UtcNow.AddHours(50));
        await using var verify = Db();
        var info = await Service(verify, third).StartPlacementTestAsync(userId, fixture.ExamId);
        Assert.True(info.PlacementId > 0);
        Assert.Equal(PlacementStatus.InProgress, info.Status);
    }

    // -------------------------------------------------------------------------------------------
    // story 54: the waiver is an INTERNAL FAILURE, not any "measured nothing"
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A sitting that measured nothing because every productive task's grading failed waives the
    /// cooldown: the learner is not punished for an outage.
    /// </summary>
    [Fact]
    public async Task A_previous_sitting_whose_grading_failed_waives_the_cooldown()
    {
        var fixture = await SeedAsync(Unique("T213G"), ("WRIT", "writing", true));
        var userId = await SeedLearnerAsync(fixture.CertCode, credits: 10);

        var placementId = await SitFreeResponseFailingAsync(userId, fixture);

        await using (var verifyFailure = Db())
        {
            var placement = await verifyFailure.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
            // #217 / PLC-19: a sitting that measured nothing is ABANDONED, not done. The reason it
            // records is what waives the cooldown below, and that is the fact this test is about.
            Assert.Equal("abandoned", placement.Status);
            Assert.Equal(PlacementFailureReason.AiGradingFailed, placement.CompletionFailureReason);
        }

        await using var db = Db();
        var view = await Service(db).GetPreflightAsync(userId);
        Assert.True(view.IsStartable);
        Assert.True(view.CooldownWaived);

        // and the start actually goes through, rather than the screen merely saying so
        var info = await Service(db).StartPlacementTestAsync(userId, fixture.ExamId);
        Assert.True(info.PlacementId > 0);
    }

    /// <summary>
    /// <b>A skipped sitting does NOT waive.</b> This is the trap: "waive when the previous sitting
    /// measured nothing" reads correctly and lets a learner who answered nothing retake immediately -
    /// which is the reroll the cooldown exists to prevent.
    /// </summary>
    [Fact]
    public async Task A_previous_sitting_the_learner_skipped_does_not_waive_the_cooldown()
    {
        var fixture = await SeedAsync(Unique("T213H"), ("LIST", "listening", false), ("READ", "reading", false));
        var userId = await SeedLearnerAsync(fixture.CertCode);

        // started and submitted without answering anything, not auto-submitted: candidate_skipped.
        // #217 / PLC-19: that sitting is now ABANDONED with no result - the very state the cooldown has
        // to read, or the "a skip does not waive" rule becomes unreachable.
        var (placementId, _) = await SitAsync(userId, fixture, answerEverything: false);

        await using (var verifyFailure = Db())
        {
            var placement = await verifyFailure.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
            Assert.Equal("abandoned", placement.Status);
            Assert.Equal(PlacementFailureReason.CandidateSkipped, placement.CompletionFailureReason);
        }

        await using var db = Db();
        var view = await Service(db).GetPreflightAsync(userId);
        Assert.False(view.IsStartable);
        Assert.False(view.CooldownWaived);

        var error = await Assert.ThrowsAsync<PlacementException>(() =>
            Service(db).StartPlacementTestAsync(userId, fixture.ExamId));
        Assert.Equal(PlacementException.PlacementCooldown, error.Code);
    }

    /// <summary>
    /// <b>A timed-out sitting does not waive either.</b> The decision, argued in
    /// <see cref="PlacementCooldown"/>: from the stored row an outage looks exactly like a learner who
    /// walked away, so waiving on the timer hands the reroll back. This test is what stops that falling
    /// through a <c>default:</c> arm by accident.
    /// </summary>
    [Fact]
    public async Task A_previous_sitting_that_timed_out_does_not_waive_the_cooldown()
    {
        var fixture = await SeedAsync(Unique("T213I"), ("LIST", "listening", false));
        var userId = await SeedLearnerAsync(fixture.CertCode);

        int placementId;
        await using (var db = Db())
        {
            placementId = (await Service(db).StartPlacementTestAsync(userId, fixture.ExamId)).PlacementId;
        }

        await using (var db = Db())
        {
            // #217 / PLC-19: a sitting that measured NOTHING is now abandoned with no result, so a
            // plain submit of an untouched paper is a refusal. This test is about the EXPIRY, not the
            // result: it asserts the deadline passed and the attempt was auto-submitted, and the
            // refusal is the proof both happened.
            await Assert.ThrowsAsync<PlacementException>(
                () => Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: true));
        }

        await using (var verifyFailure = Db())
        {
            var placement = await verifyFailure.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
            Assert.Equal(PlacementFailureReason.InactivityTimeout, placement.CompletionFailureReason);
        }

        await using var verify = Db();
        Assert.False((await Service(verify).GetPreflightAsync(userId)).IsStartable);
    }

    // -------------------------------------------------------------------------------------------
    // the certificate's own skills, and only those
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A certificate that examines two skills offers two skills, and does not ask for a microphone on a
    /// paper with no speaking.
    ///
    /// <b>The input that would pass a weaker test:</b> a fixed four-skill offer, or one built from
    /// <c>LearnerStanding</c> - which omits unmeasured skills, so after a listening-only first sitting it
    /// would silently offer one skill. Both are wrong for a different reason, and the count plus the
    /// exact codes plus the microphone flag catch both.
    /// </summary>
    [Fact]
    public async Task Only_the_skills_the_certificate_examines_are_offered()
    {
        var fixture = await SeedAsync(Unique("T213J"), ("LIST", "listening", false), ("READ", "reading", false));
        var userId = await SeedLearnerAsync(fixture.CertCode);

        // a second certificate in the same database, with the two skills this one does NOT examine. A
        // query without the certificate's own filter offers all four, and this is what makes that
        // failure deterministic rather than dependent on what else the class seeded first.
        await SeedAsync(Unique("T213J2"), ("SPEA", "speaking", true), ("WRIT", "writing", true));

        // one skill measured, so a standing-derived offer would show exactly one
        await SitAsync(userId, fixture);

        var view = await PreflightAsync(userId, new TestClock(DateTimeOffset.UtcNow.AddDays(1)));

        Assert.Equal(2, view.Offers.Count);
        Assert.Equal(["LIST", "READ"], view.Offers.Select(o => o.SkillCode).ToList());
        Assert.DoesNotContain(view.Offers, o => o.SkillCode is "SPEA" or "WRIT");
        Assert.False(view.RequiresMicrophone);
    }

    [Fact]
    public async Task A_speaking_skill_offers_the_microphone_and_a_reading_only_paper_does_not()
    {
        var speaking = await SeedAsync(Unique("T213K"), ("SPEA", "speaking", true), ("READ", "reading", false));
        var speaker = await SeedLearnerAsync(speaking.CertCode);
        var speakingView = await PreflightAsync(speaker, new TestClock(DateTimeOffset.UtcNow));
        Assert.True(speakingView.RequiresMicrophone);
        Assert.Contains(speakingView.Offers, o => o.SkillCode == "SPEA" && o.RequiresMicrophone);

        var reading = await SeedAsync(Unique("T213L"), ("READ", "reading", false));
        var reader = await SeedLearnerAsync(reading.CertCode);
        var readingView = await PreflightAsync(reader, new TestClock(DateTimeOffset.UtcNow));
        Assert.False(readingView.RequiresMicrophone);
    }

    // -------------------------------------------------------------------------------------------
    // placement is free: no ledger row, and no balance READ on this path
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// #213 / #289: on a learner's first placement per certificate, "no balance is read anywhere on this path". Counting ledger rows
    /// catches a deduction; it does not catch a <c>GetBalance</c> left in a view model, which is the way
    /// a price gets onto a free flow. So the context used for the whole path is instrumented and every
    /// command that mentions the ledger or the balance view is recorded.
    ///
    /// <b>The input that would pass a weaker test:</b> a preflight that SELECTs the balance to decide
    /// whether to show a price, and writes nothing. The row count stays zero and the criterion fails.
    /// </summary>
    [Fact]
    public async Task The_preflight_and_start_never_read_the_credit_ledger_or_a_balance()
    {
        var fixture = await SeedAsync(Unique("T213M"), ("LIST", "listening", false));
        var userId = await SeedLearnerAsync(fixture.CertCode);

        var tap = new CreditTap();
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer(_connectionString)
            .AddInterceptors(tap)
            .Options;

        int placementId;
        await using (var db = new CompanionDbContext(options))
        {
            var service = Service(db);
            await service.GetPreflightAsync(userId);
            placementId = (await service.StartPlacementTestAsync(userId, fixture.ExamId)).PlacementId;
        }

        Assert.True(tap.SawTraffic, "the interceptor saw no SQL at all, so an empty ledger count would prove nothing");
        Assert.Empty(tap.CreditCommands);

        // and the ledger itself is untouched, which is the other half of the criterion
        await using var verify = Db();
        Assert.Equal(0, await verify.CreditTransactions.CountAsync(t => t.UserId == userId));
        Assert.True(placementId > 0);
    }

    // -------------------------------------------------------------------------------------------
    // story 87: a path step is never blocked by a cooldown
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The cooldown is reachable only from the placement start. Story 87 dies the day someone moves the
    /// rule into a helper the plan calls, so the plan's own module is scanned for any reference to it.
    ///
    /// <b>The input that would pass a weaker test:</b> a runtime "generate a path while a cooldown is
    /// active" test that passes today because the service has not been wired yet. That test would also
    /// pass if someone added the call tomorrow behind a flag. This one fails on the <c>using</c>.
    /// </summary>
    [Fact]
    public void The_learning_path_module_does_not_reference_the_placement_cooldown()
    {
        var folder = Path.Combine(DatabaseFixture.RepoRootPath, "src", "Services", "GuidedLearning");
        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        var forbidden = new[] { "PlacementCooldown", "IPlacementService", "GetPreflightAsync" };
        var offenders = files
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (File: Path.GetFileName(file), LineNumber: index + 1, Line: line))
                .Where(entry => forbidden.Any(token => entry.Line.Contains(token, StringComparison.Ordinal))))
            .Select(entry => $"{entry.File}:{entry.LineNumber} {entry.Line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "the plan must not reach the placement cooldown (story 87): " + string.Join(" | ", offenders));
    }

    // -------------------------------------------------------------------------------------------
    // story 55: the RESULT carries the per-skill rows, so the result page can badge one
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The result view carries one entry per skill with <c>ReusedFromPrior</c> and the ORIGINAL
    /// measurement date, including a carried skill that has no <c>AttemptSkillScore</c> at all.
    ///
    /// <b>The input that would pass a weaker test:</b> building the result's skill list from
    /// <c>AttemptSkillScores</c> only. That is what the page did before, and it silently omitted the
    /// carried skill — the one number the learner most needs dated — because a carried skill was never
    /// sat and therefore has no score row. Asserting the carried skill is PRESENT is what catches it.
    /// </summary>
    [Fact]
    public async Task The_result_view_carries_each_skills_reuse_flag_and_original_measurement_date()
    {
        var fixture = await SeedAsync(Unique("T213N"), ("LIST", "listening", false), ("READ", "reading", false));
        var userId = await SeedLearnerAsync(fixture.CertCode, credits: 10);
        await SitAsync(userId, fixture);

        DateTimeOffset originalMeasuredAt;
        await using (var db = Db())
        {
            originalMeasuredAt = await db.LearnerSkillStates.AsNoTracking()
                .Where(s => s.UserId == userId && s.SkillId == fixture.SkillIds["READ"])
                .Select(s => s.MeasuredAt).SingleAsync();
        }

        var clock = new TestClock(DateTimeOffset.UtcNow.AddDays(1));
        int placementId;
        await using (var db = Db())
        {
            var info = await Service(db, clock).StartPlacementTestAsync(userId, fixture.ExamId, PlacementReuseChoice.ReuseValid);
            placementId = info.PlacementId;
        }

        // only Listening is sat, so Reading is the carried gap and has no AttemptSkillScore
        await using (var db = Db())
        {
            await Service(db, clock).SaveAnswerAsync(
                userId, placementId, fixture.PositionIds[fixture.SkillIds["LIST"]],
                fixture.CorrectOptionBySkillId[fixture.SkillIds["LIST"]], isFlagged: false);
        }

        await using (var db = Db())
        {
            await Service(db, clock).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using var verify = Db();
        var result = await Service(verify, clock).GetPlacementResultAsync(userId, placementId);

        Assert.NotNull(result.SkillResults);
        Assert.Equal(2, result.SkillResults!.Count);

        var carried = result.SkillResults.Single(r => r.SkillCode == "READ");
        Assert.True(carried.ReusedFromPrior);
        Assert.True(carried.IsMeasured);
        Assert.Equal(originalMeasuredAt, carried.MeasuredAt);
        Assert.NotNull(carried.Level);
        Assert.NotNull(carried.PositionInBand);

        var fresh = result.SkillResults.Single(r => r.SkillCode == "LIST");
        Assert.False(fresh.ReusedFromPrior);
        Assert.True(fresh.IsMeasured);
        Assert.True(fresh.MeasuredAt > originalMeasuredAt);
    }

    // -------------------------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Sits a one-skill productive placement whose every task fails to grade, leaving the placement done
    /// with <c>ai_grading_failed</c> and nothing measured. This is the outage, driven through the real
    /// services rather than assembled by hand.
    /// </summary>
    private async Task<int> SitFreeResponseFailingAsync(int userId, Fixture fixture)
    {
        int placementId;
        int attemptId;
        await using (var db = Db())
        {
            var info = await Service(db).StartPlacementTestAsync(userId, fixture.ExamId);
            placementId = info.PlacementId;
            attemptId = info.AttemptId;
        }

        // D6: a productive skill carries two tasks. Both must fail before the skill is not_measured, so
        // both are drafted - a one-task sitting would go `estimated_from_one_task` and measure something.
        var positions = await PositionIdsAsync(attemptId);
        foreach (var examItemId in positions)
        {
            // #269: the sitting's one-way skill lock still applies - crossing to Writing is what the take
            // screen does when the objective half is finished.
            await AdvanceToSkillAsync(attemptId, examItemId);
            await using var db = Db();
            await Service(db).SaveDraftAsync(userId, placementId, examItemId, "A written response long enough to grade.");
        }

        await using (var db = Db())
        {
            await Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        // the worker ran and the model failed on both tasks
        await SqlAsync($"""
            UPDATE AiGradings SET Status = 'failed', CompletedAt = SYSDATETIMEOFFSET(), FailureReason = N'The model timed out.'
              WHERE FreeResponseId IN (SELECT FreeResponseId FROM FreeResponses WHERE AttemptId = {attemptId});
            UPDATE FreeResponses SET Status = 'graded' WHERE AttemptId = {attemptId};
            """);

        await using (var db = Db())
        {
            await new PlacementResultWriter(db).WriteAsync(placementId);
        }

        return placementId;
    }

    /// <summary>The paper's positions, in sitting order - the ids a pool draft is addressed by.</summary>
    private async Task<List<int>> PositionIdsAsync(int attemptId)
    {
        await using var db = Db();
        var examId = await db.PlacementTests.Where(p => p.AttemptId == attemptId)
            .Select(p => p.ExamId!.Value).SingleAsync();
        return await db.ExamItems
            .Where(i => i.ExamId == examId)
            .OrderBy(i => i.DisplayOrder)
            .Select(i => i.ExamItemId)
            .ToListAsync();
    }

    /// <summary>A clock the test moves without waiting a day.</summary>
    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Records every command that touches the credit ledger or the balance view. A read is as much a
    /// violation as a write here: the criterion says "no balance is read", and a row count cannot see one.
    /// </summary>
    private sealed class CreditTap : DbCommandInterceptor
    {
        public List<string> CreditCommands { get; } = [];
        public bool SawTraffic { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command)
        {
            SawTraffic = true;
            var text = command.CommandText;
            if (text.Contains("CreditTransactions", StringComparison.OrdinalIgnoreCase)
                || text.Contains("vw_UserCreditBalance", StringComparison.OrdinalIgnoreCase))
            {
                CreditCommands.Add(text);
            }
        }
    }
}
