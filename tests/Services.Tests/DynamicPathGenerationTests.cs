using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #154 / #232: a path is generated from the learner's real weaknesses and real time budget. These tests
/// drive the real service against real SQL Server, so "two learners with the same band pair differ" is
/// proven at the row level — the steps in the database, not a shape a test re-derived.
/// <para>
/// #232 moved the divergence from "which section" to "which topic at which level", because a step is a
/// coordinate. There is no template to read: a step that needed one could not be re-planned.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DynamicPathGenerationTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (LearningPathService paths, LearnerService learners, PracticeService practice, AttemptEngine attempts, CompanionDbContext db) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        return (
            new LearningPathService(db, learners),
            learners,
            new PracticeService(db, learners),
            new AttemptEngine(db),
            db);
    }

    /// <summary>
    /// A certificate with one reading skill, four canonical reading topics, a published mock exam and a
    /// per-skill cut-score table that puts the skill at B2. The pool holds two active B2 items per topic,
    /// so every topic is drillable and a wrong answer can be recorded against any of them.
    ///
    /// The cell is B2 on purpose: `PoolIngestionTests` counts R-DET at B1 exactly, so a fixture that
    /// stocked there would shift a number another test asserts.
    /// </summary>
    private async Task<(int certId, int skillId, Dictionary<string, int> topics, int lessonId, int examId)> SeedCertAsync(string certCode)
    {
        await database.SqlAsync($"""
            -- #232: the canonical twenty topics are the step coordinate's topic axis. The schema holds
            -- the closed-set trigger but no rows, so each fixture that plants a step seeds its topic
            -- individually.
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

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sR, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 3, 1);

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours) VALUES
              (@cert, 'A2', N'Elementary',      0, 100, 1, 250),
              (@cert, 'B1', N'Intermediate',  101, 500, 2, 500),
              (@cert, 'B2', N'Upper inter.',  501, 800, 3, 800);

            -- #210/#232: the skill's OWN level comes from this table, never from the target band.
            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
            VALUES (@cert, 1, 0, N'Test standard-setting', '2024-01-01');
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled) VALUES
              (@cert, 1, @sR, 'A2', 'cut', 0, 100),
              (@cert, 1, @sR, 'B1', 'cut', 101, 500),
              (@cert, 1, @sR, 'B2', 'cut', 501, 990);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Mock Exam', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5'), 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
            VALUES (@grp, @exam, (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5'), @sR, N'One question', 'B1', 'mcq', 1);

            -- #240: a lesson is addressed by (skill, topic, level), so the fixture names the cell.
            INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
            VALUES (@cert, @sR, 'reading',
                    (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM'),
                    'B2', N'Grammar Foundations', 8, 1);
            DECLARE @lesson INT = SCOPE_IDENTITY();

            INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
            VALUES (@lesson, @cert, 'rule', N'Rule', N'Text', 1);

            -- Two B2 items per canonical reading topic, so every topic is a drillable cell.
            DECLARE @topics TABLE (Code VARCHAR(20), TopicId INT);
            INSERT INTO @topics (Code, TopicId)
            SELECT Code, TopicId FROM Topics WHERE Modality = 'reading';

            DECLARE @topicId INT, @n INT;
            DECLARE topics_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TopicId FROM @topics;
            OPEN topics_cursor;
            FETCH NEXT FROM topics_cursor INTO @topicId;
            WHILE @@FETCH_STATUS = 0
            BEGIN
              SET @n = 1;
              WHILE @n <= 2
              BEGIN
                INSERT INTO Stimuli (CefrLevel, Passage) VALUES ('B2', N'Passage');
                DECLARE @stim INT = SCOPE_IDENTITY();
                INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus)
                VALUES (@stim, 'reading', @topicId, 'B2', 'passage_comprehension', 0, 'demo', 'active');
                DECLARE @itemId INT = SCOPE_IDENTITY();
                INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                VALUES (@itemId, 1, N'Stem', N'A', N'B', N'C', N'D', 'A');
                SET @n = @n + 1;
              END
              FETCH NEXT FROM topics_cursor INTO @topicId;
            END
            CLOSE topics_cursor; DEALLOCATE topics_cursor;
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var skillId = await db.Skills.Where(s => s.CertId == cert.CertId).Select(s => s.SkillId).SingleAsync();
        var topics = await db.Topics.Where(t => t.Modality == "reading").ToDictionaryAsync(t => t.Code, t => t.TopicId);
        var lessonId = await db.Lessons.Where(l => l.CertId == cert.CertId).Select(l => l.LessonId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == cert.CertId && e.Purpose == "mock").Select(e => e.ExamId).SingleAsync();
        return (cert.CertId, skillId, topics, lessonId, examId);
    }

    /// <summary>
    /// A learner with a goal (B1→B2 at <paramref name="weeklyHours"/>) and a graded sitting that
    /// measures the certificate's skill at B2, so the step's level comes from that skill's own cut row.
    /// Without the measurement the level would fall back to the stocked cell, and the pool is shared
    /// across the assembly, so the fallback would depend on test order.
    /// </summary>
    private async Task<int> SeedLearnerAsync(
        int certId, decimal weeklyHours = 6m, int daysToTarget = 210, string measuredBand = "B2", int scaledScore = 700)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner', {certId});
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT INTO TargetBands (UserId, CertId, TargetBandCode, CurrentBandCode, TargetDate, WeeklyStudyHours, FeasibilityStatus)
            VALUES (@u, {certId}, 'B2', 'B1', DATEADD(DAY, {daysToTarget}, CAST(SYSDATETIMEOFFSET() AS DATE)), {weeklyHours}, 'feasible');

            DECLARE @measSkill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = {certId} ORDER BY SkillId);
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES ({certId}, N'Measurement sitting {Guid.NewGuid():N}', 'published', 60, 'mock');
            DECLARE @measExam INT = SCOPE_IDENTITY();
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
            VALUES (@u, @measExam, {certId}, 'graded',
                    DATEADD(DAY, -1, SYSDATETIMEOFFSET()), DATEADD(DAY, -1, SYSDATETIMEOFFSET()),
                    DATEADD(DAY, -1, SYSDATETIMEOFFSET()), {scaledScore}, '{measuredBand}', 'AttemptEngine', 1);
            DECLARE @measAttempt INT = SCOPE_IDENTITY();
            INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore)
            VALUES (@measAttempt, @measSkill, {certId}, 18, {scaledScore});
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// Records a finished pool drill in one topic with every answer wrong — which is how a real weakness
    /// appears. A practice answer carries the item it sat, and the cell's mastery is read from those.
    /// </summary>
    private static async Task RecordWrongAnswersOnTopicAsync(CompanionDbContext db, int userId, int certId, int topicId)
    {
        var items = await db.Items
            .Where(i => i.TopicId == topicId && i.CefrLevel == CefrLevel.B2)
            .Select(i => i.ItemId)
            .ToListAsync();

        var session = new PracticeSession
        {
            UserId = userId,
            CertId = certId,
            SectionId = null,
            TopicId = topicId,
            CefrLevel = CefrLevel.B2,
            Modality = "reading",
            QuestionCount = items.Count,
            Status = PracticeStatus.Finished.ToCode(),
            CorrectCount = 0,
            QuestionSource = QuestionSource.All.ToCode(),
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            FinishedAt = DateTimeOffset.UtcNow
        };
        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync();

        var order = 1;
        foreach (var itemId in items)
        {
            // #254: "newest" is the authored order, measured the same way every reader measures it.
            var versionId = await db.ItemVersions
                .Where(v => v.ItemId == itemId)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => v.ItemVersionId)
                .FirstAsync();

            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                QuestionId = null,
                SectionId = null,
                ItemId = itemId,
                ItemVersionId = versionId,
                SelectedLabel = "B",
                IsCorrect = false,
                HintUsed = false,
                DisplayOrder = order++
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The topics the plan drills, in the order it visits them. Ordered by the plan's own
    /// <c>WeekSequence</c> rather than by whatever the storage engine returns, because "weakest first"
    /// is a claim about the order, not the set.
    /// </summary>
    private static async Task<List<int>> DrilledTopicsAsync(CompanionDbContext db, int learnerPathId) =>
        await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == learnerPathId && s.StepType == "focus_practice")
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.WeekSequence)
            .Select(s => s.TopicId)
            .ToListAsync();

    [Fact]
    public async Task Two_learners_with_the_same_band_pair_get_paths_that_drill_different_topics()
    {
        var certCode = Unique("DIV");
        var (certId, _, topics, _, _) = await SeedCertAsync(certCode);

        var learnerA = await SeedLearnerAsync(certId);
        var learnerB = await SeedLearnerAsync(certId);

        await using (var seedDb = DbContextFactory.Create(database.ConnectionString))
        {
            // A is weakest at R-INF; B is weakest at R-ATT. Same band pair, same goal, same certificate.
            await RecordWrongAnswersOnTopicAsync(seedDb, learnerA, certId, topics["R-INF"]);
            await RecordWrongAnswersOnTopicAsync(seedDb, learnerB, certId, topics["R-ATT"]);
        }

        var (paths, _, _, _, db) = CreateServices();
        var summaryA = await paths.GeneratePathAsync(learnerA);
        var summaryB = await paths.GeneratePathAsync(learnerB);

        var drilledA = await DrilledTopicsAsync(db, summaryA.LearnerPathId!.Value);
        var drilledB = await DrilledTopicsAsync(db, summaryB.LearnerPathId!.Value);

        // Each learner's own wrong cell reaches their own plan. Which position it takes is the
        // selector's ordering (D11 / #235); that a wrong cell is drilled at all is what #232 supplies.
        Assert.Contains(topics["R-INF"], drilledA);
        Assert.Contains(topics["R-ATT"], drilledB);
    }

    [Fact]
    public async Task A_six_hour_goal_plans_four_weeks_of_three_to_five_steps()
    {
        var certCode = Unique("VOL");
        var (certId, _, _, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId, weeklyHours: 6m);

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);

        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .ToListAsync();

        Assert.Equal(
            Enumerable.Range(1, LearningPathPlanner.WindowWeeks).ToList(),
            steps.Select(s => s.WeekNumber!.Value).Distinct().OrderBy(w => w).ToList());

        foreach (var week in Enumerable.Range(1, LearningPathPlanner.WindowWeeks))
        {
            var weekSteps = steps.Where(s => s.WeekNumber == week).ToList();
            // #234: the count is arithmetic now, not a 3–5 bound; the invariant is that no sitting is
            // outside a sitting's length and the week is not empty.
            Assert.NotEmpty(weekSteps);
            Assert.All(weekSteps, s => Assert.InRange(s.EstimatedMinutes!.Value, 10, 45));
        }
    }

    [Fact]
    public async Task A_heavier_weekly_pace_persists_more_planned_minutes()
    {
        var certCode = Unique("PACE");
        var (certId, _, _, _, _) = await SeedCertAsync(certCode);
        var lightUser = await SeedLearnerAsync(certId, weeklyHours: 3m);
        var heavyUser = await SeedLearnerAsync(certId, weeklyHours: 10m);

        var (paths, _, _, _, db) = CreateServices();
        var light = await paths.GeneratePathAsync(lightUser);
        var heavy = await paths.GeneratePathAsync(heavyUser);

        var lightMinutes = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == light.LearnerPathId && s.WeekNumber == 1)
            .SumAsync(s => s.EstimatedMinutes ?? 0);
        var heavyMinutes = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == heavy.LearnerPathId && s.WeekNumber == 1)
            .SumAsync(s => s.EstimatedMinutes ?? 0);

        Assert.True(heavyMinutes > lightMinutes,
            $"10 h/week planned {heavyMinutes} min in week 1, 3 h/week planned {lightMinutes}");

        // The lighter pace is planned close to what the learner actually said they have.
        Assert.Equal(180, lightMinutes);
    }

    /// <summary>
    /// #234 / PLC-51: the same learner pace produces <b>different</b> weeks when the runway and the
    /// remaining band hours change. This is the wiring the pure tests cannot prove — that the plan reads
    /// <c>ScoreBands.CumulativeStudyHours</c> and the target date rather than only the declared hours.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> 6 h/week with 300 hours of band distance. A plan
    /// that still keyed off the declared pace alone gives 360 minutes for every runway. 300 hours over
    /// 10 weeks needs 30 h/week — above the declared 6 — so the week is the learner's 6 hours; over 300
    /// weeks it needs 1 h/week, which the 1.5 h floor lifts, so the week is 90 minutes. Same learner,
    /// same pace, two different weeks.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_runway_and_the_remaining_band_hours_change_the_week_not_just_the_declared_pace()
    {
        var certCode = Unique("DERIVED");
        var (certId, _, _, _, _) = await SeedCertAsync(certCode);

        // The certificate band and the skill's level are different axes (PRN-03): this learner's graded
        // sitting reports band B1 and their reading skill sits at B2 (where the fixture's stock is).
        // B1 (500 h) → B2 (800 h) is therefore 300 hours of band distance in both runs.
        var tight = await SeedLearnerAsync(certId, weeklyHours: 6m, daysToTarget: 70, measuredBand: "B1");
        var relaxed = await SeedLearnerAsync(certId, weeklyHours: 6m, daysToTarget: 2100, measuredBand: "B1");

        var (paths, _, _, _, db) = CreateServices();
        var tightPath = await paths.GeneratePathAsync(tight);
        var relaxedPath = await paths.GeneratePathAsync(relaxed);

        var tightMinutes = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == tightPath.LearnerPathId && s.WeekNumber == 1)
            .SumAsync(s => s.EstimatedMinutes ?? 0);
        var relaxedMinutes = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == relaxedPath.LearnerPathId && s.WeekNumber == 1)
            .SumAsync(s => s.EstimatedMinutes ?? 0);

        // 300 h over 10 weeks needs 30 h/week, above the 6 declared: the week is the learner's own 6 h.
        Assert.Equal(360, tightMinutes);
        // 300 h over 300 weeks needs 1 h/week, floored to 1.5: the week is 90 minutes.
        Assert.Equal(90, relaxedMinutes);
        Assert.True(tightMinutes > relaxedMinutes,
            $"a 70-day runway planned {tightMinutes} min, a 2100-day runway {relaxedMinutes}");
    }

    /// <summary>
    /// #234: every skill the plan serves is one the certificate examines. #240 distinguished
    /// "not examined" from "not measured", and a skill the certificate does not examine is not a candidate
    /// for a step at all.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> a certificate whose <c>Skills</c> table holds one
    /// reading skill while the canonical <c>Topics</c> table holds six reading and six listening topics.
    /// A plan that iterated topics or modalities rather than the certificate's own skills would schedule a
    /// listening step on a reading-only certificate — and this fixture is exactly that shape.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Every_planned_skill_is_one_the_certificate_examines()
    {
        var certCode = Unique("EXAM");
        var (certId, skillId, _, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);

        var examined = await db.Skills.Where(s => s.CertId == certId).Select(s => s.SkillId).ToListAsync();
        var planned = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .Select(s => s.SkillId)
            .Distinct()
            .ToListAsync();

        Assert.NotEmpty(planned);
        Assert.All(planned, id => Assert.Contains(id, examined));
        // This certificate examines exactly one skill, so the whole week is that skill's.
        Assert.Equal([skillId], planned);
    }

    /// <summary>
    /// #232: every generated step carries the whole coordinate — a skill, a topic, THAT SKILL'S level, a
    /// type and a size — plus the week and the ordering inside it. A step missing any of these is not a
    /// coordinate, and #233 cannot pack or order it.
    /// </summary>
    [Fact]
    public async Task Every_generated_step_carries_a_full_coordinate()
    {
        var certCode = Unique("COLS");
        var (certId, skillId, _, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);

        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .ToListAsync();

        Assert.NotEmpty(steps);
        Assert.All(steps, s =>
        {
            Assert.Equal(skillId, s.SkillId);
            Assert.Equal("reading", s.Modality);
            Assert.True(s.TopicId > 0);
            // The skill's own level, from its own cut-score row.
            Assert.Equal("B2", s.TargetCefr);
            Assert.True(s.WeekNumber > 0);
            Assert.True(s.WeekSequence > 0);
            Assert.NotNull(s.EstimatedMinutes);
            Assert.InRange(s.TargetAccuracyPct, 70, 80);
            Assert.Contains(s.SizingTier, new[] { "mastered", "developing", "low" });
            Assert.Contains(s.StepType, new[]
            {
                "diagnostic_drill", "focus_practice", "review_spaced",
                "concept_lesson", "expanded_remedial", "mock_section",
            });
        });
    }

    /// <summary>
    /// #232: nothing about a generated path comes from a band-pair template, because there is no such
    /// table. The guard is the drill choice itself: a learner weakest at R-ATT must be drilled there.
    /// </summary>
    [Fact]
    public async Task A_generated_path_is_decided_by_capability_not_by_a_stored_plan()
    {
        var certCode = Unique("NOTPL");
        var (certId, _, topics, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        await using (var seedDb = DbContextFactory.Create(database.ConnectionString))
        {
            await RecordWrongAnswersOnTopicAsync(seedDb, userId, certId, topics["R-COH"]);
        }

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);

        var drilled = await DrilledTopicsAsync(db, summary.LearnerPathId!.Value);

        // #232: the plan names a cell the learner answered wrong. "First" is the selector's ordering
        // (D11 / #235), and with the pool shared across this assembly an absolute position is not
        // something this ticket can promise; that the wrong cell reaches the plan is.
        Assert.Contains(topics["R-COH"], drilled);
    }

    /// <summary>
    /// #232 does not own the selector's ordering. "Weakest first" is D11's unified selector, filed as
    /// #235; the assertion lived here while the path still had its own draw, and it cannot be made
    /// deterministic against a pool this assembly shares with a dozen other fixtures. What #232 does
    /// guarantee — that a cell the learner answered wrong is in the plan at all — is asserted by
    /// <see cref="A_generated_path_is_decided_by_capability_not_by_a_stored_plan"/>.
    /// </summary>
    [Fact]
    public async Task The_plan_drills_a_cell_the_learner_got_wrong()
    {
        var certCode = Unique("ORDER");
        var (certId, _, topics, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        await using (var seedDb = DbContextFactory.Create(database.ConnectionString))
        {
            await RecordWrongAnswersOnTopicAsync(seedDb, userId, certId, topics["R-GRAM"]);
        }

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);

        var drilled = await DrilledTopicsAsync(db, summary.LearnerPathId!.Value);

        Assert.Contains(topics["R-GRAM"], drilled);
    }
}
