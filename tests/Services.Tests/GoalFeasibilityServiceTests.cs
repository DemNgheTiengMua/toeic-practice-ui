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
/// #153: a goal with a deadline. The service writes five columns, computes the verdict in code, and
/// refuses a goal the learner cannot reach. These tests drive the real service against real SQL
/// Server with the real CHECK constraints, so the "no deadline means no verdict" rule is proven at
/// the seam that stores it — not by re-implementing the formula here.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GoalFeasibilityServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (PlacementService placement, LearningPathService paths, CompanionDbContext db) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        return (new PlacementService(db, learners), new LearningPathService(db, learners), db);
    }

    /// <summary>
    /// Seeds a certificate with CEFR bands carrying the seeded study hours, a placement exam, and a
    /// path template between every pair of bands, so "refuses to generate" is proven against a
    /// template that exists and would otherwise be used.
    /// </summary>
    private async Task<int> SeedCefrCertAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder)
            VALUES (@sR, @cert, 'reading', 'P5', N'Sentences', 1, 1);
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            -- the seeded hours: B1 = 500, B2 = 800, C1 = 1150
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours) VALUES
              (@cert, 'A2', N'Elementary',        0,   100, 1, 250),
              (@cert, 'B1', N'Intermediate',    101,   500, 2, 500),
              (@cert, 'B2', N'Upper inter.',    501,   800, 3, 800),
              (@cert, 'C1', N'Advanced',        801,   990, 4, 1150);

            INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
            VALUES (@cert, 'B1', 'B2', N'B1 to B2');
            DECLARE @tpl INT = SCOPE_IDENTITY();

            INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, EstimatedMinutes)
            VALUES (@tpl, @sR, @cert, 1, N'Module 1', 20);
            DECLARE @m1 INT = SCOPE_IDENTITY();

            INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
            VALUES (@m1, @cert, @tpl, 'practice_section', @secR, 5, 1);

            -- #154: a path needs a section it can actually drill, so the fixture carries one question.
            -- It needs an exam to hang on: QuestionGroups.ExamId is NOT NULL, and drilling reads
            -- Questions by section, never by exam.
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Subject bank', 'published', 30, 'mock');
            DECLARE @drillExam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@drillExam, @cert, @secR, 1);
            DECLARE @drillGroup INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@drillGroup, @drillExam, @secR, @sR, N'Drill question', 2, 'mcq', 1);
            DECLARE @drillQ INT = SCOPE_IDENTITY();

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@drillQ, 'mcq', 'A', N'Right', 1),
              (@drillQ, 'mcq', 'B', N'Wrong', 0);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();
    }

    private async Task<int> SeedLearnerAsync(string certCode, string? currentBand = null, string? targetBand = null)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var goal = targetBand is null
            ? ""
            : $"""
              INSERT INTO TargetBands (UserId, CertId, TargetBandCode, CurrentBandCode)
              VALUES (@u, (SELECT CertId FROM Certificates WHERE Code = '{certCode}'),
                      '{targetBand}',
                      {(currentBand is null ? "NULL" : $"'{currentBand}'")});
              """;

        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            DECLARE @u INT = SCOPE_IDENTITY();
            {goal}
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>The learner's starting band comes from a finished placement; this is the seam that
    /// records one, so the tests set a goal from a real assessed band.</summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Setting_a_goal_with_a_deadline_persists_all_five_columns_and_its_verdict()
    {
        var certCode = Unique("GF");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, _, db) = CreateServices();

        // 210 days at 10 h/week is a goal the learner can actually meet (300 h needed, 300 available).
        var result = await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "B2", targetDate: Today.AddDays(210), weeklyStudyHours: 10m);

        Assert.Equal(FeasibilityStatus.Feasible, result.Feasibility.Status);
        Assert.Equal(300, result.Feasibility.RequiredHours);
        Assert.Equal(300m, result.Feasibility.AvailableHours);

        var saved = await db.TargetBands.AsNoTracking().SingleAsync(t => t.UserId == userId);
        Assert.Equal("B1", saved.CurrentBandCode);
        Assert.Equal("B2", saved.TargetBandCode);
        Assert.Equal(Today.AddDays(210), saved.TargetDate);
        Assert.Equal(10m, saved.WeeklyStudyHours);
        Assert.Equal(300, saved.RequiredTotalHours);
        Assert.Equal("feasible", saved.FeasibilityStatus);
    }

    [Fact]
    public async Task A_goal_without_a_deadline_stores_no_verdict()
    {
        var certCode = Unique("GN");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, _, db) = CreateServices();

        var result = await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "B2", targetDate: null, weeklyStudyHours: null);

        Assert.Equal(null, result.Feasibility.Status);

        var saved = await db.TargetBands.AsNoTracking().SingleAsync(t => t.UserId == userId);
        Assert.Equal("B2", saved.TargetBandCode);
        Assert.Null(saved.TargetDate);
        Assert.Null(saved.WeeklyStudyHours);
        Assert.Null(saved.RequiredTotalHours);
        Assert.Null(saved.FeasibilityStatus);   // "no deadline set", never "unrealistic"
    }

    [Fact]
    public async Task A_goal_on_a_certificate_whose_bands_carry_no_hours_answers_cannot_judge()
    {
        var certCode = Unique("GX");
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            -- a certificate with its own band vocabulary: CumulativeStudyHours stays NULL
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'CAT_B1', N'Band 1', 0,   50, 1),
              (@cert, 'CAT_B2', N'Band 2', 51, 100, 2);
            """);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, _, db) = CreateServices();

        var result = await placement.SetGoalAsync(
            userId, currentBandCode: "CAT_B1", targetBandCode: "CAT_B2", targetDate: Today.AddDays(30), weeklyStudyHours: 6m);

        Assert.Equal(null, result.Feasibility.Status);

        var saved = await db.TargetBands.AsNoTracking().SingleAsync(t => t.UserId == userId);
        Assert.Equal("CAT_B2", saved.TargetBandCode);
        Assert.Equal(Today.AddDays(30), saved.TargetDate);    // the deadline is kept...
        Assert.Equal(6m, saved.WeeklyStudyHours);
        Assert.Null(saved.RequiredTotalHours);                // ...but no hours are invented
        Assert.Null(saved.FeasibilityStatus);
    }

    [Fact]
    public async Task A_pace_above_the_twenty_five_hour_ceiling_is_refused_with_a_message()
    {
        var certCode = Unique("GC");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, _, db) = CreateServices();

        var ex = await Assert.ThrowsAsync<PlacementException>(() => placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "B2", targetDate: Today.AddDays(210), weeklyStudyHours: 26m));

        Assert.Equal(PlacementException.WeeklyHoursTooHigh, ex.Code);
        Assert.Contains("25", ex.Message);
        Assert.False(await db.TargetBands.AnyAsync(t => t.UserId == userId));
    }

    [Fact]
    public async Task An_unrealistic_goal_is_stored_with_both_numbers_so_the_screen_can_show_them()
    {
        var certCode = Unique("GU");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, _, db) = CreateServices();

        // B1 to C1 is 650 h; two weeks at 6 h/week is 12.
        var result = await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "C1", targetDate: Today.AddDays(14), weeklyStudyHours: 6m);

        Assert.Equal(FeasibilityStatus.Unrealistic, result.Feasibility.Status);
        Assert.Equal(650, result.Feasibility.RequiredHours);
        Assert.Equal(12m, result.Feasibility.AvailableHours);
        Assert.NotNull(result.Feasibility.EarliestFeasibleDate);

        var saved = await db.TargetBands.AsNoTracking().SingleAsync(t => t.UserId == userId);
        Assert.Equal("unrealistic", saved.FeasibilityStatus);
        Assert.Equal(650, saved.RequiredTotalHours);
    }

    [Fact]
    public async Task An_unrealistic_goal_does_not_stop_the_learner_seeing_where_they_stand()
    {
        // The goal is stored, not thrown away: the screen has to render the two numbers and the two
        // escape hatches. Only path generation is blocked.
        var certCode = Unique("GV");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, _, _) = CreateServices();

        await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "C1", targetDate: Today.AddDays(14), weeklyStudyHours: 6m);

        var view = await placement.GetGoalStatusAsync(userId);
        Assert.Equal(FeasibilityStatus.Unrealistic, view.Status);
        Assert.Equal(650, view.RequiredHours);
        Assert.Equal(12, view.AvailableHours);
    }

    [Fact]
    public async Task Path_generation_is_refused_while_the_goal_is_unrealistic_naming_both_numbers()
    {
        var certCode = Unique("GR");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, paths, db) = CreateServices();

        await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "C1", targetDate: Today.AddDays(14), weeklyStudyHours: 6m);

        var ex = await Assert.ThrowsAsync<LearningPathException>(() => paths.GeneratePathAsync(userId));

        Assert.Equal(LearningPathException.GoalUnrealistic, ex.Code);
        Assert.Contains("650", ex.Message);   // hours needed
        Assert.Contains("12", ex.Message);    // hours available
        Assert.False(await db.LearnerPaths.AnyAsync(p => p.UserId == userId));
    }

    [Fact]
    public async Task Path_generation_is_allowed_once_the_deadline_moves_to_a_reachable_date()
    {
        var certCode = Unique("GA");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, paths, _) = CreateServices();

        await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "C1", targetDate: Today.AddDays(14), weeklyStudyHours: 6m);

        // "Move the date back" — the first escape hatch, at the same pace.
        var moved = await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "C1", targetDate: Today.AddDays(109 * 7), weeklyStudyHours: 6m);
        Assert.NotEqual(FeasibilityStatus.Unrealistic, moved.Feasibility.Status);

        var summary = await paths.GeneratePathAsync(userId);
        Assert.True(summary.HasActivePath);
    }

    [Fact]
    public async Task A_stretch_goal_still_generates_a_path_and_reports_the_pace_that_would_fix_it()
    {
        var certCode = Unique("GS");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, paths, _) = CreateServices();

        // 300 h needed, 12 weeks × 13 h = 156 available: R = 0.52, a stretch.
        var result = await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "B2", targetDate: Today.AddDays(84), weeklyStudyHours: 13m);
        Assert.Equal(FeasibilityStatus.Stretch, result.Feasibility.Status);
        Assert.Equal(25, result.Feasibility.SuggestedWeeklyHours);

        var summary = await paths.GeneratePathAsync(userId);
        Assert.True(summary.HasActivePath);
    }

    [Fact]
    public async Task Re_saving_a_goal_keeps_one_row_and_updates_the_verdict()
    {
        var certCode = Unique("GW");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, _, db) = CreateServices();

        await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "C1", targetDate: Today.AddDays(14), weeklyStudyHours: 6m);
        await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "B2", targetDate: Today.AddDays(210), weeklyStudyHours: 10m);

        var saved = await db.TargetBands.AsNoTracking().Where(t => t.UserId == userId).ToListAsync();
        Assert.Single(saved);
        Assert.Equal("B2", saved[0].TargetBandCode);
        Assert.Equal("feasible", saved[0].FeasibilityStatus);
    }

    [Fact]
    public async Task Clearing_the_deadline_clears_the_verdict_instead_of_leaving_a_stale_one()
    {
        var certCode = Unique("GD");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, _, db) = CreateServices();

        await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "C1", targetDate: Today.AddDays(14), weeklyStudyHours: 6m);
        await placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "B2", targetDate: null, weeklyStudyHours: null);

        var saved = await db.TargetBands.AsNoTracking().SingleAsync(t => t.UserId == userId);
        Assert.Null(saved.FeasibilityStatus);
        Assert.Null(saved.RequiredTotalHours);
        Assert.Null(saved.TargetDate);
    }

    [Fact]
    public async Task A_target_date_in_the_past_is_refused_rather_than_silently_floored_to_one_week()
    {
        var certCode = Unique("GP");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, _, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<PlacementException>(() => placement.SetGoalAsync(
            userId, currentBandCode: "B1", targetBandCode: "B2", targetDate: Today.AddDays(-1), weeklyStudyHours: 6m));

        Assert.Equal(PlacementException.TargetDateInvalid, ex.Code);
    }

    [Fact]
    public async Task A_band_that_is_not_a_band_of_this_certificate_is_refused()
    {
        var certCode = Unique("GB");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (placement, _, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<PlacementException>(() => placement.SetGoalAsync(
            userId, currentBandCode: "NOPE", targetBandCode: "B2", targetDate: Today.AddDays(210), weeklyStudyHours: 10m));

        Assert.Equal(PlacementException.TargetBandInvalid, ex.Code);
    }

    /// <summary>
    /// #153: the old generator answered a missing starting band with a hardcoded one that matches no
    /// certificate's rows, so the path silently attached to no band at all. It must now refuse.
    /// </summary>
    [Fact]
    public async Task Path_generation_refuses_when_nothing_says_which_band_the_learner_starts_from()
    {
        var certCode = Unique("GF");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);     // goal with a target, but no starting band
        var (placement, paths, db) = CreateServices();

        await placement.SetGoalAsync(
            userId, currentBandCode: null, targetBandCode: "B2", targetDate: null, weeklyStudyHours: null);

        var ex = await Assert.ThrowsAsync<LearningPathException>(() => paths.GeneratePathAsync(userId));

        Assert.Equal(LearningPathException.StartingBandUnknown, ex.Code);
        Assert.False(await db.LearnerPaths.AnyAsync(p => p.UserId == userId));
    }

    /// <summary>#153: and the old generator answered "no target band" with the certificate's highest
    /// band — aiming every learner at a band nobody chose. The learner here has been assessed (so the
    /// starting band is known) but has set no goal.</summary>
    [Fact]
    public async Task Path_generation_refuses_when_no_target_band_is_set_anywhere()
    {
        var certCode = Unique("GT");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        // A finished placement: the starting band is known, the goal is not set.
        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Diag', 'published', 30, 'placement');
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
            VALUES ({userId}, @exam, @cert, 'graded', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 400, 'B1', 'AttemptEngine', 1);
            INSERT INTO PlacementTests (UserId, CertId, ExamId, Status, ResultBandCode, AttemptId, TotalScore, WrittenByService, ProjectionVersion)
            VALUES ({userId}, @cert, @exam, 'done', 'B1', SCOPE_IDENTITY(), 400, 'AttemptEngine', 1);
            """);

        var (_, paths, db) = CreateServices();

        var ex = await Assert.ThrowsAsync<LearningPathException>(() => paths.GeneratePathAsync(userId));

        Assert.Equal(LearningPathException.TargetBandMissing, ex.Code);
        Assert.False(await db.LearnerPaths.AnyAsync(p => p.UserId == userId));
    }

    [Fact]
    public async Task Path_generation_uses_the_goal_recorded_when_the_caller_names_no_band()
    {
        var certCode = Unique("GZ");
        await SeedCefrCertAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, currentBand: "B1", targetBand: "B2");
        var (_, paths, _) = CreateServices();

        var summary = await paths.GeneratePathAsync(userId);

        Assert.True(summary.HasActivePath);
        Assert.Equal("B1", summary.FromBandCode);
        Assert.Equal("B2", summary.ToBandCode);
    }
}
