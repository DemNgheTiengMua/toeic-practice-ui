using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #235 / D11: <b>one selector</b> serves a path step and a free drill, and this class proves it against
/// the real schema — the same fixture drives both origins and asserts the two draws agree.
/// <para>
/// Each test owns a fresh database (<see cref="DatabaseFixture.CreateFreshDatabaseAsync"/>) so the
/// assembly-shared pool cannot make a "no item was returned" assertion pass because a neighbour retired
/// the stock.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemSelectorTests(DatabaseFixture database)
{
    private sealed record Fixture(
        string ConnectionString,
        string DatabaseName,
        int CertId,
        int SkillId,
        int TopicId,
        int SiblingTopicId,
        int LearnerId,
        int EmptyTopicId);

    /// <summary>
    /// One certificate (reading), two reading topics, and one B1 production item each — plus a
    /// placement-eligible sibling in the first topic that must never be served. Item ids are captured so
    /// an assertion can name them.
    /// </summary>
    private async Task<Fixture> SeedAsync(string name)
    {
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        await using var db = DbContextFactory.Create(connectionString);

        await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('SEL235', N'Selector cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local detail');
            DECLARE @topic INT = SCOPE_IDENTITY();
            INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-INF', N'Inference');
            DECLARE @sibling INT = SCOPE_IDENTITY();
            INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GRAM', N'Grammar');
            DECLARE @empty INT = SCOPE_IDENTITY();

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @topic, 'B1', 'sentence_cloze', 0, 'production', 'active', 'sel-own');
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
            VALUES (SCOPE_IDENTITY(), 1, N'Own stem', N'a', N'b', N'c', N'd', 'A');

            -- placement-eligible stock shares the cell by construction, and a drill must never draw it
            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @topic, 'B1', 'sentence_cloze', 1, 'production', 'active', 'sel-placement');
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
            VALUES (SCOPE_IDENTITY(), 1, N'Placement stem', N'a', N'b', N'c', N'd', 'A');

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @sibling, 'B1', 'sentence_cloze', 0, 'production', 'active', 'sel-sibling');
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
            VALUES (SCOPE_IDENTITY(), 1, N'Sibling stem', N'a', N'b', N'c', N'd', 'A');

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'sel235@test.local', 0x01, N'Selector', @cert);
            DECLARE @user INT = SCOPE_IDENTITY();
            """);

        var cert = await db.Certificates.AsNoTracking().Where(c => c.Code == "SEL235").Select(c => c.CertId).SingleAsync();

        return new Fixture(
            connectionString,
            name,
            cert,
            await db.Skills.AsNoTracking().Where(s => s.CertId == cert).Select(s => s.SkillId).SingleAsync(),
            await TopicAsync(db, "R-DET"),
            await TopicAsync(db, "R-INF"),
            await db.Users.AsNoTracking().Where(u => u.Email == "sel235@test.local").Select(u => u.UserId).SingleAsync(),
            await TopicAsync(db, "R-GRAM"));
    }

    private static async Task<int> TopicAsync(CompanionDbContext db, string code) =>
        await db.Topics.AsNoTracking().Where(t => t.Modality == "reading" && t.Code == code).Select(t => t.TopicId).SingleAsync();

    private static async Task<int> ItemAsync(string connectionString, string batchTag)
    {
        await using var db = DbContextFactory.Create(connectionString);
        return await db.Items.AsNoTracking().Where(i => i.BatchTag == batchTag).Select(i => i.ItemId).SingleAsync();
    }

    /// <summary>Write a graded answer for one pool item without going through a session: the selector's
    /// only input is the answer rows, so this is all it needs.</summary>
    private static async Task AnswerAsync(
        string connectionString, int userId, int itemId, bool correct, DateTimeOffset answeredAt)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var topicId = await db.Items.AsNoTracking().Where(i => i.ItemId == itemId).Select(i => i.TopicId).SingleAsync();
        var session = new BusinessObjects.Entities.PracticeSession
        {
            UserId = userId,
            CertId = await db.Certificates.AsNoTracking().Select(c => c.CertId).FirstAsync(),
            TopicId = topicId,
            CefrLevel = CefrLevel.B1,
            Modality = "reading",
            QuestionCount = 1,
            Status = "finished",
            QuestionSource = "all",
            StartedAt = answeredAt,
            FinishedAt = answeredAt,
        };
        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync();

        var versionId = await db.ItemVersions.AsNoTracking()
            .Where(v => v.ItemId == itemId)
            .Select(v => v.ItemVersionId)
            .FirstAsync();

        db.PracticeAnswers.Add(new BusinessObjects.Entities.PracticeAnswer
        {
            SessionId = session.SessionId,
            ItemId = itemId,
            ItemVersionId = versionId,
            SelectedLabel = "A",
            IsCorrect = correct,
            AnsweredAt = answeredAt.UtcDateTime,
            DisplayOrder = 1,
        });
        await db.SaveChangesAsync();
    }

    private static ItemSelector Selector(string connectionString, DateTimeOffset now)
    {
        var db = DbContextFactory.Create(connectionString);
        return new ItemSelector(db, new FixedClock(now));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // ---------------------------------------------------------------------------------------------
    // AC1: one selector serves both origins, filtered by cert, skill, topic and level, and it never
    // draws a placement-eligible item.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The input that would pass a weaker test while being wrong: a selector that omits the placement
    /// filter returns both items and the test asserting "the cell is not empty" still passes. The
    /// assertion is therefore the exact set, and the placement item's id is named.
    /// </summary>
    [Fact]
    public async Task A_draw_never_returns_a_placement_eligible_item()
    {
        var fixture = await SeedAsync($"EESel_{Guid.NewGuid():N}"[..22]);
        try
        {
            var placementItem = await ItemAsync(fixture.ConnectionString, "sel-placement");
            var ownItem = await ItemAsync(fixture.ConnectionString, "sel-own");

            var selection = await Selector(fixture.ConnectionString, DateTimeOffset.UtcNow).SelectAsync(
                fixture.LearnerId, fixture.CertId, [fixture.SkillId], "reading", fixture.TopicId, CefrLevel.B1, "production", 10);

            Assert.Equal([ownItem], selection.Items.Select(i => i.ItemId));
            Assert.DoesNotContain(placementItem, selection.Items.Select(i => i.ItemId));
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// The same selector serves both origins: a path step and a free drill asked for the same coordinate
    /// return the same item. The input that would pass while being wrong is two selectors that agree
    /// today by luck — so the fixture puts a <b>seen</b> and an <b>unseen</b> item in the cell, where the
    /// ranking actually decides, and neither origin may return the seen one first.
    /// </summary>
    [Fact]
    public async Task A_path_step_and_a_free_drill_draw_the_same_item_at_the_same_coordinate()
    {
        var fixture = await SeedAsync($"EESel_{Guid.NewGuid():N}"[..22]);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var own = await ItemAsync(fixture.ConnectionString, "sel-own");

            // the own item has been answered, so it is seen; the sibling's is unseen
            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, own, correct: true, now.AddDays(-30));

            var selector = Selector(fixture.ConnectionString, now);

            var freeDrill = await selector.SelectAsync(
                fixture.LearnerId, fixture.CertId, [fixture.SkillId], "reading", fixture.SiblingTopicId,
                CefrLevel.B1, "production", 10);
            var pathStep = await selector.DrawForStepAsync(
                fixture.LearnerId, fixture.CertId, [fixture.SkillId], "reading", fixture.SiblingTopicId,
                CefrLevel.B1, 10);

            // Both origins, one rule: the same items in the same order.
            Assert.NotEmpty(freeDrill.Items);
            Assert.Equal(
                freeDrill.Items.Select(i => (i.ItemId, i.ItemVersionId)),
                pathStep.Items.Select(i => (i.ItemId, i.ItemVersionId)));
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// Ordering is <b>deterministic and exact</b>: least seen, then least recent, then id. The fixture is
    /// built so the three keys disagree pairwise, and the assertion is the literal sequence — running
    /// twice in one transaction would agree by luck on any stable implementation.
    /// </summary>
    [Fact]
    public async Task A_draw_is_ordered_least_seen_then_least_recent_then_identifier()
    {
        var fixture = await SeedAsync($"EESel_{Guid.NewGuid():N}"[..22]);
        try
        {
            await using var db = DbContextFactory.Create(fixture.ConnectionString);

            // Three more items in the own cell: one seen twice, one seen once recently.
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
                VALUES ('reading', {fixture.TopicId}, 'B1', 'sentence_cloze', 0, 'production', 'active', 'sel-a');
                INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                VALUES (SCOPE_IDENTITY(), 1, N'A', N'a', N'b', N'c', N'd', 'A');
                INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
                VALUES ('reading', {fixture.TopicId}, 'B1', 'sentence_cloze', 0, 'production', 'active', 'sel-b');
                INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                VALUES (SCOPE_IDENTITY(), 1, N'B', N'a', N'b', N'c', N'd', 'A');
                """);

            var now = DateTimeOffset.UtcNow;
            var seenTwice = await ItemAsync(fixture.ConnectionString, "sel-a");
            var seenOnce = await ItemAsync(fixture.ConnectionString, "sel-b");
            var unseen = await ItemAsync(fixture.ConnectionString, "sel-own");

            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, seenTwice, true, now.AddDays(-40));
            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, seenTwice, true, now.AddDays(-30));
            // long enough ago that the 72-hour correct-discrete cooldown has expired: a fixture that
            // answered this two days ago would have it cooling and the test would fail for the wrong
            // reason.
            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, seenOnce, true, now.AddDays(-10));

            var selection = await Selector(fixture.ConnectionString, now).SelectAsync(
                fixture.LearnerId, fixture.CertId, [fixture.SkillId], "reading", fixture.TopicId, CefrLevel.B1, "production", 10);

            // unseen first; then the one seen once; then the one seen twice. The identifiers would give a
            // different order (they are identity-allocated, so the two seen ones sort first).
            Assert.Equal([unseen, seenOnce, seenTwice], selection.Items.Select(i => i.ItemId));
            Assert.True(seenTwice < seenOnce, "the fixture needs the most-seen item to have the lower id");
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // AC2: cooldowns, including the shape difference.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A wrong answer keeps the item out for 24 hours and a correct comprehension answer for 14 days —
    /// at the same moment, so the two lengths cannot be confused. Passing while wrong: one shared
    /// cooldown for both, which serves the passage the learner just read.
    /// </summary>
    [Fact]
    public async Task A_wrong_item_returns_after_24_hours_and_a_correct_comprehension_after_14_days()
    {
        var fixture = await SeedAsync($"EESel_{Guid.NewGuid():N}"[..22]);
        try
        {
            await using var db = DbContextFactory.Create(fixture.ConnectionString);
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
                VALUES ('reading', {fixture.TopicId}, 'B1', 'passage_comprehension', 0, 'production', 'active', 'sel-passage');
                INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                VALUES (SCOPE_IDENTITY(), 1, N'Passage', N'a', N'b', N'c', N'd', 'A');
                """);

            var now = DateTimeOffset.UtcNow;
            var wrong = await ItemAsync(fixture.ConnectionString, "sel-own");
            var comprehension = await ItemAsync(fixture.ConnectionString, "sel-passage");

            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, wrong, correct: false, now.AddHours(-25));
            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, comprehension, correct: true, now.AddDays(-10));

            var selection = await Selector(fixture.ConnectionString, now).SelectAsync(
                fixture.LearnerId, fixture.CertId, [fixture.SkillId], "reading", fixture.TopicId, CefrLevel.B1, "production", 10);

            // The wrong item's 24 hours have passed; the passage's 14 days have not.
            Assert.Contains(wrong, selection.Items.Select(i => i.ItemId));
            Assert.DoesNotContain(comprehension, selection.Items.Select(i => i.ItemId));
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // AC3: a path step is never blocked by a cooldown — it routes around, and says so.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The step's own cell is cooling, so the draw comes from a sibling topic and the outcome says
    /// <see cref="StepDrawOutcome.SubstitutedSiblingTopic"/>. The input that would pass while being
    /// wrong: "routing around" by returning an empty list — the assertion on the sibling item's id is
    /// what refuses that, and the outcome assertion is what refuses silently serving it.
    /// </summary>
    [Fact]
    public async Task A_step_whose_cell_is_cooling_substitutes_a_sibling_topic_and_says_so()
    {
        var fixture = await SeedAsync($"EESel_{Guid.NewGuid():N}"[..22]);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var own = await ItemAsync(fixture.ConnectionString, "sel-own");
            var sibling = await ItemAsync(fixture.ConnectionString, "sel-sibling");

            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, own, correct: false, now.AddHours(-1));

            var draw = await Selector(fixture.ConnectionString, now).DrawForStepAsync(
                fixture.LearnerId, fixture.CertId, [fixture.SkillId], "reading", fixture.TopicId, CefrLevel.B1, 5);

            Assert.Equal(StepDrawOutcome.SubstitutedSiblingTopic, draw.Outcome);
            Assert.Equal([sibling], draw.Items.Select(i => i.ItemId));
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// When the whole band is cooling, the step is still executable: the longest-unseen item of its own
    /// cell is served again and the outcome says the cooldown was lifted. The input that would pass
    /// while being wrong: refusing the step with "no questions", which is the plan arguing with itself
    /// (PLC-60).
    /// </summary>
    [Fact]
    public async Task A_step_is_still_executable_when_its_whole_band_is_cooling()
    {
        var fixture = await SeedAsync($"EESel_{Guid.NewGuid():N}"[..22]);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var own = await ItemAsync(fixture.ConnectionString, "sel-own");
            var sibling = await ItemAsync(fixture.ConnectionString, "sel-sibling");

            // both topics cooling, and the own one was answered less recently than the sibling's
            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, own, correct: false, now.AddHours(-20));
            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, sibling, correct: false, now.AddHours(-1));

            var draw = await Selector(fixture.ConnectionString, now).DrawForStepAsync(
                fixture.LearnerId, fixture.CertId, [fixture.SkillId], "reading", fixture.TopicId, CefrLevel.B1, 5);

            Assert.Equal(StepDrawOutcome.CooldownLifted, draw.Outcome);
            Assert.Equal([own], draw.Items.Select(i => i.ItemId));
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// An empty topic is filled from a sibling topic of the same band: an empty cell is a stock gap, and
    /// P6 refuses to schedule a step that cannot be completed. Passing while being wrong: returning
    /// nothing because the <em>topic</em> is empty, which is the "routes around by returning nothing"
    /// failure the brief names.
    /// </summary>
    [Fact]
    public async Task A_step_whose_topic_is_empty_is_filled_from_a_sibling_topic()
    {
        var fixture = await SeedAsync($"EESel_{Guid.NewGuid():N}"[..22]);
        try
        {
            var draw = await Selector(fixture.ConnectionString, DateTimeOffset.UtcNow).DrawForStepAsync(
                fixture.LearnerId, fixture.CertId, [fixture.SkillId], "reading", fixture.EmptyTopicId, CefrLevel.B1, 5);

            var sibling = await ItemAsync(fixture.ConnectionString, "sel-sibling");
            Assert.Equal(StepDrawOutcome.SubstitutedSiblingTopic, draw.Outcome);
            Assert.Contains(sibling, draw.Items.Select(i => i.ItemId));
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// A band with no active item at all is a stock gap, and says so rather than pretending a
    /// substitution happened.
    /// </summary>
    [Fact]
    public async Task A_step_whose_band_holds_no_item_reports_empty()
    {
        var fixture = await SeedAsync($"EESel_{Guid.NewGuid():N}"[..22]);
        try
        {
            await using var db = DbContextFactory.Create(fixture.ConnectionString);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE Items SET LifecycleStatus = 'retired'
                WHERE Modality = 'reading' AND CefrLevel = 'B1' AND LifecycleStatus = 'active';
                """);

            var draw = await Selector(fixture.ConnectionString, DateTimeOffset.UtcNow).DrawForStepAsync(
                fixture.LearnerId, fixture.CertId, [fixture.SkillId], "reading", fixture.EmptyTopicId, CefrLevel.B1, 5);

            Assert.Equal(StepDrawOutcome.Empty, draw.Outcome);
            Assert.Empty(draw.Items);
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // AC6 / AC7: the daily view moves when answers are written and nothing else is.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// AC7: the view's numbers move when an answer is written and <b>nothing else</b> is. The input that
    /// would pass while being wrong is a view over <c>PracticeSessions</c>, which a session row written
    /// at start already moves — so the assertion is that starting a session moves nothing and answering
    /// one item moves all three counters by exactly one.
    /// </summary>
    [Fact]
    public async Task The_daily_volume_view_moves_only_when_an_answer_is_written()
    {
        var fixture = await SeedAsync($"EESel_{Guid.NewGuid():N}"[..22]);
        try
        {
            await using var db = DbContextFactory.Create(fixture.ConnectionString);
            var today = DateTime.UtcNow.Date;

            async Task<BusinessObjects.Views.vw_DailyPracticeVolume?> VolumeAsync() =>
                await db.vw_DailyPracticeVolumes.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.UserId == fixture.LearnerId && v.PractisedOn == today);

            Assert.Null(await VolumeAsync());

            var own = await ItemAsync(fixture.ConnectionString, "sel-own");
            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, own, correct: true, DateTimeOffset.UtcNow);

            var afterOne = await VolumeAsync();
            Assert.NotNull(afterOne);
            Assert.Equal(1, afterOne!.AnsweredCount);
            Assert.Equal(0, afterOne.ProductiveTaskCount);
            Assert.Equal(1, afterOne.DistinctCells);

            var sibling = await ItemAsync(fixture.ConnectionString, "sel-sibling");
            await AnswerAsync(fixture.ConnectionString, fixture.LearnerId, sibling, correct: true, DateTimeOffset.UtcNow);

            var afterTwo = await VolumeAsync();
            Assert.NotNull(afterTwo);
            Assert.Equal(2, afterTwo!.AnsweredCount);
            // two different cells, so the cell count moved too - a view over sessions alone would say 1
            Assert.Equal(2, afterTwo.DistinctCells);
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// A session row written with all its answers blank — which is exactly what starting a drill does —
    /// contributes nothing to the day. Passing while wrong: counting session rows, which charges a
    /// learner for a drill they never opened.
    /// </summary>
    [Fact]
    public async Task An_unanswered_session_row_contributes_nothing_to_the_day()
    {
        var fixture = await SeedAsync($"EESel_{Guid.NewGuid():N}"[..22]);
        try
        {
            await using var db = DbContextFactory.Create(fixture.ConnectionString);
            var own = await ItemAsync(fixture.ConnectionString, "sel-own");
            var versionId = await db.ItemVersions.AsNoTracking()
                .Where(v => v.ItemId == own).Select(v => v.ItemVersionId).SingleAsync();

            var session = new BusinessObjects.Entities.PracticeSession
            {
                UserId = fixture.LearnerId,
                CertId = fixture.CertId,
                TopicId = fixture.TopicId,
                CefrLevel = CefrLevel.B1,
                Modality = "reading",
                QuestionCount = 1,
                Status = "in_progress",
                QuestionSource = "all",
                StartedAt = DateTimeOffset.UtcNow,
            };
            db.PracticeSessions.Add(session);
            await db.SaveChangesAsync();

            db.PracticeAnswers.Add(new BusinessObjects.Entities.PracticeAnswer
            {
                SessionId = session.SessionId,
                ItemId = own,
                ItemVersionId = versionId,
                DisplayOrder = 1,
            });
            await db.SaveChangesAsync();

            var volume = await db.vw_DailyPracticeVolumes.AsNoTracking()
                .FirstOrDefaultAsync(v => v.UserId == fixture.LearnerId);

            Assert.Null(volume);
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }
}
