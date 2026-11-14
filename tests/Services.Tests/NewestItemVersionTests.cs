#pragma warning disable EF1002, CA2100
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #254: one definition of "an item's newest version", asked by every reader.
/// <para>
/// The schema constrains only <c>VersionNumber &gt; 0</c> and uniqueness per item, so a version can be
/// written in either order and <c>ItemVersionId</c> need not agree with <c>VersionNumber</c>. When the
/// two disagree, the readers of "newest" pick two rows: the learner drills one key and the authoring
/// gate validated another.
/// </para>
/// <para>
/// Every test runs on its own fresh catalog, dropped in <c>finally</c>. The pool is shared by the whole
/// assembly, and a fixture that stocked a certificate, item or version there has already made three
/// full-suite runs fail in this rebuild.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NewestItemVersionTests(DatabaseFixture database)
{
    private sealed record Fixture(
        int TopicId,
        int ItemAId,
        int StimulusId,
        int ItemAVersionOneId,
        int ItemAVersionTwoId,
        int DrillerUserId,
        int PathUserId);

    /// <summary>
    /// One certificate, one reading topic, one stimulus and two active B1 production items in the same
    /// cell.
    /// <para>
    /// Item A carries <b>two</b> versions. Version 1 cites characters 100-140 and carries every
    /// rationale but C's; version 2 cites 600-640 and carries every rationale but B's. The sibling's
    /// span is 110-150, so version 1 of item A overlaps it and version 2 does not - which is how a
    /// gate-four verdict can tell which row was read. The rationale gaps are the same trick for gate
    /// one, whose refusal names the option that lost its rationale.
    /// </para>
    /// <para>
    /// <paramref name="newerVersionFirst"/> is the whole point: writing version 2 before version 1
    /// makes the highest <c>ItemVersionId</c> the <em>older</em> version.
    /// </para>
    /// </summary>
    private async Task<(string ConnectionString, Fixture Fixture)> SeedAsync(
        string databaseName, string certCode, bool newerVersionFirst)
    {
        const string versionTwo = """
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD,
                KeyLabel, Explanation, RationaleA, RationaleC, RationaleD, EvidenceStart, EvidenceEnd, EvidenceUnit)
            VALUES (@itemA, 2, N'The revised stem.', N'alpha', N'bravo', N'charlie', N'delta', 'A',
                N'The revised explanation.', N'Revised distractor A.', N'Revised distractor C.',
                N'Revised distractor D.', 600, 640, 'char');
            """;

        const string versionOne = """
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD,
                KeyLabel, Explanation, RationaleA, RationaleB, RationaleD, EvidenceStart, EvidenceEnd, EvidenceUnit)
            VALUES (@itemA, 1, N'The original stem.', N'alpha', N'bravo', N'charlie', N'delta', 'B',
                N'The original explanation.', N'Original distractor A.', N'Original distractor B.',
                N'Original distractor D.', 100, 140, 'char');
            """;

        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        await using var db = DbContextFactory.Create(connectionString);

        await db.Database.ExecuteSqlRawAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Newest version', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Topics (Modality, Code, Name)
            VALUES ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching');
            DECLARE @topic INT = SCOPE_IDENTITY();

            INSERT INTO Stimuli (CefrLevel, Passage, DomainContext)
            VALUES ('B1', REPLICATE(N'x', 800), 'general');
            DECLARE @stim INT = SCOPE_IDENTITY();

            INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, PlacementEligible,
                ContentTier, LifecycleStatus, BatchTag)
            VALUES (@stim, 'reading', @topic, 'B1', 'passage_comprehension', 0, 'production', 'active', '{certCode}-a');
            DECLARE @itemA INT = SCOPE_IDENTITY();

            INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, PlacementEligible,
                ContentTier, LifecycleStatus, BatchTag)
            VALUES (@stim, 'reading', @topic, 'B1', 'passage_comprehension', 0, 'production', 'active', '{certCode}-b');
            DECLARE @sibling INT = SCOPE_IDENTITY();

            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD,
                KeyLabel, Explanation, RationaleA, RationaleB, RationaleC, RationaleD,
                EvidenceStart, EvidenceEnd, EvidenceUnit)
            VALUES (@sibling, 1, N'The sibling stem.', N'echo', N'foxtrot', N'golf', N'hotel', 'C',
                N'The sibling explanation.', N'Sibling distractor A.', N'Sibling distractor B.',
                N'Sibling distractor C.', N'Sibling distractor D.', 110, 150, 'char');

            {(newerVersionFirst ? versionTwo + "\n" + versionOne : versionOne + "\n" + versionTwo)}

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'),
                    '{certCode}-drill@test.local', 0x01, N'Driller', @cert);
            DECLARE @drillUser INT = SCOPE_IDENTITY();
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'),
                    '{certCode}-path@test.local', 0x01, N'Path Learner', @cert);
            DECLARE @pathUser INT = SCOPE_IDENTITY();

            -- The path drill's own step: a coordinate over this cell, open and ready to start.
            INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status)
            VALUES (@pathUser, @cert, 'A2', 'active');
            DECLARE @path INT = SCOPE_IDENTITY();
            INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr,
                StepType, SizingTier, Status, WeekNumber, WeekSequence, PlannedItemCount)
            VALUES (@path, @cert, @skill, 'reading', @topic, 'B1', 'focus_practice', 'developing',
                'available', 1, 1, 2);
            """);

        var itemA = await db.Items.AsNoTracking()
            .Where(i => i.BatchTag == $"{certCode}-a").Select(i => i.ItemId).SingleAsync();
        var versions = await db.ItemVersions.AsNoTracking()
            .Where(v => v.ItemId == itemA)
            .Select(v => new { v.VersionNumber, v.ItemVersionId })
            .ToListAsync();

        return (connectionString, new Fixture(
            await db.Topics.Where(t => t.Modality == "reading" && t.Code == "R-DET")
                .Select(t => t.TopicId).SingleAsync(),
            itemA,
            await db.Items.Where(i => i.ItemId == itemA).Select(i => i.StimulusId).SingleAsync() ?? 0,
            versions.Single(v => v.VersionNumber == 1).ItemVersionId,
            versions.Single(v => v.VersionNumber == 2).ItemVersionId,
            await db.Users.Where(u => u.Email == $"{certCode}-drill@test.local").Select(u => u.UserId).SingleAsync(),
            await db.Users.Where(u => u.Email == $"{certCode}-path@test.local").Select(u => u.UserId).SingleAsync()));
    }

    /// <summary>
    /// The defect: version 2 written before version 1. Every reader of "newest" must name version 2 -
    /// gate one's refusal names the rationale version 2 leaves out, gate four sees version 2's span
    /// that clears its sibling, and both selectors pin version 2.
    /// </summary>
    [Fact]
    public async Task A_lower_version_written_after_a_higher_one_is_read_the_same_way_by_every_reader()
    {
        var databaseName = $"EENewest_{Guid.NewGuid():N}"[..25];
        var certCode = $"NV{Guid.NewGuid():N}"[..10];
        try
        {
            var (connectionString, fixture) = await SeedAsync(databaseName, certCode, newerVersionFirst: true);

            // What the row is worth: the highest identity is the OLDER version, so a reader ordering
            // by insertion serves the row gate one did not judge.
            Assert.True(
                fixture.ItemAVersionOneId > fixture.ItemAVersionTwoId,
                "the fixture must write version 2 before version 1, or both orders agree and this test proves nothing");

            await AssertEveryReaderNamesVersionTwoAsync(connectionString, fixture);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    /// <summary>
    /// The accepting control. On ordinary data - version 1 then version 2 - every reader still names
    /// version 2, so a "fix" that always picks the lowest row does not pass. Between this and the test
    /// above, only an ordering that reads <c>VersionNumber</c> satisfies both.
    /// </summary>
    [Fact]
    public async Task On_ascending_versions_every_reader_still_names_the_higher_one()
    {
        var databaseName = $"EENewest_{Guid.NewGuid():N}"[..25];
        var certCode = $"NV{Guid.NewGuid():N}"[..10];
        try
        {
            var (connectionString, fixture) = await SeedAsync(databaseName, certCode, newerVersionFirst: false);

            // Here the two orders agree, which is why this cannot be the only test: it passes on the
            // implementation the ticket reports as wrong.
            Assert.True(
                fixture.ItemAVersionOneId < fixture.ItemAVersionTwoId,
                "the accepting control needs ascending versions");

            await AssertEveryReaderNamesVersionTwoAsync(connectionString, fixture);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    /// <summary>The live readers, each through its own public seam, all naming version 2.</summary>
    private static async Task AssertEveryReaderNamesVersionTwoAsync(string connectionString, Fixture fixture)
    {
        await using var db = DbContextFactory.Create(connectionString);

        // ---- the drill selector -------------------------------------------------------------
        var drilled = await DrillAnswersAsync(connectionString, fixture.DrillerUserId, fixture.TopicId);
        Assert.Equal(fixture.ItemAVersionTwoId, drilled[fixture.ItemAId]);

        // ---- the path selector --------------------------------------------------------------
        var fromPath = await PathAnswersAsync(connectionString, fixture.PathUserId);
        Assert.Equal(fixture.ItemAVersionTwoId, fromPath[fixture.ItemAId]);

        // and they named one row, stated as one equality rather than as four literals
        Assert.Equal(drilled[fixture.ItemAId], fromPath[fixture.ItemAId]);
    }

    /// <summary>The version each item of the cell was pinned to by a real pool drill.</summary>
    private static async Task<Dictionary<int, int>> DrillAnswersAsync(
        string connectionString, int userId, int topicId)
    {
        await using var db = DbContextFactory.Create(connectionString);
        var practice = new PracticeService(db, new LearnerService(db));
        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(topicId, CefrLevel.B1));
        return await AnswerPinsAsync(db, session.SessionId);
    }

    private static async Task<Dictionary<int, int>> PathAnswersAsync(string connectionString, int userId)
    {
        await using var db = DbContextFactory.Create(connectionString);
        var paths = new LearningPathService(db, new LearnerService(db));
        var stepId = await db.LearnerPathSteps
            .Where(s => s.LearnerPath.UserId == userId && s.StepType == "focus_practice")
            .Select(s => s.StepId)
            .SingleAsync();

        var started = await paths.StartStepAsync(userId, stepId);
        return await AnswerPinsAsync(db, started.SessionId!.Value);
    }

    private static async Task<Dictionary<int, int>> AnswerPinsAsync(CompanionDbContext db, int sessionId) =>
        await db.PracticeAnswers.AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .ToDictionaryAsync(a => a.ItemId!.Value, a => a.ItemVersionId!.Value);
}
