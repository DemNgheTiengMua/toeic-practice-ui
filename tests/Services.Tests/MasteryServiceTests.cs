using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;

namespace Services.Tests;

/// <summary>
/// #237: per-cell mastery through the real writer, the real schema and the real triggers.
/// <para>
/// The pure arithmetic is <see cref="MasteryEstimateTests"/> and <see cref="MasteryCellMachineTests"/>;
/// the database's own refusals are <c>docs/schema-tests/domain45-mastery.sql</c>. These prove the two
/// halves meet: that <see cref="MasteryService"/> writes a row the triggers accept, that a pool answer
/// moves the cell it was answered in, and that an idle learner's cell decays through the service.
/// </para>
/// <para>
/// Each test seeds its own certificate, learner and topic through <see cref="DatabaseFixture"/>. The
/// topic vocabulary is a closed set of twenty, so a fixture asserts a canonical code rather than
/// inventing one.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MasteryServiceTests(DatabaseFixture database)
{
    private const string ReadingTopicCode = "R-GRAM";
    private const string SpeakingTopicCode = "S-ARG";

    private static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private MasteryService CreateService(TimeProvider clock) =>
        new(DbContextFactory.Create(database.ConnectionString), clock);

    /// <summary>
    /// One learner with an active certificate, and the canonical topics seeded if absent.
    /// <para>
    /// The certificate code is unique per test. A shared code would collide with
    /// <c>CertificateServiceTests</c>, which asserts exactly one TOEIC in the catalog — a fixture that
    /// inserts a second one fails only in a full run, which is the seed-idempotency class of defect.
    /// </para>
    /// </summary>
    private async Task<int> SeedLearnerAsync()
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var certCode = Unique("CM");
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cell Cert', 1);

            -- The canonical twenty. Seeded individually so a table-wide guard cannot make this a no-op
            -- (the #184/#185 class of defect), and only when absent so a re-run is idempotent.
            INSERT INTO Topics (Modality, Code, Name)
            SELECT v.Modality, v.Code, v.Name
            FROM (VALUES
              ('listening','L-GIST',N'Global gist'), ('listening','L-DET',N'Detail'),
              ('listening','L-INF',N'Inference'), ('listening','L-ATT',N'Attitude'),
              ('listening','L-FUNC',N'Function'), ('listening','L-ORG',N'Organisation'),
              ('reading','R-GIST',N'Global thesis'), ('reading','R-DET',N'Local detail'),
              ('reading','R-INF',N'Bridging inference'), ('reading','R-ATT',N'Authorial stance'),
              ('reading','R-COH',N'Cohesion'), ('reading','R-GRAM',N'Grammar'),
              ('speaking','S-DESC',N'Description'), ('speaking','S-INT',N'Interaction'),
              ('speaking','S-ARG',N'Argument'), ('speaking','S-SYN',N'Synthesis'),
              ('writing','W-CORR',N'Correspondence'), ('writing','W-DATA',N'Data'),
              ('writing','W-ARG',N'Argument'), ('writing','W-DISC',N'Discourse')
            ) v(Modality, Code, Name)
            WHERE NOT EXISTS (SELECT 1 FROM Topics t WHERE t.Modality = v.Modality AND t.Code = v.Code);

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Cell Learner',
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<int> TopicIdAsync(string modality, string code)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Topics
            .Where(t => t.Modality == modality && t.Code == code)
            .Select(t => t.TopicId)
            .SingleAsync();
    }

    // ---- the writer -----------------------------------------------------------------------------

    [Fact]
    public async Task An_answer_creates_the_cell_row_and_the_triggers_accept_the_declaration()
    {
        // The regression that would otherwise pass in isolation: EF omits an unchanged column from its
        // UPDATE, so without marking WrittenByService modified the trigger refuses an owned write.
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync("reading", ReadingTopicCode);
        var service = CreateService(new ManualTimeProvider(Start));

        var view = await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: true);

        Assert.Equal(MasteryState.Learning, view.State);
        Assert.Equal(1, view.ObservationCount);
        Assert.Equal(0.6, view.Probability, precision: 10);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var row = await db.UserCellMasteries.AsNoTracking()
            .SingleAsync(m => m.UserId == userId && m.TopicId == topicId && m.CefrLevel == CefrLevel.B2);

        Assert.Equal(DerivedFactWriters.TopicCapability, row.WrittenByService);
        Assert.Equal(1, row.ProjectionVersion);
        Assert.Equal(MasteryState.Learning.ToCode(), row.State);
    }

    [Fact]
    public async Task Twelve_correct_answers_master_the_cell_through_the_service()
    {
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync("reading", ReadingTopicCode);
        var clock = new ManualTimeProvider(Start);
        var service = CreateService(clock);

        CellMasteryView view = default;
        for (var i = 0; i < 12; i++)
        {
            view = await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: true);
        }

        Assert.Equal(MasteryState.Mastered, view.State);
        Assert.Equal(12, view.ObservationCount);
        Assert.NotNull(view.MasteredAt);
        Assert.Equal(12, view.ProjectionVersion);
    }

    [Fact]
    public async Task Eleven_correct_answers_are_refused_by_the_count_even_though_the_threshold_is_met()
    {
        // THE TICKET'S CENTRAL CASE, through the real writer. This is the assertion a service that
        // checked only the probability would fail while every transition test still passed.
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync("reading", ReadingTopicCode);
        var service = CreateService(new ManualTimeProvider(Start));

        CellMasteryView view = default;
        for (var i = 0; i < 11; i++)
        {
            view = await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: true);
        }

        Assert.True(view.Probability >= MasterySchedule.MasteredThreshold);
        Assert.Equal(11, view.ObservationCount);
        Assert.NotEqual(MasteryState.Mastered, view.State);
    }

    [Fact]
    public async Task A_pool_item_at_another_cell_is_credited_to_its_own_cell()
    {
        // #237: the cell is the ITEM's (topic, level), never the session's. A substituted sibling-topic
        // draw serves an item from a different cell, and crediting the session's cell would mark a
        // topic the learner did not practise.
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync("reading", ReadingTopicCode);
        var otherTopicId = await TopicIdAsync("reading", "R-COH");
        var service = CreateService(new ManualTimeProvider(Start));

        await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: true);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cells = await db.UserCellMasteries.AsNoTracking()
            .Where(m => m.UserId == userId)
            .ToListAsync();

        var row = Assert.Single(cells);
        Assert.Equal(topicId, row.TopicId);
        Assert.Equal(CefrLevel.B2, row.CefrLevel);
        Assert.DoesNotContain(cells, m => m.TopicId == otherTopicId);
    }

    [Fact]
    public async Task A_different_level_is_a_different_cell()
    {
        // The cell is (topic, level). Mastering B2 says nothing about B1.
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync("reading", ReadingTopicCode);
        var service = CreateService(new ManualTimeProvider(Start));

        for (var i = 0; i < 12; i++)
        {
            await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: true);
        }

        var b1 = await service.ReadAsync(userId, topicId, CefrLevel.B1);
        var b2 = await service.ReadAsync(userId, topicId, CefrLevel.B2);

        Assert.Null(b1);
        Assert.Equal(MasteryState.Mastered, b2!.Value.State);
    }

    // ---- the lapse ------------------------------------------------------------------------------

    [Fact]
    public async Task Wrong_then_correct_then_wrong_leaves_a_mastered_cell_mastered()
    {
        // The counter half, through the service: a correct answer resets ConsecutiveWrong.
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync("reading", ReadingTopicCode);
        var service = CreateService(new ManualTimeProvider(Start));

        for (var i = 0; i < 12; i++)
        {
            await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: true);
        }

        await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: false);
        var afterCorrect = await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: true);
        Assert.Equal(0, afterCorrect.ConsecutiveWrong);

        var afterSecondWrong = await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: false);

        Assert.Equal(1, afterSecondWrong.ConsecutiveWrong);
        Assert.Equal(MasteryState.Mastered, afterSecondWrong.State);
    }

    [Fact]
    public async Task A_single_wrong_answer_sets_the_review_window_and_keeps_the_state()
    {
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync("reading", ReadingTopicCode);
        var clock = new ManualTimeProvider(Start);
        var service = CreateService(clock);

        for (var i = 0; i < 12; i++)
        {
            await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: true);
        }

        var at = clock.GetUtcNow();
        var afterMiss = await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: false);

        Assert.Equal(MasteryState.Mastered, afterMiss.State);
        Assert.Equal(at + MasterySchedule.ReviewWindow, afterMiss.NextReviewDueAt);
    }

    [Fact]
    public async Task Two_wrong_answers_in_a_row_lapse_the_cell_to_reviewing()
    {
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync("reading", ReadingTopicCode);
        var service = CreateService(new ManualTimeProvider(Start));

        for (var i = 0; i < 12; i++)
        {
            await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: true);
        }

        await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: false);
        var lapsed = await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: false);

        Assert.Equal(MasteryState.Reviewing, lapsed.State);
        Assert.NotNull(lapsed.MasteredAt);   // earned once and kept
    }

    // ---- the decay and the mock-section exception ------------------------------------------------

    /// <summary>Drives one cell to mastered at a known instant, so the decay clock has a fixed origin.</summary>
    private async Task<(int UserId, int TopicId)> MasterCellAsync(string modality, string topicCode, CefrLevel level)
    {
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync(modality, topicCode);
        var service = CreateService(new ManualTimeProvider(Start));
        var minimum = MasteryEstimate.MinimumObservationsFor(
            EnumCodes.Parse<Modality>(modality));

        // The productive minimum is a floor, not a sufficiency: keep going until the estimate also
        // clears the upper cut, which is the rule PLC-68 states and the service enforces.
        var answered = 0;
        CellMasteryView view = default;
        while (view.State != MasteryState.Mastered && answered < 40)
        {
            view = await service.RecordAnswerAsync(userId, topicId, level, isCorrect: true);
            answered++;
        }

        Assert.Equal(MasteryState.Mastered, view.State);

        // Move the row's own practice date back so the 28-day clock starts where the test expects.
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var row = await db.UserCellMasteries
            .SingleAsync(m => m.UserId == userId && m.TopicId == topicId && m.CefrLevel == level);
        row.LastPractisedAt = Start.AddDays(-90);
        row.WrittenByService = DerivedFactWriters.TopicCapability;
        db.Entry(row).Property(m => m.WrittenByService).IsModified = true;
        row.ProjectionVersion++;
        await db.SaveChangesAsync();

        return (userId, topicId);
    }

    [Fact]
    public async Task A_mastered_cell_untouched_for_the_stated_period_decays_to_reviewing_through_the_service()
    {
        var (userId, topicId) = await MasterCellAsync("reading", ReadingTopicCode, CefrLevel.B2);
        var service = CreateService(new ManualTimeProvider(Start));

        var changed = await service.ApplyInactivityAsync(userId, demonstrations: []);

        var cell = Assert.Single(changed);
        Assert.Equal(topicId, cell.TopicId);
        Assert.Equal(MasteryState.Reviewing, cell.State);
        Assert.True(cell.Probability < MasterySchedule.MasteredThreshold);
    }

    [Fact]
    public async Task A_mock_section_at_or_above_the_level_saves_the_cell_from_decay()
    {
        var (userId, _) = await MasterCellAsync("reading", ReadingTopicCode, CefrLevel.B2);
        var service = CreateService(new ManualTimeProvider(Start));

        // A B2 mock in the cell's own skill, sat inside the window: at or above the level, so evidence.
        var demonstrations = new[]
        {
            new MockSectionDemonstration(Modality.Reading, CefrLevel.B2, Start.AddDays(-10)),
        };

        var changed = await service.ApplyInactivityAsync(userId, demonstrations);

        var cell = Assert.Single(changed);
        Assert.Equal(MasteryState.Mastered, cell.State);
    }

    [Fact]
    public async Task A_below_level_mock_section_does_not_save_the_cell()
    {
        // THE TRAP NAMED IN THE TICKET. "Any mock exists" would pass the test above; this is the input
        // that separates the rule. A B1 mock is not evidence about a B2 cell.
        var (userId, _) = await MasterCellAsync("reading", ReadingTopicCode, CefrLevel.B2);
        var service = CreateService(new ManualTimeProvider(Start));

        var demonstrations = new[]
        {
            new MockSectionDemonstration(Modality.Reading, CefrLevel.B1, Start.AddDays(-10)),
        };

        var changed = await service.ApplyInactivityAsync(userId, demonstrations);

        var cell = Assert.Single(changed);
        Assert.Equal(MasteryState.Reviewing, cell.State);
    }

    [Fact]
    public async Task A_mock_section_in_another_skill_does_not_save_the_cell()
    {
        // The other half of the scoping rule: "at or above that level" AND the same skill. A C1
        // listening mock is not evidence about a B2 reading cell.
        var (userId, _) = await MasterCellAsync("reading", ReadingTopicCode, CefrLevel.B2);
        var service = CreateService(new ManualTimeProvider(Start));

        var demonstrations = new[]
        {
            new MockSectionDemonstration(Modality.Listening, CefrLevel.C1, Start.AddDays(-10)),
        };

        var changed = await service.ApplyInactivityAsync(userId, demonstrations);

        var cell = Assert.Single(changed);
        Assert.Equal(MasteryState.Reviewing, cell.State);
    }

    [Fact]
    public async Task A_mock_section_predating_the_last_practice_does_not_save_the_cell()
    {
        // The demonstration must postdate the practice it is standing in for: a mock sat before the
        // learner last drilled the cell says nothing about the work done since.
        var (userId, _) = await MasterCellAsync("reading", ReadingTopicCode, CefrLevel.B2);
        var service = CreateService(new ManualTimeProvider(Start));

        var demonstrations = new[]
        {
            new MockSectionDemonstration(Modality.Reading, CefrLevel.B2, Start.AddDays(-200)),
        };

        var changed = await service.ApplyInactivityAsync(userId, demonstrations);

        var cell = Assert.Single(changed);
        Assert.Equal(MasteryState.Reviewing, cell.State);
    }

    [Fact]
    public async Task A_cell_never_mastered_is_untouched_by_the_inactivity_tick()
    {
        // The trap's integration half: the tick only reads mastered rows, so a weak cell cannot be
        // silently re-graded by the calendar.
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync("reading", ReadingTopicCode);
        var service = CreateService(new ManualTimeProvider(Start));
        await service.RecordAnswerAsync(userId, topicId, CefrLevel.B2, isCorrect: false);

        var changed = await service.ApplyInactivityAsync(userId, demonstrations: []);

        Assert.Empty(changed);
        var stillLearning = await service.ReadAsync(userId, topicId, CefrLevel.B2);
        Assert.Equal(MasteryState.Learning, stillLearning!.Value.State);
    }

    // ---- the productive minimum ----------------------------------------------------------------

    [Fact]
    public async Task A_productive_cell_masters_on_its_own_minimum_and_its_own_threshold()
    {
        // PLC-68's productive minimum is 3 tasks; the estimate also has to clear 0.80, which three
        // perfect tasks (0.714) does not. This asserts the joint rule on the modality that has the
        // smaller floor, so a service reading the objective minimum everywhere would master at 3.
        var userId = await SeedLearnerAsync();
        var topicId = await TopicIdAsync("speaking", SpeakingTopicCode);
        var service = CreateService(new ManualTimeProvider(Start));

        for (var i = 0; i < 3; i++)
        {
            var three = await service.RecordAnswerAsync(
                userId, topicId, CefrLevel.B1, isCorrect: true);
            Assert.Equal(i + 1, three.ObservationCount);
        }

        var afterThree = await service.ReadAsync(userId, topicId, CefrLevel.B1);
        Assert.NotEqual(MasteryState.Mastered, afterThree!.Value.State);
        Assert.True(afterThree.Value.Probability < MasterySchedule.MasteredThreshold);

        var view = afterThree.Value;
        for (var i = 0; i < 4; i++)
        {
            view = await service.RecordAnswerAsync(userId, topicId, CefrLevel.B1, isCorrect: true);
        }

        Assert.Equal(MasteryState.Mastered, view.State);
        Assert.Equal(7, view.ObservationCount);
    }
}
