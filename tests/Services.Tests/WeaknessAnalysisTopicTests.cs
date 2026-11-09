using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataAccess;
using BusinessObjects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #243 / D3: the topic-and-level axis of the weakness page.
/// <para>
/// <b>What these tests are for.</b> The guards below are about a statement the page makes about a
/// person: "your weakest topic is X at level Y". The inputs that would satisfy a weaker test are named
/// on each one, because the interesting defects here are not crashes - a page that ranks an
/// unevidenced cell, or a page that divides in C#, renders a confident sentence and passes every
/// "did it return data" assertion.
/// </para>
/// <para>
/// Each test seeds a certificate, a learner and its answers into a database of its own
/// (<see cref="DatabaseFixture.CreateFreshDatabaseAsync"/>) and drops it in <c>finally</c>, so a test
/// can assert on an exact cell list without another test's rows in the way.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class WeaknessAnalysisTopicTests(DatabaseFixture database)
{
    private const int ObjectiveFloor = 12;
    private const int ProductiveFloor = 3;

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private static WeaknessAnalysisService Service(string connectionString)
    {
        var db = DbContextFactory.Create(connectionString);
        return new WeaknessAnalysisService(db, new LearnerService(db));
    }

    /// <summary>
    /// One certificate, one learner on it, and the canonical topics if absent. <paramref name="modalities"/>
    /// becomes one skill per modality, so a test can build a certificate that examines one skill or several.
    /// Returns the learner id.
    /// </summary>
    private static async Task<(int UserId, int CertId)> SeedAsync(
        string connectionString, string certCode, params string[] modalities)
    {
        if (modalities.Length == 0)
        {
            modalities = ["reading"];
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
            $"INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, '{m[..Math.Min(4, m.Length)].ToUpperInvariant()}', N'{m}', '{m}', {i + 1});"));

        await ExecAsync($"""
            INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Topic cert', 1);
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            {skillRows}
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{certCode}@x.test', 0x01, N'Topic Learner', @cert);

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
            """);

        var certId = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();
        var userId = await db.Users.Where(u => u.Email == $"{certCode}@x.test").Select(u => u.UserId).SingleAsync();
        return (userId, certId);
    }

    /// <summary>
    /// Seeds <paramref name="answered"/> pool answer rows in one cell, of which <paramref name="correct"/>
    /// are right. <paramref name="answeredAtStartOnly"/> writes the rows with every answer column null,
    /// which is exactly what <c>PracticeService</c> writes when a drill is opened.
    /// </summary>
    private static async Task SeedCellAsync(
        string connectionString, int userId, int certId, string modality, string topicCode, string level,
        int answered, int correct, bool answeredAtStartOnly = false)
    {
        await using var db = DbContextFactory.Create(connectionString);
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"""
                DECLARE @cert INT = {certId};
                DECLARE @user INT = {userId};
                DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = '{modality}' AND Code = '{topicCode}');
                DECLARE @n INT = {answered};
                DECLARE @i INT = 1;

                INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
                VALUES (@user, @cert, @topic, '{level}', '{modality}', @n, 'in_progress', 'all');
                DECLARE @session INT = SCOPE_IDENTITY();

                WHILE @i <= @n
                BEGIN
                  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, RenderFormat)
                  VALUES ('{modality}', @topic, '{level}',
                          CASE WHEN '{modality}' = 'reading' THEN 'sentence_cloze' ELSE 'transactional_correspondence' END,
                          'demo', 'active',
                          CASE WHEN '{modality}' IN ('speaking','writing') THEN 'free_response' ELSE 'mcq' END);
                  DECLARE @item INT = SCOPE_IDENTITY();

                  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                  VALUES (@item, 1, N'stem',
                          CASE WHEN '{modality}' IN ('speaking','writing') THEN NULL ELSE N'a' END,
                          CASE WHEN '{modality}' IN ('speaking','writing') THEN NULL ELSE N'b' END,
                          CASE WHEN '{modality}' IN ('speaking','writing') THEN NULL ELSE N'c' END,
                          CASE WHEN '{modality}' IN ('speaking','writing') THEN NULL ELSE N'd' END,
                          CASE WHEN '{modality}' IN ('speaking','writing') THEN NULL ELSE 'A' END);
                  DECLARE @version INT = SCOPE_IDENTITY();

                  -- An answer row with every answer column null is what a drill start writes.
                  INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, DisplayOrder)
                  VALUES (@session, @item, @version, @i);

                  SET @i = @i + 1;
                END;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        if (answeredAtStartOnly)
        {
            return;
        }

        // Grade the cell: the first `correct` answers right, the rest wrong, in one statement so a
        // reader cannot see a half-graded cell.
        await using var grade = connection.CreateCommand();
        grade.CommandText = $"""
            DECLARE @session INT = (SELECT MAX(SessionId) FROM PracticeSessions WHERE UserId = {userId});

            WITH ordered AS (
              SELECT AnswerId, ROW_NUMBER() OVER (ORDER BY DisplayOrder) AS rn
              FROM PracticeAnswers WHERE SessionId = @session
            )
            UPDATE pa
            SET IsCorrect = CASE WHEN o.rn <= {correct} THEN 1 ELSE 0 END,
                SelectedLabel = CASE WHEN o.rn <= {correct} THEN 'A' ELSE 'B' END
            FROM PracticeAnswers pa JOIN ordered o ON o.AnswerId = pa.AnswerId;
            """;
        await grade.ExecuteNonQueryAsync();
    }

    // ------------------------------------------------------------------------------------------
    // criterion 5: an empty cell cannot be selected as the weakest
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The wrong input this catches:</b> three drill rows written at start and never answered - the
    /// shape a learner leaves behind by opening a drill and closing the tab. A cell "ranked" from those
    /// answers reads 0%, sorts first, and the page tells the learner their weakest topic is one they
    /// have never been graded on. The contradicted input is the same page with the rows excluded.
    /// </summary>
    [Fact]
    public async Task An_empty_cell_cannot_be_selected_as_the_weakest()
    {
        var name = $"Wk243Empty_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certCode = Unique("WKE");
            var (userId, certId) = await SeedAsync(connectionString, certCode);

            // A cell with 12 graded answers at 50%: rankable, and the only real evidence.
            await SeedCellAsync(connectionString, userId, certId, "reading", "R-DET", "B1",
                ObjectiveFloor, ObjectiveFloor / 2);

            // A cell with three rows and no verdicts: zero evidence, and numerically 0%.
            await SeedCellAsync(connectionString, userId, certId, "reading", "R-ATT", "B2",
                answeredAtStartOnly: true, answered: 3, correct: 0);

            var view = await Service(connectionString).GetWeaknessAnalysisAsync(userId);

            Assert.NotNull(view.WeakestTopic);
            Assert.Equal("R-DET", view.WeakestTopic!.TopicCode);
            Assert.Equal("B1", view.WeakestTopic.CefrLevel.Code);

            // And the unanswered cell is not on the page at all - not merely unranked.
            Assert.DoesNotContain(view.Topics, t => t.TopicCode == "R-ATT");
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ------------------------------------------------------------------------------------------
    // criterion 2: too few observations is excluded rather than ranked
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The wrong input this catches:</b> treating the floor as <c>Answered &gt; 0</c>. That is not
    /// "too few", it is "none", and it lets a cell with two wrong answers (0%) outrank a cell with
    /// twelve answers at 50% - which is the page naming the learner's weakest topic from two clicks.
    /// The input that would pass a weaker test is a fixture where the shortest cell is also the worst
    /// <i>rankable</i> one, so exclusion and ranking agree by accident.
    /// </summary>
    [Fact]
    public async Task A_cell_below_the_observation_floor_is_listed_but_never_ranked()
    {
        var name = $"Wk243Floor_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certCode = Unique("WKF");
            var (userId, certId) = await SeedAsync(connectionString, certCode);

            // Worst accuracy, too little evidence: 2 answers, both wrong (0%).
            await SeedCellAsync(connectionString, userId, certId, "reading", "R-ATT", "B2",
                answered: 2, correct: 0);

            // Better accuracy, enough evidence: 12 answers, 7 correct (58%).
            await SeedCellAsync(connectionString, userId, certId, "reading", "R-GRAM", "B1",
                answered: ObjectiveFloor, correct: 7);

            var view = await Service(connectionString).GetWeaknessAnalysisAsync(userId);

            Assert.NotNull(view.WeakestTopic);
            Assert.Equal("R-GRAM", view.WeakestTopic!.TopicCode);

            // The short cell is still reported, with its count, so the learner is told why it is not
            // ranked rather than seeing it vanish.
            var shortCell = Assert.Single(view.Topics, t => t.TopicCode == "R-ATT");
            Assert.Equal(2, shortCell.Answered);
            Assert.False(shortCell.IsWeakest);
            Assert.Equal(ObjectiveFloor, shortCell.ObservationFloor);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The floor follows the modality (PLC-68): three tasks are enough for a productive cell, and
    /// three answers are not enough for an objective one. <b>The wrong input this catches:</b> one
    /// floor for every cell, which either suppresses a productive weakness that is real or admits an
    /// objective one that is not.
    /// </summary>
    [Fact]
    public async Task The_floor_is_three_tasks_for_a_productive_cell_and_twelve_items_for_an_objective_one()
    {
        var name = $"Wk243Mod_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certCode = Unique("WKM");
            var (userId, certId) = await SeedAsync(connectionString, certCode, "reading", "writing");

            // Productive: 3 tasks, all wrong. Rankable at the productive floor.
            await SeedCellAsync(connectionString, userId, certId, "writing", "W-ARG", "B1",
                answered: ProductiveFloor, correct: 0);

            // Objective: 3 items, all wrong. NOT rankable at 12.
            await SeedCellAsync(connectionString, userId, certId, "reading", "R-INF", "B1",
                answered: 3, correct: 0);

            var view = await Service(connectionString).GetWeaknessAnalysisAsync(userId);

            var writing = Assert.Single(view.Topics, t => t.TopicCode == "W-ARG");
            Assert.Equal(ProductiveFloor, writing.ObservationFloor);
            Assert.Equal(3, writing.Answered);
            Assert.Equal(0, writing.Correct);
            Assert.True(writing.IsWeakest);
            Assert.Equal("W-ARG", view.WeakestTopic!.TopicCode);

            var reading = Assert.Single(view.Topics, t => t.TopicCode == "R-INF");
            Assert.Equal(ObjectiveFloor, reading.ObservationFloor);
            Assert.False(reading.IsWeakest);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ------------------------------------------------------------------------------------------
    // criterion 1: the accuracy is the view's, not the service's arithmetic
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The wrong input this catches:</b> a service that divides <c>Correct</c> by <c>Answered</c>
    /// with integer truncation. 2 of 3 is 67 by the view's rounding and 66 by truncation, so a cell
    /// whose figure cannot be produced by a correct division is the only way to tell the two apart -
    /// a fixture of 1 of 2 reads 50 both ways and proves nothing.
    /// </summary>
    [Fact]
    public async Task Accuracy_is_the_views_rounding_and_never_recomputed_in_the_service()
    {
        var name = $"Wk243Pct_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certCode = Unique("WKP");
            var (userId, certId) = await SeedAsync(connectionString, certCode);

            await SeedCellAsync(connectionString, userId, certId, "reading", "R-COH", "B1",
                answered: 3, correct: 2);

            await using var db = DbContextFactory.Create(connectionString);
            var viewRow = await db.vw_UserTopicAccuracies
                .AsNoTracking()
                .SingleAsync(v => v.UserId == userId && v.CefrLevel == "B1");
            Assert.Equal(67, viewRow.AccuracyPercent);

            var view = await Service(connectionString).GetWeaknessAnalysisAsync(userId);
            var cell = Assert.Single(view.Topics, t => t.TopicCode == "R-COH");
            Assert.Equal(viewRow.AccuracyPercent, cell.AccuracyPercent);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ------------------------------------------------------------------------------------------
    // criterion 3: a topic with no items is never a weakness
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The wrong input this catches:</b> ranking the canonical twenty topics and zero-filling the
    /// ones with no items. A zero-filled topic reads 0% and wins, which is precisely the defect SRS §2
    /// item 3 records ("twelve of the fifteen categories held no items at all, so 'your weakest topic'
    /// was a statement about an empty set").
    /// </summary>
    [Fact]
    public async Task A_topic_with_no_items_is_never_reported()
    {
        var name = $"Wk243NoItem_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certCode = Unique("WKN");
            var (userId, certId) = await SeedAsync(connectionString, certCode);

            await SeedCellAsync(connectionString, userId, certId, "reading", "R-DET", "B1",
                answered: ObjectiveFloor, correct: 6);

            var view = await Service(connectionString).GetWeaknessAnalysisAsync(userId);

            // R-GIST, R-INF, R-ATT, R-COH, R-GRAM hold no items in this fixture. None may be a row,
            // and none may be the weakest.
            Assert.All(view.Topics, t => Assert.Equal("R-DET", t.TopicCode));
            Assert.DoesNotContain(view.Topics, t => t.TopicCode is "R-GIST" or "R-INF" or "R-ATT" or "R-COH" or "R-GRAM");
            Assert.Equal("R-DET", view.WeakestTopic!.TopicCode);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Certificate isolation without a <c>CertId</c> on a topic: a reading-only certificate must not be
    /// told its weakest topic is a writing one. <b>The wrong input this catches:</b> a fixture with one
    /// modality, where the filter is unobservable because there is nothing for it to exclude.
    /// </summary>
    [Fact]
    public async Task A_topic_outside_the_certificates_skills_is_not_reported()
    {
        var name = $"Wk243Cert_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certCode = Unique("WKC");
            var (userId, certId) = await SeedAsync(connectionString, certCode, "reading");

            // A writing cell, worse than the reading one, on a certificate that does not examine it.
            await SeedCellAsync(connectionString, userId, certId, "writing", "W-ARG", "B1",
                answered: ProductiveFloor, correct: 0);
            await SeedCellAsync(connectionString, userId, certId, "reading", "R-DET", "B1",
                answered: ObjectiveFloor, correct: 9);

            var view = await Service(connectionString).GetWeaknessAnalysisAsync(userId);

            Assert.DoesNotContain(view.Topics, t => t.TopicCode == "W-ARG");
            Assert.Equal("R-DET", view.WeakestTopic!.TopicCode);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ------------------------------------------------------------------------------------------
    // criterion 4: the lesson link is for the cell, not the topic
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The wrong input this catches:</b> linking a weakness to <i>any</i> lesson of its topic. Two
    /// lessons for the same topic at different levels is the only fixture that can tell the two apart:
    /// with one lesson per topic, a link that ignores the level is indistinguishable from one that
    /// respects it.
    /// </summary>
    [Fact]
    public async Task A_weakness_links_to_the_lesson_for_its_own_topic_and_level()
    {
        var name = $"Wk243Lesson_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certCode = Unique("WKL");
            var (userId, certId) = await SeedAsync(connectionString, certCode);

            // Same topic, two levels, two lessons: B1 is weaker and must win, and must carry B1's lesson.
            await SeedCellAsync(connectionString, userId, certId, "reading", "R-DET", "B1",
                answered: ObjectiveFloor, correct: 3);
            await SeedCellAsync(connectionString, userId, certId, "reading", "R-DET", "B2",
                answered: ObjectiveFloor, correct: 11);

            int b1Lesson, b2Lesson;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                await using var connection = db.Database.GetDbConnection(); await connection.OpenAsync();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"""
                    DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = {certId});
                    DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET');
                    INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
                    VALUES ({certId}, @skill, 'reading', @topic, 'B1', N'Detail at B1', 5, 1);
                    DECLARE @b1 INT = SCOPE_IDENTITY();
                    INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
                    VALUES ({certId}, @skill, 'reading', @topic, 'B2', N'Detail at B2', 5, 2);
                    DECLARE @b2 INT = SCOPE_IDENTITY();
                    SELECT @b1, @b2;
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                await reader.ReadAsync();
                b1Lesson = reader.GetInt32(0);
                b2Lesson = reader.GetInt32(1);
            }

            var view = await Service(connectionString).GetWeaknessAnalysisAsync(userId);

            Assert.Equal("B1", view.WeakestTopic!.CefrLevel.Code);
            Assert.Equal(b1Lesson, view.WeakestTopic.LessonId);
            Assert.NotEqual(b2Lesson, view.WeakestTopic.LessonId);

            // And the stronger B2 cell points at B2's lesson, so the link is not "the first lesson of
            // the topic" either.
            var b2Cell = Assert.Single(view.Topics, t => t.CefrLevel.Code == "B2");
            Assert.Equal(b2Lesson, b2Cell.LessonId);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The half of criterion 4 that a fixture with lessons everywhere cannot see: a cell with no lesson
    /// reports no link, rather than borrowing a neighbour's. <b>The wrong input this catches:</b> a
    /// fallback to the topic's first lesson, which looks right on any seeded database.
    /// </summary>
    [Fact]
    public async Task A_cell_with_no_lesson_reports_no_link()
    {
        var name = $"Wk243NoLesson_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certCode = Unique("WKNL");
            var (userId, certId) = await SeedAsync(connectionString, certCode);

            await SeedCellAsync(connectionString, userId, certId, "reading", "R-DET", "B1",
                answered: ObjectiveFloor, correct: 4);

            var view = await Service(connectionString).GetWeaknessAnalysisAsync(userId);

            Assert.NotNull(view.WeakestTopic);
            Assert.Null(view.WeakestTopic!.LessonId);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ------------------------------------------------------------------------------------------
    // criterion 2, second half: nothing rankable means nothing is named
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Below the floor everywhere: the page must name no weakness at all. <b>The wrong input this
    /// catches:</b> taking <c>ranked.First()</c> from the unranked list when the ranked one is empty,
    /// which produces a confident sentence from a single session.
    /// </summary>
    [Fact]
    public async Task With_no_rankable_cell_no_weakest_topic_is_named()
    {
        var name = $"Wk243None_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certCode = Unique("WKZ");
            var (userId, certId) = await SeedAsync(connectionString, certCode);

            await SeedCellAsync(connectionString, userId, certId, "reading", "R-DET", "B1",
                answered: 4, correct: 1);
            await SeedCellAsync(connectionString, userId, certId, "reading", "R-GRAM", "B2",
                answered: 2, correct: 0);

            var view = await Service(connectionString).GetWeaknessAnalysisAsync(userId);

            Assert.Null(view.WeakestTopic);
            Assert.DoesNotContain(view.Topics, t => t.IsWeakest);
            // Both cells are still listed, with their counts - the page says why nothing is ranked
            // rather than showing an empty box.
            Assert.Equal(2, view.Topics.Count);
            Assert.All(view.Topics, t => Assert.False(t.IsWeakest));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }
}
