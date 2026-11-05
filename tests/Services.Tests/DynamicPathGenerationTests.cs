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
/// #154: a path is generated from the learner's real weaknesses and real time budget, not looked up
/// from <c>PathTemplates</c> by band pair. These tests drive the real service against real SQL Server,
/// so "two learners with the same band pair differ" is proven at the row level — the steps in the
/// database, not a shape a test re-derived.
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
    /// A certificate with four drillable sections, one lesson, a published mock exam and CEFR bands
    /// carrying the seeded hours. Its only <c>PathTemplate</c> has one item that drills P5 — the old
    /// way. A path that still read the template would drill P5 for everybody, so the divergence tests
    /// below can tell a generated path from a looked-up one.
    /// </summary>
    private async Task<(int certId, Dictionary<string, int> sections, int lessonId, int examId)> SeedCertAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sR, @cert, 'reading', 'P5', N'Incomplete Sentences',  3, 1),
              (@sR, @cert, 'reading', 'P6', N'Text Completion',       3, 2),
              (@sR, @cert, 'reading', 'P7', N'Reading Comprehension', 3, 3),
              (@sR, @cert, 'reading', 'P8', N'Cross-text',            3, 4);

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours) VALUES
              (@cert, 'A2', N'Elementary',      0, 100, 1, 250),
              (@cert, 'B1', N'Intermediate',  101, 500, 2, 500),
              (@cert, 'B2', N'Upper inter.',  501, 800, 3, 800);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Mock Exam', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO Lessons (CertId, SkillId, Title, EstimatedMinutes, DisplayOrder)
            VALUES (@cert, @sR, N'Grammar Foundations', 8, 1);
            DECLARE @lesson INT = SCOPE_IDENTITY();

            INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
            VALUES (@lesson, @cert, 'rule', N'Rule', N'Text', 1);

            -- Three mcq questions per section, so every section is drillable and a wrong answer can
            -- be recorded against any of them.
            DECLARE @sec INT, @grp INT, @q INT, @d INT = 1;
            DECLARE sections_cursor CURSOR LOCAL FAST_FORWARD FOR
              SELECT SectionId FROM Sections WHERE CertId = @cert ORDER BY DisplayOrder;
            OPEN sections_cursor;
            FETCH NEXT FROM sections_cursor INTO @sec;
            WHILE @@FETCH_STATUS = 0
            BEGIN
              INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, @d);
              SET @grp = SCOPE_IDENTITY();

              DECLARE @i INT = 1;
              WHILE @i <= 3
              BEGIN
                INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
                VALUES (@grp, @exam, @sec, @sR, CONCAT(N'Section ', @d, N' question ', @i), 2, 'mcq', @i);
                SET @q = SCOPE_IDENTITY();
                INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
                  (@q, 'mcq', 'A', N'Right', 1),
                  (@q, 'mcq', 'B', N'Wrong', 0);
                SET @i = @i + 1;
              END
              SET @d = @d + 1;
              FETCH NEXT FROM sections_cursor INTO @sec;
            END
            CLOSE sections_cursor; DEALLOCATE sections_cursor;

            -- The old template: its only item drills P5
            INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
            VALUES (@cert, 'B1', 'B2', N'B1 to B2');
            DECLARE @tpl INT = SCOPE_IDENTITY();

            INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, EstimatedMinutes)
            VALUES (@tpl, @sR, @cert, 1, N'Module 1', 20);
            DECLARE @m1 INT = SCOPE_IDENTITY();

            INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
            VALUES (@m1, @cert, @tpl, 'practice_section',
                    (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5'), 5, 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var sections = await db.Sections.Where(s => s.CertId == cert.CertId).ToDictionaryAsync(s => s.Code, s => s.SectionId);
        var lessonId = await db.Lessons.Where(l => l.CertId == cert.CertId).Select(l => l.LessonId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == cert.CertId && e.Purpose == "mock").Select(e => e.ExamId).SingleAsync();
        return (cert.CertId, sections, lessonId, examId);
    }

    /// <summary>A learner with a goal (B1→B2 at <paramref name="weeklyHours"/>) and no practice
    /// history, so the planner has the certificate's own order to fall back on.</summary>
    private async Task<int> SeedLearnerAsync(
        int certId, decimal weeklyHours = 6m, int daysToTarget = 210)
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
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// Records a finished practice session on one section with every answer wrong — which is how a
    /// real weakness appears (there is no other writer of <c>vw_UserSectionAccuracyAll</c>).
    /// </summary>
    private static async Task RecordWrongAnswersAsync(CompanionDbContext db, int userId, int sectionId)
    {
        var questions = await db.Questions
            .Where(q => q.SectionId == sectionId)
            .OrderBy(q => q.QuestionId)
            .Select(q => q.QuestionId)
            .ToListAsync();

        var session = new PracticeSession
        {
            UserId = userId,
            CertId = (await db.Sections.Where(s => s.SectionId == sectionId).Select(s => s.CertId).SingleAsync()),
            SectionId = sectionId,
            Modality = "reading",
            QuestionCount = questions.Count,
            Status = PracticeStatus.Finished.ToCode(),
            CorrectCount = 0,
            QuestionSource = QuestionSource.All.ToCode(),
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            FinishedAt = DateTimeOffset.UtcNow
        };
        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync();

        var order = 1;
        foreach (var questionId in questions)
        {
            var wrongOption = await db.QuestionOptions.FirstAsync(o => o.QuestionId == questionId && !o.IsCorrect);
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                QuestionId = questionId,
                SectionId = sectionId,
                SelectedOptionId = wrongOption.OptionId,
                IsCorrect = false,
                HintUsed = false,
                DisplayOrder = order++
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<List<int>> DrilledSectionsAsync(CompanionDbContext db, int learnerPathId) =>
        await db.LearnerPathSteps
            .Include(s => s.PathModuleItem)
            .Where(s => s.LearnerPathId == learnerPathId && s.PathModuleItem.RefSectionId != null)
            .Select(s => s.PathModuleItem.RefSectionId!.Value)
            .Distinct()
            .ToListAsync();

    [Fact]
    public async Task Two_learners_with_the_same_band_pair_get_paths_that_drill_different_sections()
    {
        var certCode = Unique("DIV");
        var (certId, sections, _, _) = await SeedCertAsync(certCode);

        var learnerA = await SeedLearnerAsync(certId);
        var learnerB = await SeedLearnerAsync(certId);

        await using (var seedDb = DbContextFactory.Create(database.ConnectionString))
        {
            // A is weakest at P7; B is weakest at P5. Same band pair, same goal, same certificate.
            await RecordWrongAnswersAsync(seedDb, learnerA, sections["P7"]);
            await RecordWrongAnswersAsync(seedDb, learnerB, sections["P5"]);
        }

        var (paths, _, _, _, db) = CreateServices();
        var summaryA = await paths.GeneratePathAsync(learnerA);
        var summaryB = await paths.GeneratePathAsync(learnerB);

        var drilledA = await DrilledSectionsAsync(db, summaryA.LearnerPathId!.Value);
        var drilledB = await DrilledSectionsAsync(db, summaryB.LearnerPathId!.Value);

        Assert.Contains(sections["P7"], drilledA);
        Assert.Contains(sections["P5"], drilledB);

        // Materially different, not only reordered: the two paths do not drill the same set.
        Assert.NotEqual(
            string.Join(",", drilledA.OrderBy(x => x)),
            string.Join(",", drilledB.OrderBy(x => x)));
    }

    [Fact]
    public async Task A_six_hour_goal_plans_four_weeks_of_three_to_five_steps()
    {
        var certCode = Unique("VOL");
        var (certId, _, _, _) = await SeedCertAsync(certCode);
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
            Assert.InRange(weekSteps.Count, LearningPathPlanner.MinimumStepsPerWeek, LearningPathPlanner.MaximumStepsPerWeek);
            Assert.All(weekSteps, s => Assert.InRange(s.EstimatedMinutes!.Value, 10, 45));
        }
    }

    [Fact]
    public async Task A_heavier_weekly_pace_persists_more_planned_minutes()
    {
        var certCode = Unique("PACE");
        var (certId, _, _, _) = await SeedCertAsync(certCode);
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

    [Fact]
    public async Task Every_generated_step_carries_a_week_a_duration_and_an_accuracy_target()
    {
        var certCode = Unique("COLS");
        var (certId, _, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);

        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .ToListAsync();

        Assert.NotEmpty(steps);
        Assert.All(steps, s =>
        {
            Assert.NotNull(s.WeekNumber);
            Assert.NotNull(s.EstimatedMinutes);
            Assert.InRange(s.TargetAccuracyPct, 70, 80);
        });
    }

    [Fact]
    public async Task A_generated_path_does_not_read_the_band_pair_template()
    {
        // The certificate's only template drills P5. A learner weakest at P7 must not get P5.
        var certCode = Unique("NOTPL");
        var (certId, sections, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        await using (var seedDb = DbContextFactory.Create(database.ConnectionString))
        {
            await RecordWrongAnswersAsync(seedDb, userId, sections["P7"]);
            await RecordWrongAnswersAsync(seedDb, userId, sections["P8"]);
        }

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);

        var drilled = await DrilledSectionsAsync(db, summary.LearnerPathId!.Value);

        Assert.Contains(sections["P7"], drilled);
        Assert.DoesNotContain(sections["P5"], drilled);
    }

    [Fact]
    public async Task The_two_weakest_sections_are_the_ones_drilled_in_that_order()
    {
        var certCode = Unique("ORDER");
        var (certId, sections, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        await using (var seedDb = DbContextFactory.Create(database.ConnectionString))
        {
            // P8 weakest, then P6, then P5, then P7 — so the plan must pick P8 and P6, not P5.
            await RecordWrongAnswersAsync(seedDb, userId, sections["P8"]);
            await RecordWrongAnswersAsync(seedDb, userId, sections["P6"]);
        }

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);

        var drilled = await DrilledSectionsAsync(db, summary.LearnerPathId!.Value);

        Assert.Equal(2, drilled.Count);
        Assert.Contains(sections["P8"], drilled);
        Assert.Contains(sections["P6"], drilled);
    }
}
