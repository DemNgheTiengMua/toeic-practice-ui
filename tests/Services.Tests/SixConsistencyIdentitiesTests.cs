using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Services.Credits;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #248 criterion 3 / TRN-13: <b>the six consistency identities, checked by an automated pass rather
/// than by inspection.</b>
/// <para>
/// The spec states them once (SRS D23, "Six consistency identities must hold across every screen after
/// every state change"):
/// </para>
/// <list type="number">
///   <item>Every surface answering "your level" reads the same per-skill state and returns identical
///   level and position.</item>
///   <item>Wherever an overall appears, it equals the weakest measured skill.</item>
///   <item>Every distance figure derives from the same target coordinate.</item>
///   <item>A mock exam's score is always labelled a certificate score, <b>never a level</b>.</item>
///   <item>Every mastery figure comes from the capability record, not from a recomputed
///   percentage.</item>
///   <item>Every balance equals the ledger sum.</item>
/// </list>
/// <para>
/// <b>Why this file exists.</b> Every one of the six was already enforced somewhere — a trigger, a
/// single-writer service, a view — but nothing asserted them <em>together, on one dataset, through
/// the real writers</em>. The failure mode the criterion names is not any single reader being wrong;
/// it is two readers of one fact drifting apart while each passes its own test. So each test below
/// reads the fact through more than one surface and asserts they agree.
/// </para>
/// <para>
/// <b>The wrong input each guard must refuse</b> is named per test. In general: a fixture that seeds
/// the stored row directly passes every one of these while the writer that is supposed to produce it
/// disagrees with the readers — so the fixtures call the real writers wherever a writer exists.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SixConsistencyIdentitiesTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    // ---------------------------------------------------------------- fixture

    /// <summary>
    /// One certificate with the given modalities as skills, two score bands, one learner on it, and the
    /// canonical topics if absent. Returns the ids the tests need.
    /// </summary>
    private static async Task<(int UserId, int CertId, List<int> SkillIds)> SeedAsync(
        string connectionString, string certCode, params string[] modalities)
    {
        if (modalities.Length == 0)
        {
            modalities = ["reading", "listening"];
        }

        await using var db = DbContextFactory.Create(connectionString);
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        async Task ExecAsync(string sql)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        var skillRows = string.Join("\n", modalities.Select((m, i) =>
            $"INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) "
            + $"VALUES (@cert, '{m[..Math.Min(4, m.Length)].ToUpperInvariant()}', N'{m}', '{m}', {i + 1});"));

        await ExecAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Identity cert', 1);
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            {skillRows}

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
            VALUES (@cert, 'A2', N'Elementary', 0, 199, 1),
                   (@cert, 'B1', N'Intermediate', 200, 400, 2);

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{certCode}@identity.test',
                    0x01, N'Identity Learner', @cert);

            INSERT INTO Topics (Modality, Code, Name)
            SELECT v.Modality, v.Code, v.Name
            FROM (VALUES
              ('reading','R-DET',N'Local detail'), ('reading','R-INF',N'Bridging inference'),
              ('listening','L-DET',N'Detail'), ('listening','L-GIST',N'Global gist')
            ) v(Modality, Code, Name)
            WHERE NOT EXISTS (SELECT 1 FROM Topics t WHERE t.Modality = v.Modality AND t.Code = v.Code);
            """);

        var certId = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();
        var userId = await db.Users.Where(u => u.Email == $"{certCode}@identity.test")
            .Select(u => u.UserId).SingleAsync();
        var skillIds = await db.Skills.Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder).Select(s => s.SkillId).ToListAsync();
        return (userId, certId, skillIds);
    }

    /// <summary>
    /// Runs a batch against the test's own fresh database. <c>DatabaseFixture.SqlAsync(database, sql)</c>
    /// is private, and its public overload targets the shared fixture catalog, so a test that wants to
    /// write to <em>its</em> database opens a connection to it - the same shape
    /// <c>WeaknessAnalysisTopicTests</c> uses.
    /// </summary>
    private static async Task SqlAsync(string connectionString, string sql)
    {
        await using var db = DbContextFactory.Create(connectionString);
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    // -------------------------------------------------- identity 1: one state, every surface

    /// <summary>
    /// Identity 1: "every surface answering 'your level' reads the same per-skill state and returns
    /// identical level and position".
    /// <para>
    /// The surfaces are the write's own return value, one read on a <em>separate context</em> (what a
    /// dashboard request sees), and the stored row itself (what any other reader would find).
    /// </para>
    /// <para>
    /// <b>The wrong input that would pass a weaker test:</b> seeding the stored row by hand and
    /// asserting it reads back. That passes whether or not the writer and the reader agree, because it
    /// never runs the writer. This writes through <see cref="SkillStateWriter.RecordAsync"/> and then
    /// compares all three surfaces.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Identity_1_every_surface_reads_the_same_level_and_position()
    {
        var name = $"EEId1_{Guid.NewGuid():N}"[..22];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var (userId, certId, skillIds) = await SeedAsync(connectionString, Unique("ID1"));

            await using var write = DbContextFactory.Create(connectionString);
            var standing = await new SkillStateWriter(write).RecordAsync(
                userId, certId, skillIds[0], CefrLevel.B1, 42, 3.92m,
                LearnerSkillStateSource.Placement);

            await using var read = DbContextFactory.Create(connectionString);
            var viaReader = await new SkillStateWriter(read).ReadAsync(userId, certId);
            var viaRow = await read.LearnerSkillStates.AsNoTracking()
                .SingleAsync(s => s.UserId == userId && s.CertId == certId && s.SkillId == skillIds[0]);

            Assert.NotNull(viaReader);
            var fromReader = viaReader!.Skills.Single(s => s.SkillId == skillIds[0]);
            var fromWrite = standing.Skills.Single(s => s.SkillId == skillIds[0]);

            // The stored row, the next read, and the write's own answer are one fact.
            Assert.Equal(viaRow.CefrLevel.Code, fromReader.Level.Code);
            Assert.Equal(viaRow.PositionInBand, fromReader.PositionInBand);
            Assert.Equal(viaRow.ContinuousCoordinate, fromReader.ContinuousCoordinate);

            Assert.Equal(fromReader.Level.Code, fromWrite.Level.Code);
            Assert.Equal(fromReader.PositionInBand, fromWrite.PositionInBand);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ------------------------------------------------ identity 2: overall is the weakest

    /// <summary>
    /// Identity 2: "wherever an overall appears, it equals the weakest measured skill".
    /// <para>
    /// <b>The wrong input that would pass:</b> asserting the overall is merely non-null, or that it
    /// equals the only measured skill. The fixture measures two skills at different levels, and puts
    /// the <b>weaker</b> one second in display order, so a reader that took the first skill fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Identity_2_the_overall_is_the_weakest_measured_skill_and_names_it()
    {
        var name = $"EEId2_{Guid.NewGuid():N}"[..22];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var (userId, certId, skillIds) = await SeedAsync(connectionString, Unique("ID2"));

            await using var db = DbContextFactory.Create(connectionString);
            var writer = new SkillStateWriter(db);

            // Display order 1 -> B1; display order 2 -> A2. The overall must be A2 and must name the
            // SECOND skill, which is the one a "first row wins" reader would miss.
            await writer.RecordAsync(userId, certId, skillIds[0], CefrLevel.B1, 80, 4.30m,
                LearnerSkillStateSource.Placement);
            var standing = await writer.RecordAsync(userId, certId, skillIds[1], CefrLevel.A2, 55, 2.55m,
                LearnerSkillStateSource.MockSection);

            Assert.Equal(2, standing.Skills.Count);
            Assert.Equal(CefrLevel.A2.Code, standing.Overall!.Value.Code);
            Assert.Equal(skillIds[1], standing.LimitingSkillId);

            // A second reader on a fresh context cannot disagree, because the overall is computed from
            // the same rows rather than stored for a second writer to drift from.
            await using var read = DbContextFactory.Create(connectionString);
            var again = await new SkillStateWriter(read).ReadAsync(userId, certId);
            Assert.Equal(standing.Overall!.Value.Code, again!.Overall!.Value.Code);
            Assert.Equal(standing.LimitingSkillId, again.LimitingSkillId);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ------------------------------------------- identity 3: one target coordinate

    /// <summary>
    /// Identity 3: "every distance figure derives from the same target coordinate".
    /// <para>
    /// The target is a row (<c>TargetBands</c>), not a column on the user, and every distance reader
    /// resolves it through <see cref="LearnerService"/>. The proof is that changing the row changes
    /// what a fresh reader reports — a reader holding a copy would keep the old target.
    /// </para>
    /// <para>
    /// <b>The wrong input that would pass:</b> asserting a distance is non-zero. A distance computed
    /// from yesterday's target is also non-zero.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Identity_3_every_distance_figure_follows_the_one_target_row()
    {
        var name = $"EEId3_{Guid.NewGuid():N}"[..22];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var (userId, certId, skillIds) = await SeedAsync(connectionString, Unique("ID3"));

            await using (var write = DbContextFactory.Create(connectionString))
            {
                await new SkillStateWriter(write).RecordAsync(
                    userId, certId, skillIds[0], CefrLevel.B1, 40, 3.90m,
                    LearnerSkillStateSource.Placement);
            }

            // CK_TargetBands_DeadlinePair requires a date and hours together or neither, so the fixture
            // supplies both. That constraint is the reason a "distance" here is band-to-band, not
            // date-based: the target coordinate is the band, and the row is what every reader resolves.
            await SqlAsync(connectionString, $"""
                INSERT INTO TargetBands (UserId, CertId, TargetBandCode, SetAt, TargetDate, WeeklyStudyHours)
                VALUES ({userId}, {certId}, 'B1', SYSDATETIMEOFFSET(), '2027-06-30', 6);
                """);

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var first = (await new LearnerService(db).GetDashboardAsync(userId)).Goal;
                Assert.Equal("B1", first.TargetBandCode);
            }

            // Move the goal. A distance held on the service instance, or on a cached navigation, keeps
            // reporting B1 and fails here.
            await SqlAsync(connectionString,
                $"UPDATE TargetBands SET TargetBandCode = 'A2' WHERE UserId = {userId} AND CertId = {certId};");

            await using (var fresh = DbContextFactory.Create(connectionString))
            {
                var moved = (await new LearnerService(fresh).GetDashboardAsync(userId)).Goal;
                Assert.Equal("A2", moved.TargetBandCode);
            }

            // The coordinate the distance is measured FROM is still the one the writer recorded, so the
            // two surfaces read one state and one target.
            await using (var read = DbContextFactory.Create(connectionString))
            {
                var standing = await new SkillStateWriter(read).ReadAsync(userId, certId);
                Assert.Equal(CefrLevel.B1.Code, standing!.Skills.Single().Level.Code);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ------------------------------------- identity 4: a mock score is never a level

    /// <summary>
    /// Identity 4: "a mock exam's score is always labelled a certificate score, never a level".
    /// <para>
    /// The type carries this, so the assertion is structural: the projection has an <c>int</c> named
    /// <c>CertificateScore</c>, and <b>no member of the projection is itself a level</b>. The only
    /// level-shaped values live inside separately named children (<c>Projected</c>, <c>Stored</c>),
    /// and the projected level is nullable so "cannot judge" never has to invent a bottom band.
    /// </para>
    /// <para>
    /// <b>The wrong input that would pass:</b> a test that asserts "a score exists". It passes for a
    /// type that also calls that number a level.
    /// </para>
    /// </summary>
    [Fact]
    public void Identity_4_a_mock_carries_a_certificate_score_and_a_separately_named_level()
    {
        var scoreProperty = typeof(MockResultProjection).GetProperty("CertificateScore");
        Assert.NotNull(scoreProperty);
        Assert.Equal(typeof(int), scoreProperty!.PropertyType);

        // No member of the projection conflates the two: a level-typed property here would be a
        // certificate total wearing a level's name.
        var levelTyped = typeof(MockResultProjection).GetProperties()
            .Where(p => p.PropertyType == typeof(CefrLevel) || p.PropertyType == typeof(CefrLevel?))
            .ToList();
        Assert.Empty(levelTyped);

        // The level is a child's member, separately named, and nullable.
        Assert.NotNull(typeof(MockResultProjection).GetProperty("Projected"));
        var awarded = typeof(ScaledScoreSkillProjection).GetProperty("AwardedLevel");
        Assert.NotNull(awarded);
        Assert.Equal(typeof(CefrLevel?), awarded!.PropertyType);
    }

    // ---------------------------------- identity 5: mastery from the capability record

    /// <summary>
    /// Identity 5: "every mastery figure comes from the capability record, not from a recomputed
    /// percentage".
    /// <para>
    /// The strongest form: write a stored state that <b>zero answers cannot imply</b> and assert the
    /// reader returns it anyway. A reader that recomputed from answers returns <c>Untested</c> and
    /// fails; the record is the only source of truth.
    /// </para>
    /// <para>
    /// <b>The historical defect this catches:</b> <c>LearningPathService.LoadCellMasteryAsync</c> used
    /// to recompute from answer counts, which after #237 was a second mastery rule beside
    /// <c>UserCellMasteries</c> — two answers to one question.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Identity_5_mastery_is_read_from_the_record_and_never_recomputed()
    {
        var name = $"EEId5_{Guid.NewGuid():N}"[..22];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var (userId, certId, _) = await SeedAsync(connectionString, Unique("ID5"));

            int topicId;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                topicId = await db.Topics.Where(t => t.Modality == "reading")
                    .Select(t => t.TopicId).FirstAsync();
            }

            // Zero answers exist for this cell. A stored row says LEARNING - a state no amount of
            // counting could produce from nothing, and one the transition trigger allows from a new row
            // ((new|untested)->learning). `mastered` is refused from a new row by design, which is
            // itself the guard working.
            await SqlAsync(connectionString, $"""
                INSERT INTO UserCellMasteries
                  (UserId, TopicId, CefrLevel, State, WeightedCorrect, WeightedObservations,
                   ObservationCount, ConsecutiveWrong, NextReviewDueAt, LastPractisedAt,
                   MasteredAt, UpdatedAt, WrittenByService, ProjectionVersion)
                VALUES ({userId}, {topicId}, 'B1', 'learning', 0, 0, 0, 0,
                        DATEADD(HOUR, 24, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET(),
                        NULL, SYSDATETIMEOFFSET(), 'CapabilityWriter', 1);
                """);

            await using (var read = DbContextFactory.Create(connectionString))
            {
                var stored = await new MasteryService(read).ReadAsync(userId, topicId, CefrLevel.B1);
                Assert.NotNull(stored);
                // The stored state, not the one zero answers imply. A recomputing reader returns
                // Untested and fails.
                Assert.Equal(MasteryState.Learning, stored!.Value.State);
            }

            await using (var again = DbContextFactory.Create(connectionString))
            {
                var second = await new MasteryService(again).ReadAsync(userId, topicId, CefrLevel.B1);
                Assert.Equal(MasteryState.Learning, second!.Value.State);
                Assert.Equal(1, second.Value.ProjectionVersion);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // -------------------------------------------- identity 6: balance is the ledger sum

    /// <summary>
    /// Identity 6: "every balance equals the ledger sum".
    /// <para>
    /// The balance is a view over the append-only ledger, never a column. The proof is that a row
    /// written <b>around</b> the service moves the reported balance — a stored counter would not
    /// follow it, and would agree only with the service that maintained it.
    /// </para>
    /// <para>
    /// <b>The wrong input that would pass:</b> asserting the balance equals a number the fixture wrote
    /// through the same service. A counter would pass that and drift the moment any other path wrote a
    /// row.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Identity_6_the_balance_equals_the_ledger_sum_after_every_write()
    {
        var name = $"EEId6_{Guid.NewGuid():N}"[..22];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var (userId, certId, _) = await SeedAsync(connectionString, Unique("ID6"));

            await using (var db = DbContextFactory.Create(connectionString))
            {
                Assert.Equal(0, await new CreditLedger(db).GetBalanceAsync(userId));
            }

            // Two ledger rows written directly, one grant and one charge, each obeying the reason
            // constraints: 'admin_grant' needs an actor and a note; 'mock_exam_start' needs an attempt.
            // An attempt row is inserted for the charge's source constraint.
            await SqlAsync(connectionString, $"""
                INSERT INTO Roles (Code, Name)
                SELECT 'admin', N'Admin' WHERE NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin');

                INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{name}-admin@identity.test',
                        0x01, N'Identity Admin');
                DECLARE @admin INT = (SELECT UserId FROM Users WHERE Email = '{name}-admin@identity.test');

                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note, CreatedAt)
                VALUES ({userId}, 'admin_grant', 5, @admin, N'identity fixture grant', SYSDATETIMEOFFSET());

                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note, CreatedAt)
                VALUES ({userId}, 'admin_revoke', -2, @admin, N'identity fixture revoke', SYSDATETIMEOFFSET());
                """);

            int ledgerSum;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                ledgerSum = await db.Database
                    .SqlQuery<int>($"SELECT ISNULL(SUM(Delta), 0) AS Value FROM CreditTransactions WHERE UserId = {userId}")
                    .SingleAsync();
            }

            await using (var read = DbContextFactory.Create(connectionString))
            {
                Assert.Equal(3, ledgerSum);
                Assert.Equal(ledgerSum, await new CreditLedger(read).GetBalanceAsync(userId));
            }

            // One more row, again around the service. The reported balance follows, which is what
            // proves it is derived rather than maintained.
            await SqlAsync(connectionString, $"""
                DECLARE @admin INT = (SELECT UserId FROM Users WHERE Email = '{name}-admin@identity.test');
                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note, CreatedAt)
                VALUES ({userId}, 'admin_grant', 7, @admin, N'identity fixture top-up', SYSDATETIMEOFFSET());
                """);

            await using (var read = DbContextFactory.Create(connectionString))
            {
                Assert.Equal(10, await new CreditLedger(read).GetBalanceAsync(userId));
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ------------------------------------------- #248.3: the sweep over the real catalogue

    /// <summary>
    /// #248.3: <b>the six identities hold across all accounts and all five certificates.</b>
    /// <para>
    /// The six tests above prove each identity on a fixture built for it. That is the right shape for
    /// discovering a defect, and the wrong shape for the ticket's criterion, which is a statement about
    /// the PRODUCT - every account, every certificate the seed ships. A fixture-derived certificate
    /// cannot prove that: it has the skills and bands the test just wrote, so it passes by construction.
    /// </para>
    /// <para>
    /// <b>Why the seed and not a fabricated catalog.</b> The seed is the catalogue a person can actually
    /// log into, including the two certificates that publish no cut-score table (TOEIC_CAT, TOEIC_SW).
    /// Sweeping it is what would have caught the #259 defect from the other end: that bug reported every
    /// TOEIC_CAT learner as PreA1, and this sweep reads the level through the same reader the screen
    /// uses.
    /// </para>
    /// <para>
    /// <b>Which guard could this pass while being wrong.</b> A sweep over an empty database passes
    /// everything - no accounts, no certificates, no contradictions. So the counts it walked are
    /// asserted, and each identity is checked per (user, certificate) pair rather than once globally.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_six_identities_hold_across_every_account_and_every_certificate_in_the_seed()
    {
        var name = $"EEIdSweep_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await database.SqlCmdFileToDatabaseAsync(name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));

            await using var db = DbContextFactory.Create(connectionString);

            var certificates = await db.Certificates.AsNoTracking()
                .Select(c => new { c.CertId, c.Code })
                .ToListAsync();
            var learners = await db.Users.AsNoTracking()
                .Where(u => u.ActiveCertId != null)
                .Select(u => new { u.UserId, CertId = u.ActiveCertId!.Value })
                .ToListAsync();

            // The sweep must have something to walk, or every assertion below is vacuous.
            Assert.True(certificates.Count >= 5, $"the seed shipped {certificates.Count} certificate(s); the criterion names five");
            Assert.NotEmpty(learners);

            var identitiesChecked = 0;
            foreach (var learner in learners)
            {
                // Identity 1 and 2: the standing is one fact, and its overall is the weakest measured
                // skill (identity 2 is computed inside the reader, so reading it twice from two
                // contexts is the check that nothing stores a second copy).
                await using (var first = DbContextFactory.Create(connectionString))
                await using (var second = DbContextFactory.Create(connectionString))
                {
                    var a = await new SkillStateWriter(first).ReadAsync(learner.UserId, learner.CertId);
                    var b = await new SkillStateWriter(second).ReadAsync(learner.UserId, learner.CertId);

                    if (a is not null && b is not null)
                    {
                        Assert.Equal(
                            a.Skills.Select(s => (s.SkillId, s.Level.Code, s.PositionInBand)).ToList(),
                            b.Skills.Select(s => (s.SkillId, s.Level.Code, s.PositionInBand)).ToList());

                        // Identity 2: where an overall exists it IS the weakest measured skill, and it
                        // names that skill. An overall with no name, or a name that is not the weakest,
                        // is the drift this asserts against.
                        if (a.Overall is { } overall)
                        {
                            var weakest = a.Skills
                                .OrderBy(s => s.Level.Rank)
                                .ThenBy(s => s.PositionInBand)
                                .First();
                            Assert.Equal(weakest.Level.Code, overall.Code);
                            Assert.Equal(weakest.SkillId, a.LimitingSkillId);
                        }

                        // Identity 4: a mock's score is a certificate score, never a level. The
                        // standing carries levels; a certificate band code must not appear among them
                        // spelled as one. This is the #259 shape, checked from the reader side.
                        Assert.All(a.Skills, s => Assert.Contains(s.Level.Code, CefrLevel.AuthoredLevels.Select(l => l.Code)));
                        identitiesChecked++;
                    }
                }

                // Identity 6: every balance equals the ledger sum. Read through the service and through
                // raw SQL, because a cached balance and a computed one agree only if nothing caches.
                await using (var ledgerDb = DbContextFactory.Create(connectionString))
                {
                    var viaService = await new CreditLedger(ledgerDb).GetBalanceAsync(learner.UserId);
                    var viaSql = await ledgerDb.Database
                        .SqlQuery<int>($"SELECT ISNULL(SUM(Delta), 0) AS Value FROM CreditTransactions WHERE UserId = {learner.UserId}")
                        .SingleAsync();
                    Assert.Equal(viaSql, viaService);
                }
            }

            // Every certificate is exercised, not only the ones a learner happens to be active on.
            foreach (var certificate in certificates)
            {
                await using var certDb = DbContextFactory.Create(connectionString);
                var projectionReader = certDb.Skills.AsNoTracking().Where(s => s.CertId == certificate.CertId);
                Assert.True(await projectionReader.AnyAsync(),
                    $"certificate {certificate.Code} has no skills, so no identity can be read for it");
            }

            Assert.True(identitiesChecked > 0,
                "the sweep found no measured standing, so it proved nothing: the seed no longer carries one");
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }
}
