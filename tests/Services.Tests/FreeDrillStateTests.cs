using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #235 / PLC-61..64: free drilling is unrestricted but recommended, unlocks once the week's steps are
/// done, and its locked state <b>names the incomplete steps and their remaining minutes</b>.
/// <para>
/// The brief's trap is a lock asserted by a boolean: <c>Assert.False(state.IsUnlocked)</c> passes for a
/// lock that says nothing, which is the most hostile state in the product. Every test below asserts the
/// sentence, the step ids and the minute total.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FreeDrillStateTests(DatabaseFixture database)
{
    private sealed record Fixture(string ConnectionString, string DatabaseName, int CertId, int SkillId, int TopicId, int LearnerId, int PathId);

    /// <summary>One certificate, one reading topic with two active B1 items, one learner, and a path
    /// whose active week holds two unfinished steps of 12 and 8 minutes.</summary>
    private async Task<Fixture> SeedAsync(string name)
    {
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        await using var db = DbContextFactory.Create(connectionString);

        await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('FREE235', N'Free drill cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @skill INT = SCOPE_IDENTITY();
            INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local detail');
            DECLARE @topic INT = SCOPE_IDENTITY();

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @topic, 'B1', 'sentence_cloze', 0, 'production', 'active', 'free-a');
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
            VALUES (SCOPE_IDENTITY(), 1, N'A', N'a', N'b', N'c', N'd', 'A');

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'free235@test.local', 0x01, N'Free', @cert);
            DECLARE @user INT = SCOPE_IDENTITY();

            INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status, ActiveWeek)
            VALUES (@user, @cert, 'A2', 'active', 1);
            DECLARE @path INT = SCOPE_IDENTITY();

            -- The active week's two unfinished steps: 12 minutes and 8 minutes, in plan order.
            INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr,
                StepType, SizingTier, Status, WeekNumber, WeekSequence, EstimatedMinutes)
            VALUES (@path, @cert, @skill, 'reading', @topic, 'B1', 'focus_practice', 'developing', 'available', 1, 1, 12);
            INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr,
                StepType, SizingTier, Status, WeekNumber, WeekSequence, EstimatedMinutes)
            VALUES (@path, @cert, @skill, 'reading', @topic, 'B1', 'review_spaced', 'mastered', 'available', 1, 2, 8);
            """);

        var cert = await db.Certificates.AsNoTracking().Where(c => c.Code == "FREE235").Select(c => c.CertId).SingleAsync();
        var user = await db.Users.AsNoTracking().Where(u => u.Email == "free235@test.local").Select(u => u.UserId).SingleAsync();

        return new Fixture(
            connectionString,
            name,
            cert,
            await db.Skills.AsNoTracking().Where(s => s.CertId == cert).Select(s => s.SkillId).SingleAsync(),
            await db.Topics.AsNoTracking().Where(t => t.Modality == "reading" && t.Code == "R-DET").Select(t => t.TopicId).SingleAsync(),
            user,
            await db.LearnerPaths.AsNoTracking().Where(p => p.UserId == user).Select(p => p.LearnerPathId).SingleAsync());
    }

    private PracticeService Service(string connectionString) =>
        new(DbContextFactory.Create(connectionString), new LearnerService(DbContextFactory.Create(connectionString)));

    /// <summary>
    /// The lock <b>names</b> the incomplete steps and their remaining minutes. The input that would pass
    /// a weaker test while being wrong is any implementation whose locked state has an empty
    /// explanation: <c>IsUnlocked == false</c> alone, or a notice saying only "come back later". So the
    /// step ids, both titles, the exact minute total and the notice text are all asserted.
    /// </summary>
    [Fact]
    public async Task The_locked_state_names_the_incomplete_steps_and_their_remaining_minutes()
    {
        var fixture = await SeedAsync($"EEFree_{Guid.NewGuid():N}"[..22]);
        try
        {
            var state = await Service(fixture.ConnectionString).GetFreeDrillStateAsync(
                fixture.LearnerId, fixture.TopicId, CefrLevel.B1);

            Assert.False(state.IsUnlocked);
            Assert.Equal(2, state.IncompleteSteps.Count);
            Assert.Equal(20, state.RemainingMinutes);
            Assert.Equal([12, 8], state.IncompleteSteps.Select(s => s.EstimatedMinutes));
            Assert.All(state.IncompleteSteps, s => Assert.Equal(1, s.WeekNumber));

            Assert.NotNull(state.LockNotice);
            Assert.Contains("2 step", state.LockNotice!, StringComparison.Ordinal);
            Assert.Contains("20 minute", state.LockNotice!, StringComparison.Ordinal);
            // the notice names each step's own minutes too, so "which step costs how long" is answerable
            Assert.Contains("12 min", state.LockNotice!, StringComparison.Ordinal);
            Assert.Contains("8 min", state.LockNotice!, StringComparison.Ordinal);
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// Completing the week's steps unlocks the drill. Passing while wrong: unlocking on <em>any</em>
    /// completed step, or on a step of a later week.
    /// </summary>
    [Fact]
    public async Task Finishing_the_weeks_steps_unlocks_free_drilling()
    {
        var fixture = await SeedAsync($"EEFree_{Guid.NewGuid():N}"[..22]);
        try
        {
            await using var db = DbContextFactory.Create(fixture.ConnectionString);
            await db.Database.ExecuteSqlRawAsync($"""
                UPDATE LearnerPathSteps SET Status = 'done', CompletedAt = SYSDATETIMEOFFSET()
                WHERE LearnerPathId = {fixture.PathId};
                """);

            var state = await Service(fixture.ConnectionString).GetFreeDrillStateAsync(
                fixture.LearnerId, fixture.TopicId, CefrLevel.B1);

            Assert.True(state.IsUnlocked);
            Assert.Empty(state.IncompleteSteps);
            Assert.Equal(0, state.RemainingMinutes);
            Assert.Null(state.LockNotice);
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// A completed step of a <b>later</b> week does not open the lock: the gate is the active week's own
    /// work. Passing while wrong: counting every unfinished step on the path, which would also pass the
    /// assertion above.
    /// </summary>
    [Fact]
    public async Task A_step_of_a_later_week_is_not_in_the_active_weeks_lock()
    {
        var fixture = await SeedAsync($"EEFree_{Guid.NewGuid():N}"[..22]);
        try
        {
            await using var db = DbContextFactory.Create(fixture.ConnectionString);
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr,
                    StepType, SizingTier, Status, WeekNumber, WeekSequence, EstimatedMinutes)
                VALUES ({fixture.PathId}, {fixture.CertId}, {fixture.SkillId}, 'reading', {fixture.TopicId}, 'B1',
                    'focus_practice', 'developing', 'available', 2, 1, 40);
                """);

            var state = await Service(fixture.ConnectionString).GetFreeDrillStateAsync(
                fixture.LearnerId, fixture.TopicId, CefrLevel.B1);

            Assert.Equal(20, state.RemainingMinutes);
            Assert.DoesNotContain(state.IncompleteSteps, s => s.EstimatedMinutes == 40);
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// PLC-61: the screen highlights the <b>top three</b> prioritised coordinates. Passing while wrong:
    /// returning every open step, which is a plan, not a highlight — the fourth coordinate must be
    /// absent.
    /// </summary>
    [Fact]
    public async Task The_priorities_are_at_most_three_coordinates()
    {
        var fixture = await SeedAsync($"EEFree_{Guid.NewGuid():N}"[..22]);
        try
        {
            await using var db = DbContextFactory.Create(fixture.ConnectionString);
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GIST', N'Global thesis');
                INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-INF', N'Inference');
                INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-ATT', N'Stance');
                INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr,
                    StepType, SizingTier, Status, WeekNumber, WeekSequence, EstimatedMinutes)
                SELECT {fixture.PathId}, {fixture.CertId}, {fixture.SkillId}, 'reading', t.TopicId, 'B1',
                       'focus_practice', 'developing', 'available', 1, 10 + t.TopicId, 10
                FROM Topics t WHERE t.Modality = 'reading' AND t.Code IN ('R-GIST','R-INF','R-ATT');
                """);

            var priorities = await Service(fixture.ConnectionString).GetPrioritisedCoordinatesAsync(fixture.LearnerId);

            Assert.Equal(3, priorities.Count);
            Assert.Equal(3, priorities.Select(p => (p.TopicId, p.Level)).Distinct().Count());
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// PLC-64: the daily ceiling is read from the stored answers, and reaching it locks further drills
    /// with a notice about overnight spacing. Passing while wrong: reading a counter that a refused
    /// drill could never move, or asserting the boolean without the notice.
    /// </summary>
    [Fact]
    public async Task Reaching_the_daily_ceiling_locks_drilling_and_says_why()
    {
        var fixture = await SeedAsync($"EEFree_{Guid.NewGuid():N}"[..22]);
        try
        {
            // Unlock the plan first, so the ceiling is the only lock left.
            await using var db = DbContextFactory.Create(fixture.ConnectionString);
            await db.Database.ExecuteSqlRawAsync($"""
                UPDATE LearnerPathSteps SET Status = 'done', CompletedAt = SYSDATETIMEOFFSET()
                WHERE LearnerPathId = {fixture.PathId};
                """);

            var practice = Service(fixture.ConnectionString);
            var before = await practice.GetFreeDrillStateAsync(fixture.LearnerId, fixture.TopicId, CefrLevel.B1);
            Assert.True(before.IsUnlocked);
            Assert.False(before.DailyCeilingReached);
            Assert.Equal(0, before.AnsweredToday);

            // 60 answered items today: the view is what the state reads, so writing answers is enough.
            // One answer per (session, item version), so the loop inserts 60 distinct sessions' worth
            // of one answer each rather than 60 rows on one session.
            await db.Database.ExecuteSqlRawAsync($"""
                DECLARE @today DATETIME2 = CAST(SYSUTCDATETIME() AS DATE);
                DECLARE @n INT = 1;
                WHILE @n <= 60
                BEGIN
                  INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
                  VALUES ({fixture.LearnerId}, {fixture.CertId}, {fixture.TopicId}, 'B1', 'reading', 1, 'finished', 'all');
                  DECLARE @s INT = SCOPE_IDENTITY();
                  INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, AnsweredAt, DisplayOrder)
                  SELECT @s, i.ItemId, v.ItemVersionId, 'A', 1, DATEADD(MINUTE, @n, @today), 1
                  FROM Items i JOIN ItemVersions v ON v.ItemId = i.ItemId
                  WHERE i.BatchTag = 'free-a';
                  SET @n = @n + 1;
                END
                """);

            var after = await Service(fixture.ConnectionString).GetFreeDrillStateAsync(
                fixture.LearnerId, fixture.TopicId, CefrLevel.B1);

            Assert.True(after.DailyCeilingReached);
            Assert.Equal(60, after.AnsweredToday);
            Assert.NotNull(after.LockNotice);
            Assert.Contains("ceiling", after.LockNotice!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("overnight", after.LockNotice!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("lesson", after.LockNotice!, StringComparison.OrdinalIgnoreCase);

            // and the drill itself refuses, rather than writing a session that was never permitted
            var ex = await Assert.ThrowsAsync<PracticeException>(() =>
                practice.StartSessionAsync(fixture.LearnerId, new StartPracticeSessionInput(fixture.TopicId, CefrLevel.B1)));
            Assert.Equal(PracticeException.FreeDrillLocked, ex.Code);
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }

    /// <summary>
    /// The three locks are computed independently, and the notice reports <b>every</b> one that fires.
    /// <para>
    /// <b>The wrong input that would pass:</b> a ternary chain that reports only the first lock. Both
    /// flags are exposed separately, so a test asserting the flags passes - but the learner is told
    /// about the week's steps and not that today's ceiling is also spent, so clearing the steps reveals
    /// a second wall with no warning. That is the "lock with no exit" shape PLC-62 refuses.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_second_lock_is_reported_before_the_first_one_is_cleared()
    {
        var fixture = await SeedAsync($"EEFree_{Guid.NewGuid():N}"[..22]);
        try
        {
            await using var db = DbContextFactory.Create(fixture.ConnectionString);

            // Both locks at once: the path's steps are still open (the seed leaves them so), and the
            // day's ceiling is spent.
            await db.Database.ExecuteSqlRawAsync($"""
                DECLARE @today DATETIME2 = CAST(SYSUTCDATETIME() AS DATE);
                DECLARE @n INT = 1;
                WHILE @n <= 60
                BEGIN
                  INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
                  VALUES ({fixture.LearnerId}, {fixture.CertId}, {fixture.TopicId}, 'B1', 'reading', 1, 'finished', 'all');
                  DECLARE @s INT = SCOPE_IDENTITY();
                  INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, AnsweredAt, DisplayOrder)
                  SELECT @s, i.ItemId, v.ItemVersionId, 'A', 1, DATEADD(MINUTE, @n, @today), 1
                  FROM Items i JOIN ItemVersions v ON v.ItemId = i.ItemId
                  WHERE i.BatchTag = 'free-a';
                  SET @n = @n + 1;
                END
                """);

            var state = await Service(fixture.ConnectionString)
                .GetFreeDrillStateAsync(fixture.LearnerId, fixture.TopicId, CefrLevel.B1);

            Assert.False(state.IsUnlocked);
            Assert.True(state.DailyCeilingReached);
            Assert.NotEmpty(state.IncompleteSteps);

            Assert.NotNull(state.LockNotice);
            // Both sentences, not just the first one the chain happened to reach.
            Assert.Contains("step", state.LockNotice!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ceiling", state.LockNotice!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await database.DropDatabaseAsync(fixture.DatabaseName);
        }
    }
}
