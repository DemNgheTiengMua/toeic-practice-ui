using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #233 / D10: the four-week window against the real schema, real triggers and the real planner.
/// <para>
/// Each test gets its own catalog via <see cref="DatabaseFixture.CreateFreshDatabaseAsync"/>, because
/// every one of them writes a <c>LearnerPaths</c> row and the "one active path per (user, cert)" index
/// makes a shared catalog a source of cross-test interference rather than of coverage.
/// </para>
/// <para>
/// The five acceptance criteria, one test each, plus the two traps that are only visible on a second
/// call (idempotence) or a late write (immutability).
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PathWindowSynthesizerTests(DatabaseFixture database)
{
    private static readonly DateOnly Day1 = new(2026, 3, 2);

    private sealed record Seed(int CertId, int SkillId, int TopicId, int UserId, int LearnerPathId);

    /// <summary>
    /// One certificate, one reading skill, four topics with two B2 items each, a lesson, and an active
    /// path whose window opens on <see cref="Day1"/> with four weeks of steps already planned.
    /// </summary>
    private async Task<Seed> SeedAsync(string connStr, DateOnly windowStart, int activeWeek = 1)
    {
        var code = $"WIN{Guid.NewGuid():N}"[..12];
        var email = $"{Guid.NewGuid():N}@window.test";

        await using var db = DbContextFactory.Create(connStr);
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO Roles (Code, Name)
            SELECT 'learner', N'Learner'
            WHERE NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner');
            -- the fixture catalog does not demand an author on unattributed writes (#201)
            IF NOT EXISTS (SELECT 1 FROM CatalogMode WHERE ModeId = 1)
              INSERT INTO CatalogMode (ModeId, IsFixtureCatalog) VALUES (1, 1);

            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule)
            VALUES ('{code}', N'Window cert', 1, 'avg_whole');
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1);

            UPDATE Skills SET MinScaledScore = 0, MaxScaledScore = 990, ScoreStep = 1 WHERE SkillId = @skill;

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours) VALUES
              (@cert, 'A2', N'Elementary',    0, 100, 1, 250),
              (@cert, 'B1', N'Intermediate',101, 500, 2, 500),
              (@cert, 'B2', N'Upper int.',  501, 800, 3, 800);

            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
            VALUES (@cert, 1, 0, N'Test standard-setting', '2024-01-01');
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled) VALUES
              (@cert, 1, @skill, 'A2', 'cut', 0, 100),
              (@cert, 1, @skill, 'B1', 'cut', 101, 500),
              (@cert, 1, @skill, 'B2', 'cut', 501, 990);

            -- the canonical twenty, individually guarded (AGENTS.md: guard every row, not the table)
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

            INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
            VALUES (@cert, @skill, 'reading',
                    (SELECT TopicId FROM Topics WHERE Modality='reading' AND Code='R-GRAM'), 'B2',
                    N'Grammar Foundations', 8, 1);

            DECLARE @t INT, @n INT;
            DECLARE topics_cursor CURSOR LOCAL FAST_FORWARD FOR
              SELECT TopicId FROM Topics WHERE Modality = 'reading' ORDER BY TopicId;
            OPEN topics_cursor;
            FETCH NEXT FROM topics_cursor INTO @t;
            WHILE @@FETCH_STATUS = 0
            BEGIN
              SET @n = 1;
              WHILE @n <= 2
              BEGIN
                INSERT INTO Stimuli (CefrLevel, Passage) VALUES ('B2', N'Passage');
                INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus)
                VALUES (SCOPE_IDENTITY(), 'reading', @t, 'B2', 'passage_comprehension', 0, 'demo', 'active');
                DECLARE @item INT = SCOPE_IDENTITY();
                INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                VALUES (@item, 1, N'Stem', N'A', N'B', N'C', N'D', 'A');
                SET @n = @n + 1;
              END
              FETCH NEXT FROM topics_cursor INTO @t;
            END
            CLOSE topics_cursor; DEALLOCATE topics_cursor;

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code='learner'), '{email}', 0x01, N'Window Learner', @cert);
            DECLARE @u INT = SCOPE_IDENTITY();

            INSERT INTO TargetBands (UserId, CertId, TargetBandCode, CurrentBandCode, TargetDate, WeeklyStudyHours, FeasibilityStatus)
            VALUES (@u, @cert, 'B2', 'B1', DATEADD(DAY, 210, CAST(SYSDATETIMEOFFSET() AS DATE)), 10, 'feasible');

            INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status, WindowStartDate, ActiveWeek, WindowSlides)
            VALUES (@u, @cert, 'B1', 'active', '{windowStart:yyyy-MM-dd}', {activeWeek}, 0);
            """);

        var cert = await db.Certificates.AsNoTracking().SingleAsync(c => c.Code == code);
        var userId = await db.Users.AsNoTracking().Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
        var pathId = await db.LearnerPaths.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.LearnerPathId).SingleAsync();
        var skillId = await db.Skills.AsNoTracking().Where(s => s.CertId == cert.CertId).Select(s => s.SkillId).SingleAsync();
        var topicId = await db.Topics.AsNoTracking().Where(t => t.Modality == "reading" && t.Code == "R-DET").Select(t => t.TopicId).SingleAsync();

        return new Seed(cert.CertId, skillId, topicId, userId, pathId);
    }

    /// <summary>
    /// Writes the path's first four weeks in the shape the planner produces: two steps in each week,
    /// the first available and the rest locked. <paramref name="doneInWeekOne"/> closes that many of
    /// week one's steps through the real closure writer.
    /// </summary>
    private static async Task PlanFourWeeksAsync(
        CompanionDbContext db, Seed seed, int doneInWeekOne = 0)
    {
        var sequence = 0;
        for (var week = 1; week <= PathWindow.Weeks; week++)
        {
            for (var i = 0; i < 2; i++)
            {
                var status = week == 1 && i < doneInWeekOne
                    ? LearnerPathStepStatus.Done.ToCode()
                    : week == 1 && i == 0
                        ? LearnerPathStepStatus.Available.ToCode()
                        : LearnerPathStepStatus.Locked.ToCode();

                var step = new LearnerPathStep
                {
                    LearnerPathId = seed.LearnerPathId,
                    CertId = seed.CertId,
                    SkillId = seed.SkillId,
                    Modality = "reading",
                    TopicId = seed.TopicId,
                    TargetCefr = "B2",
                    StepType = PathStepType.FocusPractice.ToCode(),
                    SizingTier = SizingTier.Developing.ToCode(),
                    Status = status,
                    WeekNumber = week,
                    WeekSequence = ++sequence,
                    PlannedItemCount = 3,
                    EstimatedMinutes = 12,
                    TargetAccuracyPct = 70,
                    WrittenByService = status == LearnerPathStepStatus.Done.ToCode()
                        ? DerivedFactWriters.StepClosure
                        : null,
                    ProjectionVersion = status == LearnerPathStepStatus.Done.ToCode() ? 1 : 0,
                };

                if (step.WrittenByService is not null)
                {
                    step.Score = 80m;
                    step.ActualAccuracyPct = 80m;
                    step.StartedAt = DateTimeOffset.UtcNow.AddDays(-2);
                    step.CompletedAt = DateTimeOffset.UtcNow.AddDays(-1);
                }

                db.LearnerPathSteps.Add(step);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task<int> AdvanceAsync(CompanionDbContext db, Seed seed, DateOnly today) =>
        await new PathWindowSynthesizer(db).AdvanceAsync(
            seed.LearnerPathId,
            today,
            ct => Task.FromResult(LearningPathPlanner.PacingFromGoal(10m)),
            ct => BlocksAsync(db, seed, ct));

    /// <summary>The blocks the planner plans a week from: the seeded skill at B2 with four drillable
    /// topics, weakest first (id order here, because nobody has answered anything yet).</summary>
    private static async Task<IReadOnlyList<SkillPlanBlock>> BlocksAsync(
        CompanionDbContext db, Seed seed, System.Threading.CancellationToken ct)
    {
        var topics = await db.Topics.AsNoTracking()
            .Where(t => t.Modality == "reading")
            .OrderBy(t => t.TopicId)
            .Select(t => t.TopicId)
            .ToListAsync(ct);

        return
        [
            new SkillPlanBlock(
                seed.SkillId,
                Modality.Reading,
                CefrLevel.B2,
                MasteryState.Untested,
                topics,
                LessonId: null,
                MockExamId: null,
                DistanceToTarget: 1.0m),
        ];
    }

    // ---------------------------------------------------------------------------------------------
    // 1. exactly four weeks are live; the window slides and one week is appended
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task The_calendar_closing_the_first_week_archives_it_and_appends_exactly_one_new_week()
    {
        var dbName = $"EEWinSlide_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed);

            // day 8 of the window: the calendar crosses the boundary
            var archived = await AdvanceAsync(db, seed, Day1.AddDays(7));

            Assert.Equal(1, archived);

            var path = await db.LearnerPaths.AsNoTracking().SingleAsync(p => p.LearnerPathId == seed.LearnerPathId);
            Assert.Equal(2, path.ActiveWeek);
            Assert.Equal(Day1.AddDays(7), path.WindowStartDate);
            Assert.Equal(1, path.WindowSlides);

            var weeks = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId)
                .Select(s => s.WeekNumber!.Value)
                .Distinct()
                .OrderBy(w => w)
                .ToListAsync();

            // Week one's rows stay as history; the live window is 2..5, so the oldest live week is
            // gone from the window and the far end is new — and no surviving week was regenerated.
            Assert.Equal([1, 2, 3, 4, 5], weeks);

            // exactly four weeks live at any moment
            var live = weeks.Where(w => PathWindow.IsLive(w, path.ActiveWeek)).ToList();
            Assert.Equal([2, 3, 4, 5], live);
            Assert.Equal(4, live.Count);

            var archivedWeek = await db.LearnerPathWeeks.AsNoTracking()
                .SingleAsync(w => w.LearnerPathId == seed.LearnerPathId);
            Assert.Equal(1, archivedWeek.WeekNumber);
            Assert.Equal(PathWindow.CloseCalendar, archivedWeek.CloseReason);
            Assert.Equal(2, archivedWeek.PlannedSteps);
            Assert.Equal(0, archivedWeek.CompletedSteps);
            Assert.Equal(DerivedFactWriters.WindowSynthesis, archivedWeek.WrittenByService);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The trap "synthesis that regenerates all four weeks": a surviving week keeps its identity, its
    /// plan and everything it has recorded, because only week five is new.
    /// <para>
    /// The one transition a slide may make on a surviving week is <c>locked → available</c>, which is
    /// <c>UnlockNextAsync</c> opening the new first week — not a rewrite of the plan. The assertion
    /// therefore pins the plan fields and the recorded outcome fields exactly, and allows only that
    /// one direction of status change.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_slide_leaves_the_three_surviving_weeks_plan_and_outcomes_identical()
    {
        var dbName = $"EEWinKeep_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed, doneInWeekOne: 1);

            var before = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber >= 2)
                .OrderBy(s => s.StepId)
                .Select(s => new
                {
                    s.StepId, s.WeekNumber, s.WeekSequence, s.StepType, s.SizingTier,
                    s.PlannedItemCount, s.EstimatedMinutes, s.Status, s.Score,
                    s.ActualAccuracyPct, s.CompletedAt, s.ProjectionVersion,
                })
                .ToListAsync();

            await AdvanceAsync(db, seed, Day1.AddDays(7));

            var after = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber >= 2 && s.WeekNumber <= 4)
                .ToDictionaryAsync(s => s.StepId);

            Assert.Equal(before.Count, after.Count);
            foreach (var was in before)
            {
                Assert.True(after.TryGetValue(was.StepId, out var now), $"step {was.StepId} disappeared");

                // the plan is untouched
                Assert.Equal(was.WeekNumber, now!.WeekNumber);
                Assert.Equal(was.WeekSequence, now.WeekSequence);
                Assert.Equal(was.StepType, now.StepType);
                Assert.Equal(was.SizingTier, now.SizingTier);
                Assert.Equal(was.PlannedItemCount, now.PlannedItemCount);
                Assert.Equal(was.EstimatedMinutes, now.EstimatedMinutes);

                // and so is everything it recorded
                Assert.Equal(was.Score, now.Score);
                Assert.Equal(was.ActualAccuracyPct, now.ActualAccuracyPct);
                Assert.Equal(was.CompletedAt, now.CompletedAt);
                Assert.Equal(was.ProjectionVersion, now.ProjectionVersion);

                // the only status move a slide may make is unlocking the new first week
                if (was.Status != now.Status)
                {
                    Assert.Equal("locked", was.Status);
                    Assert.Equal("available", now.Status);
                }
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 2. completed weeks are immutable: a late answer cannot alter one
    // ---------------------------------------------------------------------------------------------
    /// <summary>
    /// The input that would pass a flag-based implementation while being wrong: a well-formed write
    /// that changes only the week's completed count, declared by the owner itself. There is no flag to
    /// consult — the archive row itself refuses, and the step's own closure is refused because its week
    /// is no longer live.
    /// </summary>
    [Fact]
    public async Task A_late_answer_cannot_alter_an_archived_week()
    {
        var dbName = $"EEWinImmutable_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed);

            await AdvanceAsync(db, seed, Day1.AddDays(7));

            var archivedBefore = await db.LearnerPathWeeks.AsNoTracking()
                .SingleAsync(w => w.LearnerPathId == seed.LearnerPathId);
            var weekOneStepsBefore = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber == 1)
                .OrderBy(s => s.StepId)
                .Select(s => new { s.StepId, s.Status, s.Score, s.ActualAccuracyPct, s.CompletedAt })
                .ToListAsync();

            // The late answer: a step in the archived week closes after the fact, exactly as a session
            // finished late would. Its status is `available`, so it is a closeable step on every count
            // except that its week has already closed.
            var step = await db.LearnerPathSteps
                .Where(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber == 1 && s.Status != "done")
                .OrderBy(s => s.StepId)
                .FirstAsync();
            await PathStepCompletion.CloseStepAsync(db, step, score: 100m, accuracyPct: 100m);

            await using var verifyDb = DbContextFactory.Create(connStr);

            // the archive is byte-identical
            var archivedAfter = await verifyDb.LearnerPathWeeks.AsNoTracking()
                .SingleAsync(w => w.LearnerPathId == seed.LearnerPathId);

            Assert.Equal(
                (archivedBefore.WeekNumber, archivedBefore.CompletedSteps, archivedBefore.PlannedSteps,
                    archivedBefore.PlannedMinutes, archivedBefore.AverageAccuracyPct, archivedBefore.ClosedOn,
                    archivedBefore.CloseReason, archivedBefore.ArchivedAt, archivedBefore.ProjectionVersion),
                (archivedAfter.WeekNumber, archivedAfter.CompletedSteps, archivedAfter.PlannedSteps,
                    archivedAfter.PlannedMinutes, archivedAfter.AverageAccuracyPct, archivedAfter.ClosedOn,
                    archivedAfter.CloseReason, archivedAfter.ArchivedAt, archivedAfter.ProjectionVersion));

            // and the archived week's step rows did not move either
            var weekOneStepsAfter = await verifyDb.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber == 1)
                .OrderBy(s => s.StepId)
                .Select(s => new { s.StepId, s.Status, s.Score, s.ActualAccuracyPct, s.CompletedAt })
                .ToListAsync();

            Assert.Equal(weekOneStepsBefore, weekOneStepsAfter);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 3. a step in the closing week keeps its answers; unattempted items return to the pool
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task A_closing_weeks_partial_answers_survive_and_its_unattempted_items_go_back_to_the_pool()
    {
        var dbName = $"EEWinRelease_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed);

            // A drill step in week one holding four slots, one of them answered.
            var step = await db.LearnerPathSteps
                .SingleAsync(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber == 1 && s.Status == "available");
            step.Status = LearnerPathStepStatus.InProgress.ToCode();
            await db.SaveChangesAsync();

            var items = await db.Items.AsNoTracking()
                .Where(i => i.TopicId == seed.TopicId && i.CefrLevel == CefrLevel.B2)
                .OrderBy(i => i.ItemId)
                .Select(i => new { i.ItemId })
                .Take(2)
                .ToListAsync();
            Assert.Equal(2, items.Count);

            var versions = await db.ItemVersions.AsNoTracking()
                .Where(v => items.Select(i => i.ItemId).Contains(v.ItemId))
                .ToDictionaryAsync(v => v.ItemId, v => v.ItemVersionId);

            var session = new PracticeSession
            {
                UserId = seed.UserId,
                CertId = seed.CertId,
                TopicId = seed.TopicId,
                CefrLevel = CefrLevel.B2,
                Modality = "reading",
                QuestionCount = 2,
                Status = PracticeStatus.InProgress.ToCode(),
                QuestionSource = QuestionSource.All.ToCode(),
                LearnerPathStepId = step.StepId,
                StartedAt = DateTimeOffset.UtcNow.AddDays(-2),
            };
            db.PracticeSessions.Add(session);
            await db.SaveChangesAsync();

            // slot 1 answered, slot 2 left unattempted
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                ItemId = items[0].ItemId,
                ItemVersionId = versions[items[0].ItemId],
                SelectedLabel = "A",
                IsCorrect = true,
                DisplayOrder = 1,
                AnsweredAt = DateTime.UtcNow.AddDays(-2),
            });
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                ItemId = items[1].ItemId,
                ItemVersionId = versions[items[1].ItemId],
                DisplayOrder = 2,
            });
            await db.SaveChangesAsync();

            await AdvanceAsync(db, seed, Day1.AddDays(7));

            await using var verifyDb = DbContextFactory.Create(connStr);
            var remaining = await verifyDb.PracticeAnswers.AsNoTracking()
                .Where(a => a.SessionId == session.SessionId)
                .OrderBy(a => a.DisplayOrder)
                .ToListAsync();

            // the answer already given still counts toward capability
            var survived = Assert.Single(remaining);
            Assert.Equal(items[0].ItemId, survived.ItemId);
            Assert.True(survived.IsCorrect);

            // and the unattempted item is back in the pool: nothing holds it to the closed week
            Assert.DoesNotContain(remaining, a => a.ItemId == items[1].ItemId);

            // the session still holds evidence, so it is still resumable and its count is honest
            var reloaded = await verifyDb.PracticeSessions.AsNoTracking().SingleAsync(s => s.SessionId == session.SessionId);
            Assert.Equal(PracticeStatus.InProgress.ToCode(), reloaded.Status);
            Assert.Equal(1, reloaded.QuestionCount);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 4. a partially completed week is archived on schedule and its unmastered topics re-queued
    // ---------------------------------------------------------------------------------------------
    /// <summary>
    /// The input that would pass a "requeued" claim while being wrong: a row written somewhere with
    /// the topic id on it, but no step in the new week naming that topic. The assertion is on the
    /// synthesised week's <b>steps</b>, not on the archive row.
    /// </summary>
    [Fact]
    public async Task A_half_finished_week_is_archived_on_schedule_and_its_unmastered_topic_reappears_in_the_new_week()
    {
        var dbName = $"EEWinRequeue_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);

            // Week one is entirely about R-DET, and only one of its two steps is done.
            await PlanFourWeeksAsync(db, seed, doneInWeekOne: 1);

            // Make the unfinished steps name a DIFFERENT topic, so "the topic is in the new week" is a
            // claim about the re-queue and not about the block's first topic happening to be R-DET.
            var otherTopic = await db.Topics.AsNoTracking()
                .Where(t => t.Modality == "reading" && t.Code == "R-GRAM")
                .Select(t => t.TopicId)
                .SingleAsync();

            var unfinished = await db.LearnerPathSteps
                .Where(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber == 1 && s.Status != "done")
                .ToListAsync();
            Assert.NotEmpty(unfinished);
            foreach (var s in unfinished)
            {
                s.TopicId = otherTopic;
            }
            await db.SaveChangesAsync();

            await AdvanceAsync(db, seed, Day1.AddDays(7));

            var archived = await db.LearnerPathWeeks.AsNoTracking()
                .SingleAsync(w => w.LearnerPathId == seed.LearnerPathId);

            // archived ON SCHEDULE, partially, not carried forward
            Assert.Equal(PathWindow.CloseCalendar, archived.CloseReason);
            Assert.Equal(2, archived.PlannedSteps);
            Assert.Equal(1, archived.CompletedSteps);
            Assert.Contains(otherTopic, RequeuedTopics.Deserialize(archived.RequeuedTopicIdsJson));

            // the topic actually appears in the synthesised week's steps
            var newWeekSteps = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber == 5)
                .OrderBy(s => s.WeekSequence)
                .ToListAsync();
            Assert.NotEmpty(newWeekSteps);

            // and it LEADS the week: the first drill sitting is the topic the closing week left
            // unfinished. A re-queue that only reorders the block can still miss the week's few
            // sittings, which is the ticket's "re-queued but never appears" trap.
            var firstDrill = newWeekSteps.First(s => s.StepType is "focus_practice" or "review_spaced" or "diagnostic_drill");
            Assert.Equal(otherTopic, firstDrill.TopicId);

            // and the unfinished week-one steps were not carried forward: week one stays as it closed
            var weekOne = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber == 1)
                .ToListAsync();
            Assert.Equal(2, weekOne.Count);
            Assert.Single(weekOne, s => s.Status != "done");
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 5. a learner mid-step at a boundary resumes that step
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task A_learner_mid_step_at_the_boundary_still_has_that_step_open_and_resumable()
    {
        var dbName = $"EEWinResume_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed);

            var midStep = await db.LearnerPathSteps
                .SingleAsync(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber == 1 && s.Status == "available");
            midStep.Status = LearnerPathStepStatus.InProgress.ToCode();
            midStep.StartedAt = DateTimeOffset.UtcNow.AddDays(-3);
            await db.SaveChangesAsync();

            await AdvanceAsync(db, seed, Day1.AddDays(7));

            await using var verifyDb = DbContextFactory.Create(connStr);
            var reloaded = await verifyDb.LearnerPathSteps.AsNoTracking()
                .SingleAsync(s => s.StepId == midStep.StepId);

            // not lost, not rebuilt, not closed: it resumes where it was
            Assert.Equal(LearnerPathStepStatus.InProgress.ToCode(), reloaded.Status);
            Assert.Equal(midStep.StartedAt, reloaded.StartedAt);

            // and the learner can open it again through the real service
            var paths = new LearningPathService(verifyDb, new LearnerService(verifyDb));
            var start = await paths.StartStepAsync(seed.UserId, midStep.StepId);
            Assert.Equal(midStep.StepId, start.StepId);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // the "all steps done" condition, and idempotence
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task A_finished_first_week_slides_before_its_calendar_boundary()
    {
        var dbName = $"EEWinAllDone_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed, doneInWeekOne: 2);

            // day two: the calendar has not crossed, but the work is finished
            var archived = await AdvanceAsync(db, seed, Day1.AddDays(1));

            Assert.Equal(1, archived);

            var week = await db.LearnerPathWeeks.AsNoTracking()
                .SingleAsync(w => w.LearnerPathId == seed.LearnerPathId);
            Assert.Equal(PathWindow.CloseAllDone, week.CloseReason);
            Assert.Equal(2, week.CompletedSteps);

            var path = await db.LearnerPaths.AsNoTracking().SingleAsync(p => p.LearnerPathId == seed.LearnerPathId);
            Assert.Equal(2, path.ActiveWeek);

            // the calendar boundary did not move: the next week still ends on the same day eight
            Assert.Equal(Day1, path.WindowStartDate);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// Two slides at the same instant must not synthesise two weeks. The input that would pass a
    /// read-then-write implementation is a second call made while the first call's archive is already
    /// committed but its token has not been observed by the caller.
    /// <para>
    /// A second call with the same <c>today</c> owes no slide on the calendar — <c>WindowStartDate</c>
    /// moved — so that alone proves nothing about the token. The input that isolates it is a caller
    /// that <b>still holds the path it read before the first slide</b>, which is exactly the concurrent
    /// case: the token is the only thing that can stop it, because it computes the same week to close
    /// from the stale copy it is holding.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_callers_holding_the_same_stale_path_archive_one_week()
    {
        var dbName = $"EEWinIdempotent_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed);

            // The second caller read the path BEFORE the first slide committed. Loading it tracked is
            // what reproduces that: EF returns the tracked instance without refreshing it, so this
            // context genuinely holds the pre-slide window while the database has moved on.
            await using var staleDb = DbContextFactory.Create(connStr);
            var stale = await staleDb.LearnerPaths
                .SingleAsync(p => p.LearnerPathId == seed.LearnerPathId);
            Assert.Equal(1, stale.ActiveWeek);          // the tracked copy genuinely is behind
            Assert.Equal(0, stale.WindowSlides);

            var first = await AdvanceAsync(db, seed, Day1.AddDays(7));
            Assert.Equal(1, first);

            var second = await new PathWindowSynthesizer(staleDb).AdvanceAsync(
                seed.LearnerPathId,
                Day1.AddDays(7),
                ct => Task.FromResult(LearningPathPlanner.PacingFromGoal(10m)),
                ct => BlocksAsync(staleDb, seed, ct));

            Assert.Equal(0, second);

            var weeks = await db.LearnerPathWeeks.AsNoTracking()
                .Where(w => w.LearnerPathId == seed.LearnerPathId)
                .CountAsync();
            Assert.Equal(1, weeks);

            var path = await db.LearnerPaths.AsNoTracking().SingleAsync(p => p.LearnerPathId == seed.LearnerPathId);
            Assert.Equal(2, path.ActiveWeek);
            Assert.Equal(1, path.WindowSlides);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// A path with an archived unfinished week can still complete. The input that would pass the
    /// obvious implementation is counting every undone step: the archived week's step is undone
    /// forever, so completion becomes unreachable after any partial archive. Only live steps count.
    /// </summary>
    [Fact]
    public async Task A_path_completes_even_though_an_archived_week_left_a_step_undone()
    {
        var dbName = $"EEWinComplete_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed, doneInWeekOne: 1);

            // week one closes with one of its two steps unfinished
            await AdvanceAsync(db, seed, Day1.AddDays(7));

            // now finish every live step, in order
            var live = await db.LearnerPathSteps
                .Where(s => s.LearnerPathId == seed.LearnerPathId
                    && s.WeekNumber >= 2 && s.Status != "done")
                .OrderBy(s => s.WeekNumber)
                .ThenBy(s => s.WeekSequence)
                .ThenBy(s => s.StepId)
                .ToListAsync();

            foreach (var step in live)
            {
                await db.Entry(step).ReloadAsync();
                if (step.Status == "locked")
                {
                    step.Status = "available";
                    await db.SaveChangesAsync();
                }

                await PathStepCompletion.CloseStepAsync(db, step, score: 80m, accuracyPct: 80m);
            }

            var path = await db.LearnerPaths.AsNoTracking().SingleAsync(p => p.LearnerPathId == seed.LearnerPathId);
            Assert.Equal("completed", path.Status);

            // and the archived week's unfinished step is still unfinished: history was not tidied up
            var archivedUndone = await db.LearnerPathSteps.AsNoTracking()
                .CountAsync(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber == 1 && s.Status != "done");
            Assert.Equal(1, archivedUndone);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The weaker half, still worth pinning: calling twice in a row with the same date is a no-op,
    /// because the first slide moved the window's own start day.
    /// </summary>
    [Fact]
    public async Task Calling_the_slide_twice_in_a_row_archives_one_week()
    {
        var dbName = $"EEWinTwice_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed);

            Assert.Equal(1, await AdvanceAsync(db, seed, Day1.AddDays(7)));
            Assert.Equal(0, await AdvanceAsync(db, seed, Day1.AddDays(7)));

            Assert.Equal(1, await db.LearnerPathWeeks.AsNoTracking()
                .Where(w => w.LearnerPathId == seed.LearnerPathId).CountAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// A long absence owes one close per week it spanned, and the far end keeps up. The input that
    /// would pass a single-slide implementation is a learner who returns after three weeks; the window
    /// must be at week four, not week two.
    /// </summary>
    [Fact]
    public async Task A_three_week_absence_closes_three_weeks_and_leaves_four_live()
    {
        var dbName = $"EEWinAbsence_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed);

            var archived = await AdvanceAsync(db, seed, Day1.AddDays(21));

            Assert.Equal(3, archived);

            var path = await db.LearnerPaths.AsNoTracking().SingleAsync(p => p.LearnerPathId == seed.LearnerPathId);
            Assert.Equal(4, path.ActiveWeek);
            Assert.Equal(Day1.AddDays(21), path.WindowStartDate);

            var weeks = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId)
                .Select(s => s.WeekNumber!.Value)
                .Distinct()
                .ToListAsync();

            var live = weeks.Where(w => PathWindow.IsLive(w, path.ActiveWeek)).Distinct().OrderBy(w => w).ToList();

            // four weeks live, all planned: 4, 5, 6, 7. Week 4's original steps survive; 5-7 are new.
            Assert.Equal([4, 5, 6, 7], live);

            var archiveRows = await db.LearnerPathWeeks.AsNoTracking()
                .Where(w => w.LearnerPathId == seed.LearnerPathId)
                .OrderBy(w => w.WeekNumber)
                .Select(w => w.WeekNumber)
                .ToListAsync();
            Assert.Equal([1, 2, 3], archiveRows);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The trap "WeekSequence colliding across windows": after a slide, the new week's sequences must
    /// be greater than every sequence already used, or UnlockNextAsync orders the new week's first
    /// step before a step the learner has not reached.
    /// </summary>
    [Fact]
    public async Task The_synthesised_week_never_reuses_a_week_sequence()
    {
        var dbName = $"EEWinSequence_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = await SeedAsync(connStr, Day1);
            await using var db = DbContextFactory.Create(connStr);
            await PlanFourWeeksAsync(db, seed);

            var beforeMax = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId)
                .MaxAsync(s => s.WeekSequence);

            await AdvanceAsync(db, seed, Day1.AddDays(7));

            var newWeek = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId && s.WeekNumber == 5)
                .OrderBy(s => s.WeekSequence)
                .ToListAsync();

            Assert.NotEmpty(newWeek);
            Assert.All(newWeek, s => Assert.True(s.WeekSequence > beforeMax,
                $"week 5 step {s.StepId} reused sequence {s.WeekSequence}, which ≤ {beforeMax}"));

            // no duplicate sequence anywhere on the path
            var duplicates = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId)
                .GroupBy(s => s.WeekSequence)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync();
            Assert.Empty(duplicates);

            // and the far end is what UnlockNextAsync opens next: it is still reachable
            var open = await db.LearnerPathSteps.AsNoTracking()
                .Where(s => s.LearnerPathId == seed.LearnerPathId && s.Status != "locked")
                .OrderBy(s => s.WeekNumber)
                .ThenBy(s => s.WeekSequence)
                .FirstOrDefaultAsync();
            Assert.NotNull(open);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }
}
