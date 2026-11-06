using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #211 / D15: the learner's per-skill state is one row per learner per skill, written by exactly one
/// service, and the overall is the weakest measured skill computed at read.
/// <para>
/// Three assertions here are the ones a plausible implementation passes while being wrong, so each is
/// written as the wrong input rather than the happy path:
/// </para>
/// <list type="number">
/// <item><b>A stored overall.</b> The input is a certificate with three skills at B2/B2/A2. A service
/// that kept a fourth "overall" row, or a column, would return A2 and look right on this fixture - so
/// the test also asserts that the TABLE HAS NO SUCH COLUMN, which is the only way to tell "computed"
/// from "stored and happens to agree".</item>
/// <item><b>A tie-break that depends on row order.</b> Two skills at the same level, inserted in the
/// REVERSE of the certificate's order, so a "first row the query returned" implementation names the
/// other skill. The certificate's order is what the assertion pins.</item>
/// <item><b>An unmeasured skill counted as A1.</b> A certificate with three skills and only one
/// measured row; a service that joins skills and left-joins state would report A1 for the other two and
/// an overall of A1. The overall must equal the one measured skill.</item>
/// </list>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SkillStateWriterTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private SkillStateWriter CreateService(TimeProvider? clock = null) =>
        new(DbContextFactory.Create(database.ConnectionString), clock);

    /// <summary>
    /// A certificate with the named skills, in the given display order, and a learner. Returns the
    /// cert id, the skill ids in display order, and the user id.
    /// </summary>
    private async Task<(int CertId, int[] SkillIds, int UserId)> SeedAsync(
        string certCode, params (string Code, string Modality)[] skills)
    {
        var skillRows = string.Join(",", skills.Select((s, i) =>
            $"(@c, '{s.Code}', N'{s.Code}', '{s.Modality}', {i + 1})"));

        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @c INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES {skillRows};
            DECLARE @email VARCHAR(255) = CONCAT(NEWID(), '@test.local');
            INSERT INTO Users (RoleId, Email, PasswordHash)
              VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), @email, 0x01);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var skillIds = await db.Skills.Where(s => s.CertId == cert.CertId)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.SkillId)
            .Select(s => s.SkillId).ToArrayAsync();
        var userId = await db.Users
            .Where(u => u.Email.EndsWith("@test.local"))
            .OrderByDescending(u => u.UserId)
            .Select(u => u.UserId).FirstAsync();

        return (cert.CertId, skillIds, userId);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // -------------------------------------------------------------------------------------------
    // AC1: one row per learner per skill carries level, position, coordinate, source and expiry.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The row is written by the owner, and what is stored is exactly what was measured - including the
    /// expiry, which is the measurement plus D18's 90 days rather than something the caller passes.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> accepting the expiry from the caller. That
    /// would let a placement store a 30-day window and a calibration store 365, and the reuse rule
    /// would then differ per skill with nothing recording which rule applied. The assertion is on the
    /// exact interval, not "later than MeasuredAt".
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recording_a_skill_stores_the_level_position_coordinate_source_and_a_ninety_day_expiry()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code, ("LIST", "listening"), ("READ", "reading"));
        var now = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

        await CreateService(new FixedClock(now)).RecordAsync(
            userId, certId, skillIds[0], CefrLevel.B1, 42, 3.62m, LearnerSkillStateSource.Placement);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var row = await db.LearnerSkillStates.AsNoTracking()
            .SingleAsync(s => s.UserId == userId && s.CertId == certId && s.SkillId == skillIds[0]);

        Assert.Equal(CefrLevel.B1, row.CefrLevel);
        Assert.Equal(42, row.PositionInBand);
        Assert.Equal(3.62m, row.ContinuousCoordinate);
        Assert.Equal(LearnerSkillStateSource.Placement, row.Source);
        Assert.Equal(now, row.MeasuredAt);
        Assert.Equal(now.AddDays(90), row.ExpiresAt);
        // the declaration is the owner's, and the version moved off its default
        Assert.Equal(DerivedFactWriters.LearnerSkillState, row.WrittenByService);
        Assert.Equal(1, row.ProjectionVersion);
    }

    /// <summary>
    /// A second measurement of the same skill updates the row rather than adding one: "one row per
    /// learner per skill" is what every screen's single read depends on.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> EF's natural "insert or update" confusion -
    /// a caller that constructs a new entity and adds it. That succeeds on a table whose key is an
    /// identity column and fails here, but a service that caught the exception and fell through would
    /// leave two rows with nothing noticing. The count assertion is the half that catches that; the
    /// version assertion is what proves the update went through the owner's path.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recording_the_same_skill_twice_updates_one_row_and_moves_the_version_forward()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code, ("LIST", "listening"));
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));

        await CreateService(clock).RecordAsync(
            userId, certId, skillIds[0], CefrLevel.B1, 42, 3.62m, LearnerSkillStateSource.Placement);
        var standing = await CreateService(clock).RecordAsync(
            userId, certId, skillIds[0], CefrLevel.B2, 10, 4.10m, LearnerSkillStateSource.MockSection);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var rows = await db.LearnerSkillStates.AsNoTracking()
            .Where(s => s.UserId == userId && s.CertId == certId).ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal(CefrLevel.B2, row.CefrLevel);
        Assert.Equal(2, row.ProjectionVersion);
        Assert.Equal(LearnerSkillStateSource.MockSection, row.Source);
        Assert.Equal(CefrLevel.B2, standing.Overall);
    }

    // -------------------------------------------------------------------------------------------
    // AC2: no stored overall, and the computed one is the weakest measured skill.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The overall is the weakest MEASURED skill, and the limiting skill is named.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> an average. These three levels (B2, B2, A2)
    /// average to B1.67 - a service that averaged would answer "B1" for the overall, and a service that
    /// took the minimum would answer A2. The assertion is A2, which only MIN produces. Reading has the
    /// LOWER position of the two B2 skills, so an implementation that took "the skill with the lowest
    /// position overall" instead of "the lowest level, then position" would name Reading, and the
    /// assertion that the limiting skill is Speaking catches it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_overall_is_the_weakest_measured_skill_and_the_limiting_skill_is_named()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code,
            ("LIST", "listening"), ("READ", "reading"), ("SPEA", "speaking"));
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var writer = CreateService(clock);

        await writer.RecordAsync(userId, certId, skillIds[0], CefrLevel.B2, 40, 4.40m, LearnerSkillStateSource.Placement);
        await writer.RecordAsync(userId, certId, skillIds[1], CefrLevel.B2, 5, 4.05m, LearnerSkillStateSource.Placement);
        var standing = await writer.RecordAsync(
            userId, certId, skillIds[2], CefrLevel.A2, 64, 2.64m, LearnerSkillStateSource.Placement);

        Assert.Equal(3, standing.Skills.Count);
        Assert.Equal(CefrLevel.A2, standing.Overall);
        Assert.Equal(skillIds[2], standing.LimitingSkillId);
        Assert.Equal("SPEA", standing.LimitingSkillCode);
        // the mean of B2/B2/A2 would be a level between B1 and B2; no such value can be produced here
        Assert.DoesNotContain(standing.Skills, s => s.Level == CefrLevel.A1);
    }

    /// <summary>
    /// <b>Nothing stores the overall.</b> The assertion is against the schema, not the service, because
    /// that is the only place "stored" and "computed" differ: a committed column, a computed column and
    /// an "overall pseudo-skill" row all agree with MIN on a freshly seeded fixture.
    /// <para>
    /// <b>The input that passes a weaker implementation:</b> a stored overall called anything but
    /// "overall" - <c>AggregateLevel</c>, <c>Standing</c>, <c>MinLevel</c>. A <c>LIKE '%overall%'</c>
    /// lookup, which is the obvious guard, misses all three and reports "nothing stored" while a
    /// column sits there. So the primary assertion is the EXACT column set: any added column fails, and
    /// its name is printed. The pseudo-skill check below catches the third way to store it.
    /// </para>
    /// <para>
    /// The column lookup is proved to work first, so a query that sees nothing cannot report "absent
    /// everywhere" and pass - the defect lib.sql records from domain04.
    /// </para>
    /// </summary>
    [Fact]
    public async Task No_overall_is_stored_anywhere_on_the_per_skill_state_table()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var columns = await db.Database
            .SqlQueryRaw<string>(
                "SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS " +
                "WHERE TABLE_NAME = 'LearnerSkillStates'")
            .ToListAsync();

        // the lookup works at all, before its result is used to prove an absence
        Assert.Contains("CefrLevel", columns);

        // the whole row, and nothing else. A stored overall under any name is an extra column here.
        string[] expected =
        [
            "UserId", "CertId", "SkillId",
            "CefrLevel", "PositionInBand", "ContinuousCoordinate",
            "Source", "MeasuredAt", "ExpiresAt",
            "WrittenByService", "ProjectionVersion",
        ];
        Assert.Equal(expected.OrderBy(c => c), columns.OrderBy(c => c));

        // An "overall" stored as a pseudo-skill row is the same fact in a different place: every reader
        // computes MIN over the certificate's skills, so a skill coded OVERALL would silently enter the
        // minimum as a fourth skill that is not one.
        var overallSkills = await db.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM Skills WHERE UPPER(Code) IN ('OVERALL', 'ALL')")
            .SingleAsync();
        Assert.Equal(0, overallSkills);
    }

    /// <summary>
    /// An unmeasured skill is ABSENT, not an A1 floor. A learner measured only in Listening reads an
    /// overall of B1, and the certificate's Reading is not in the vector at all - so a dashboard
    /// renders one standing card and says the other is not measured, rather than printing A1.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> a left join from Skills to state, which is
    /// the natural shape and reports every skill. On this fixture it answers A1 for Reading and an
    /// overall of A1 - a fabricated bottom band, which is the same class of invention as a prorated
    /// study-hour estimate.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_unmeasured_skill_is_absent_from_the_vector_rather_than_present_at_a1()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code, ("LIST", "listening"), ("READ", "reading"));
        var writer = CreateService(new FixedClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)));

        await writer.RecordAsync(
            userId, certId, skillIds[0], CefrLevel.B1, 50, 3.50m, LearnerSkillStateSource.Placement);

        var standing = await writer.ReadAsync(userId, certId);

        Assert.NotNull(standing);
        var only = Assert.Single(standing.Skills);
        Assert.Equal(skillIds[0], only.SkillId);
        Assert.Equal(CefrLevel.B1, standing.Overall);
    }

    // -------------------------------------------------------------------------------------------
    // AC3: the limiting skill's tie is broken deterministically.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Two skills at the same level, inserted in the REVERSE of the certificate's order, so a
    /// "whichever row the query happened to return" implementation and a "lowest position" one disagree.
    /// <para>
    /// <b>The rule is D5's, and the reason is that the two halves of the tie are not equally weak.</b>
    /// B1 at 5/100 is one answer from dropping to A2; B1 at 80/100 is not. Naming the 80/100 skill as
    /// the limiting skill would send the learner's study time to the wrong modality - and would disagree
    /// with the placement snapshot's own <c>LimitingSkillId</c> for the same data.
    /// </para>
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> Reading (the second skill) is inserted FIRST
    /// and holds the lower position, so a row-insertion-order tie-break and a lowest-position one agree
    /// - and the assertion pins Reading, which the position rule produces. The second half of the test
    /// then sets the positions EQUAL, where position cannot decide, and pins the certificate's order,
    /// so the final tie-break is proved deterministic rather than incidentally correct.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_limiting_skill_is_the_lowest_level_then_the_lowest_position()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code, ("LIST", "listening"), ("READ", "reading"));
        var writer = CreateService(new FixedClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)));

        // same level, different positions: Reading is weaker despite being the second skill
        await writer.RecordAsync(userId, certId, skillIds[1], CefrLevel.B1, 5, 3.05m, LearnerSkillStateSource.Placement);
        var standing = await writer.RecordAsync(
            userId, certId, skillIds[0], CefrLevel.B1, 80, 3.80m, LearnerSkillStateSource.Placement);

        Assert.Equal(CefrLevel.B1, standing.Overall);
        Assert.Equal(skillIds[1], standing.LimitingSkillId);
        Assert.Equal("READ", standing.LimitingSkillCode);

        // and the answer is the same on a second read in a fresh context: determinism, not luck
        var again = await CreateService().ReadAsync(userId, certId);
        Assert.Equal(standing.LimitingSkillId, again!.LimitingSkillId);
    }

    /// <summary>
    /// When the lowest level AND the lowest position are both shared, the certificate's own order names
    /// the skill. This is the last tie-break, and it exists because position is not unique: two skills
    /// can both sit at B1 50/100.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> any non-deterministic final tie-break -
    /// "first row the query returned" - agrees with this fixture on most runs and disagrees on some.
    /// The assertion is made twice against fresh contexts, and the expected value is the certificate's
    /// display order, which is data rather than insertion order (Writing is written first here).
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_full_tie_is_broken_by_the_certificates_own_display_order()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code,
            ("LIST", "listening"), ("READ", "reading"), ("WRIT", "writing"));
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));

        // Writing is written first, so insertion order points at it; the certificate's order points at
        // Listening. All three are B1 50, so level and position cannot decide.
        await CreateService(clock).RecordAsync(userId, certId, skillIds[2], CefrLevel.B1, 50, 3.50m, LearnerSkillStateSource.Placement);
        await CreateService(clock).RecordAsync(userId, certId, skillIds[1], CefrLevel.B1, 50, 3.50m, LearnerSkillStateSource.Placement);
        var standing = await CreateService(clock).RecordAsync(
            userId, certId, skillIds[0], CefrLevel.B1, 50, 3.50m, LearnerSkillStateSource.Placement);

        Assert.Equal(skillIds[0], standing.LimitingSkillId);
        Assert.Equal("LIST", standing.LimitingSkillCode);

        for (var run = 0; run < 3; run++)
        {
            var again = await CreateService().ReadAsync(userId, certId);
            Assert.Equal(skillIds[0], again!.LimitingSkillId);
        }
    }

    // -------------------------------------------------------------------------------------------
    // AC4: a write from any service other than the owner is refused.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A second service writing the row directly - the shape a mistaken refactor takes, since the table
    /// is in the shared context - is refused by the trigger, not by the published interface.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> the row is valid in every other respect:
    /// it is inside the scale, inside the band, a real source, and its expiry matches its measurement.
    /// A test that also broke a CHECK would be refused before the writer guard ran and would go green
    /// with the guard deleted. The assertion also pins that the message names the rule, so a refusal by
    /// the wrong guard cannot be mistaken for this one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_write_declaring_another_service_is_refused_by_the_single_writer_guard()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code, ("LIST", "listening"));
        var now = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        db.LearnerSkillStates.Add(new LearnerSkillState
        {
            UserId = userId,
            CertId = certId,
            SkillId = skillIds[0],
            CefrLevel = CefrLevel.B1,
            PositionInBand = 50,
            ContinuousCoordinate = 3.50m,
            Source = LearnerSkillStateSource.Calibration,
            MeasuredAt = now,
            ExpiresAt = now.AddDays(90),
            WrittenByService = "CalibrationService",
            ProjectionVersion = 1,
        });

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var message = refused.InnerException?.Message ?? refused.Message;
        Assert.Contains("per-skill state has exactly one writer", message);

        await using var verify = DbContextFactory.Create(database.ConnectionString);
        Assert.False(await verify.LearnerSkillStates.AnyAsync(
            s => s.UserId == userId && s.SkillId == skillIds[0]));
    }

    /// <summary>
    /// The inherited-name case, driven through EF rather than SQL: a tracked row is loaded, only the
    /// position is changed, and the save omits <c>WrittenByService</c> because the value is unchanged.
    /// <para>
    /// <b>The input that passes a value-only guard:</b> the column still holds SkillStateWriter from
    /// the previous write, so a trigger comparing the VALUE passes this statement. It is the
    /// <c>UPDATE(WrittenByService)</c> half that refuses it - and without the explicit
    /// <c>IsModified</c> in <see cref="SkillStateWriter"/> the owner itself would trip this same
    /// guard, which is why the accepted-case test above is the control.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_write_that_changes_only_the_position_without_declaring_is_refused()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code, ("LIST", "listening"));
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        await CreateService(clock).RecordAsync(
            userId, certId, skillIds[0], CefrLevel.B1, 42, 3.62m, LearnerSkillStateSource.Placement);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var row = await db.LearnerSkillStates
            .SingleAsync(s => s.UserId == userId && s.SkillId == skillIds[0]);
        row.PositionInBand = 5;
        row.ProjectionVersion = 2;

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var message = refused.InnerException?.Message ?? refused.Message;
        Assert.Contains("per-skill state has exactly one writer", message);

        await using var verify = DbContextFactory.Create(database.ConnectionString);
        var stored = await verify.LearnerSkillStates.AsNoTracking()
            .SingleAsync(s => s.UserId == userId && s.SkillId == skillIds[0]);
        Assert.Equal(42, stored.PositionInBand);
    }

    /// <summary>
    /// A stale callback cannot overwrite a newer state: a placement result arriving after the learner
    /// confirmed a calibration carries the version it read before, and the trigger refuses it.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> the band it would write is a real one and
    /// the expiry matches its own measurement, so only the ordering is wrong. A guard using
    /// <c>&lt;</c> instead of <c>&lt;=</c> accepts the equal-version case, which is the replay that
    /// overwrites the result it replays.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_stale_projection_version_cannot_overwrite_a_newer_state()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code, ("LIST", "listening"));
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var writer = CreateService(clock);

        await writer.RecordAsync(
            userId, certId, skillIds[0], CefrLevel.B2, 10, 4.10m, LearnerSkillStateSource.Calibration);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var row = await db.LearnerSkillStates
            .SingleAsync(s => s.UserId == userId && s.SkillId == skillIds[0]);
        Assert.Equal(1, row.ProjectionVersion);
        row.CefrLevel = CefrLevel.A2;
        row.ProjectionVersion = 1;                 // the version it read BEFORE the calibration
        db.Entry(row).Property(s => s.WrittenByService).IsModified = true;

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var message = refused.InnerException?.Message ?? refused.Message;
        Assert.Contains("projection version must be newer", message);

        await using var verify = DbContextFactory.Create(database.ConnectionString);
        var stored = await verify.LearnerSkillStates.AsNoTracking()
            .SingleAsync(s => s.UserId == userId && s.SkillId == skillIds[0]);
        Assert.Equal(CefrLevel.B2, stored.CefrLevel);
    }

    // -------------------------------------------------------------------------------------------
    // The refusals the writer makes instead of storing a fabricated value.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// An out-of-band position is refused, not clamped. 140 silently becoming 100 reads as "top of the
    /// band", which is a fabricated result wearing a valid number.
    /// </summary>
    [Fact]
    public async Task Recording_a_position_outside_the_band_is_refused_rather_than_clamped()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code, ("LIST", "listening"));

        var refused = await Assert.ThrowsAsync<SkillStateException>(() => CreateService().RecordAsync(
            userId, certId, skillIds[0], CefrLevel.B1, 140, 3.62m, LearnerSkillStateSource.Placement));

        Assert.Equal(SkillStateException.InvalidPosition, refused.Code);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.False(await db.LearnerSkillStates.AnyAsync(s => s.UserId == userId));
    }

    /// <summary>A coordinate outside the 1.00-6.99 axis is not a measurement of anything.</summary>
    [Fact]
    public async Task Recording_a_coordinate_outside_the_scale_is_refused()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code, ("LIST", "listening"));

        var refused = await Assert.ThrowsAsync<SkillStateException>(() => CreateService().RecordAsync(
            userId, certId, skillIds[0], CefrLevel.C2, 50, 7.10m, LearnerSkillStateSource.Placement));

        Assert.Equal(SkillStateException.OutsideScale, refused.Code);
    }

    /// <summary>
    /// A skill of another certificate is refused, and the message names both ids. The composite foreign
    /// key enforces the same rule; the check here is what turns the database exception into something a
    /// caller can act on, and it is tested because a service that skipped it would still be refused -
    /// by an exception nobody can read.
    /// </summary>
    [Fact]
    public async Task Recording_a_skill_of_another_certificate_is_refused()
    {
        var codeA = Unique("SK211A");
        var codeB = Unique("SK211B");
        var (certA, _, userId) = await SeedAsync(codeA, ("LIST", "listening"));
        var (_, skillsB, _) = await SeedAsync(codeB, ("READ", "reading"));

        var refused = await Assert.ThrowsAsync<SkillStateException>(() => CreateService().RecordAsync(
            userId, certA, skillsB[0], CefrLevel.B1, 50, 3.50m, LearnerSkillStateSource.Placement));

        Assert.Equal(SkillStateException.SkillNotInCertificate, refused.Code);
    }

    // -------------------------------------------------------------------------------------------
    // Expiry, read rather than recomputed.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A result past its 90-day window is still returned - it is the learner's standing - but it is
    /// marked stale, which is what lets a screen print the measurement date and offer a re-measure
    /// instead of presenting an eight-month-old result as today's truth.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> <c>now &gt; ExpiresAt</c> instead of
    /// <c>&gt;=</c>. The two differ only at the exact boundary, so this fixture lands on it: a result
    /// whose window closes at the exact instant of the read. At that instant the window is over, and an
    /// exclusive comparison would call it fresh forever.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_result_at_the_exact_end_of_its_window_is_marked_stale()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code, ("LIST", "listening"));
        var measuredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await CreateService(new FixedClock(measuredAt)).RecordAsync(
            userId, certId, skillIds[0], CefrLevel.B1, 50, 3.50m, LearnerSkillStateSource.Placement);

        var atBoundary = await CreateService(new FixedClock(measuredAt.AddDays(90))).ReadAsync(userId, certId);
        Assert.True(Assert.Single(atBoundary!.Skills).IsStale);

        var oneSecondBefore = await CreateService(new FixedClock(measuredAt.AddDays(90).AddSeconds(-1)))
            .ReadAsync(userId, certId);
        Assert.False(Assert.Single(oneSecondBefore!.Skills).IsStale);
    }

    /// <summary>
    /// The certificate's own order is the order the vector is returned in, so the screen and the
    /// tie-break read the same sequence. Listed here explicitly because the tie-break above depends on
    /// it, and a "sorted by level" vector would make the tie-break a function of the data rather than
    /// of the certificate.
    /// </summary>
    [Fact]
    public async Task The_vector_is_returned_in_the_certificates_own_order()
    {
        var code = Unique("SK211");
        var (certId, skillIds, userId) = await SeedAsync(code,
            ("LIST", "listening"), ("READ", "reading"), ("WRIT", "writing"));
        var writer = CreateService(new FixedClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)));

        // written out of order on purpose, and with levels that would sort differently
        await writer.RecordAsync(userId, certId, skillIds[2], CefrLevel.A2, 10, 2.10m, LearnerSkillStateSource.Placement);
        await writer.RecordAsync(userId, certId, skillIds[0], CefrLevel.C1, 10, 5.10m, LearnerSkillStateSource.Placement);
        await writer.RecordAsync(userId, certId, skillIds[1], CefrLevel.B1, 10, 3.10m, LearnerSkillStateSource.Placement);

        var standing = await writer.ReadAsync(userId, certId);

        Assert.Equal(skillIds, standing!.Skills.Select(s => s.SkillId).ToArray());
    }
}
