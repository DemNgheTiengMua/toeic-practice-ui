using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>
/// #159: the dashboard's three blocks, measured. The point of the ticket was that eleven blocks of
/// commentary hid one action and two features; these tests pin what the three blocks must decide.
/// </summary>
public sealed partial class LearnerServiceTests
{
    private async Task<(int CertId, int SkillId, int SectionId)> SeedCertWithNamedSkillsAsync(
        string code,
        params (string Code, string Name, string Modality)[] skills)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Cert {code}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            """ + string.Concat(skills.Select((s, i) => $"""
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MaxScaledScore)
              VALUES (@cert, '{s.Code}', N'{s.Name}', '{s.Modality}', {i + 1}, 500);
            """)) + string.Concat(skills.Select((s, i) => $"""
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder)
              VALUES ((SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = '{s.Code}'),
                      @cert, '{s.Modality}', 'S{i + 1}', N'{s.Name} section', 1, {i + 1});
            """)) + """
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low band', 0, 249, 1),
              (@cert, 'HIGH', N'High band', 250, 500, 2);
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
              VALUES (@cert, N'Mock exam', 'published', 120, 'mock');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var skillId = await db.Skills.Where(s => s.CertId == certId).OrderBy(s => s.DisplayOrder).Select(s => s.SkillId).FirstAsync();
        var sectionId = await db.Sections.Where(s => s.CertId == certId).Select(s => s.SectionId).FirstAsync();
        return (certId, skillId, sectionId);
    }

    // ---- Skill Cards: Cert.Skills, not a Listening/Reading pair (#149 applied to the dashboard) ----

    [Fact]
    public async Task Skill_cards_render_one_card_per_skill_of_a_certificate_whose_names_are_not_listening_reading()
    {
        var code = Unique("D");
        await SeedCertWithNamedSkillsAsync(code,
            ("COMP", "Comprehension", "reading"),
            ("EXPR", "Expression", "writing"));

        var userId = await SeedLearnerAsync(code);
        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(new[] { "Comprehension", "Expression" }, dashboard.Skills.Select(s => s.Name));
        Assert.Equal(new[] { "COMP", "EXPR" }, dashboard.Skills.Select(s => s.Code));
        // Nothing invented: no card the certificate never defined.
        Assert.DoesNotContain(dashboard.Skills, s => s.Name is "Listening" or "Reading");
    }

    [Fact]
    public async Task Skill_cards_render_every_skill_of_a_certificate_that_has_more_than_two()
    {
        var code = Unique("E");
        await SeedCertWithNamedSkillsAsync(code,
            ("A", "Alpha", "listening"),
            ("B", "Beta", "reading"),
            ("C", "Gamma", "writing"),
            ("D", "Delta", "speaking"),
            ("E", "Epsilon", "reading"));

        var userId = await SeedLearnerAsync(code);
        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(5, dashboard.Skills.Count);
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" }, dashboard.Skills.Select(s => s.Name));
    }

    [Fact]
    public async Task A_skill_card_carries_the_latest_graded_score_and_its_three_weakest_categories()
    {
        var code = Unique("S");
        var (_, skillId, sectionId) = await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);

        // Five categories answered to five different accuracies. The view unions practice answers,
        // so the card's accuracy comes from the same rows the weakness page reads.
        foreach (var (catCode, catName, correct) in new[]
        {
            ("C1", "Weak one", 1), ("C2", "Weak two", 2), ("C3", "Weak three", 3),
            ("C4", "Weak four", 4), ("C5", "Strong", 10),
        })
        {
            await SeedSectionPracticeHistoryAsync(userId, code, sectionId,
                correctCount: correct, totalCount: 10,
                categoryCode: catCode, categoryName: catName);
        }

        // One graded score for the skill itself, from a real graded attempt. The trigger requires the
        // attempt's total to equal the sum of its skill scores, so both are seeded with 350.
        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{code}');
            DECLARE @exam INT = (SELECT TOP 1 ExamId FROM Exams WHERE CertId = @cert AND Purpose = 'mock');
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
            VALUES ({userId}, @exam, @cert, 'graded', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 350, 'HIGH');
            DECLARE @attempt INT = SCOPE_IDENTITY();
            INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore)
            VALUES (@attempt, {skillId}, @cert, 30, 350);
            """);

        var dashboard = await CreateService().GetDashboardAsync(userId);
        var card = Assert.Single(dashboard.Skills);

        Assert.Equal(350, card.LatestScaledScore);
        Assert.Equal(500, card.MaxScaledScore);
        // Three at most, lowest accuracy first - the same ordering the weakness page uses.
        Assert.Equal(new[] { "Weak one", "Weak two", "Weak three" }, card.WeakestCategories.Select(c => c.Name));
        Assert.Equal(new[] { 10, 20, 30 }, card.WeakestCategories.Select(c => c.AccuracyPercent));
        Assert.All(card.WeakestCategories, c => Assert.Equal(10, c.Answered));
    }

    [Fact]
    public async Task A_skill_with_no_graded_exam_reports_no_score_rather_than_zero()
    {
        var code = Unique("Z");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        // A zero would read as "you scored nothing"; null reads as "nothing graded yet". The card
        // renders a dash for null and a number for zero, so the two can never be confused.
        Assert.Null(Assert.Single(dashboard.Skills).LatestScaledScore);
    }

    [Fact]
    public async Task Skill_cards_are_the_active_certificates_skills_only()
    {
        var toeic = Unique("T");
        var ielts = Unique("I");
        await SeedCertWithNamedSkillsAsync(toeic, ("LIST", "Listening", "listening"));
        await SeedCertWithNamedSkillsAsync(ielts, ("COMP", "Comprehension", "reading"), ("EXPR", "Expression", "writing"));
        var userId = await SeedLearnerAsync(toeic);

        var toeicDash = await CreateService().GetDashboardAsync(userId);
        Assert.Equal(new[] { "Listening" }, toeicDash.Skills.Select(s => s.Name));

        await CreateService().SwitchCertificateAsync(userId, ielts);
        var ieltsDash = await CreateService().GetDashboardAsync(userId);
        Assert.Equal(new[] { "Comprehension", "Expression" }, ieltsDash.Skills.Select(s => s.Name));
    }

    // ---- Hero Next Action ----

    [Fact]
    public async Task The_hero_points_at_the_placement_test_until_one_is_done()
    {
        var code = Unique("H");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal("/placement", dashboard.NextAction.ActionUrl);
        Assert.Contains("level", dashboard.NextAction.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Take the free placement test", dashboard.NextAction.ActionLabel);
    }

    [Fact]
    public async Task The_hero_resumes_an_unfinished_attempt_before_anything_else()
    {
        var code = Unique("R");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedInProgressAttemptAsync(userId, code, "2026-10-01T10:00:00+07:00");

        var dashboard = await CreateService().GetDashboardAsync(userId);

        var attemptId = dashboard.UnfinishedAttempt!.AttemptId;
        Assert.Equal($"/exams/take/{attemptId}", dashboard.NextAction.ActionUrl);
        Assert.DoesNotContain("/placement", dashboard.NextAction.ActionUrl);
    }

    [Fact]
    public async Task The_hero_asks_for_a_target_band_once_placement_is_done_and_no_goal_is_set()
    {
        var code = Unique("G");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal("/placement/target-band", dashboard.NextAction.ActionUrl);
    }

    [Fact]
    public async Task The_hero_names_the_weakest_topic_once_there_is_accuracy_to_rank()
    {
        var code = Unique("W");
        var (_, _, sectionId) = await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");
        await SeedSetTargetBandAsync(userId, code, "HIGH");
        await SeedSectionPracticeHistoryAsync(userId, code, sectionId, correctCount: 3, totalCount: 10,
            categoryCode: "TENSE", categoryName: "Verb tense");

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal("/practice", dashboard.NextAction.ActionUrl);
        Assert.Contains("Verb tense", dashboard.NextAction.Title);
        Assert.Equal("danger", dashboard.NextAction.Severity);
    }

    [Fact]
    public async Task The_hero_offers_the_speaking_practice_when_the_certificate_has_one_and_nothing_is_weaker()
    {
        var code = Unique("P");
        await SeedCertWithNamedSkillsAsync(code, ("SPEA", "Speaking", "speaking"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");
        await SeedSetTargetBandAsync(userId, code, "HIGH");

        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{code}');
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Speaking practice 1', 'published', 20, 'speaking_practice');
            """);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.StartsWith("/speaking/", dashboard.NextAction.ActionUrl);
        Assert.EndsWith("/instructions", dashboard.NextAction.ActionUrl);
    }

    // ---- Goal Standing ----

    [Fact]
    public async Task Goal_standing_carries_the_target_date_pace_and_this_weeks_minutes()
    {
        var code = Unique("L");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");
        await SeedGoalWithPathAsync(userId, code, "HIGH",
            targetDate: "2026-12-20", weeklyHours: 6m, feasibility: "stretch",
            weekSteps: [(1, 30, true), (1, 30, false), (2, 45, false)]);

        var dashboard = await CreateService().GetDashboardAsync(userId);
        var goal = dashboard.Goal;

        Assert.Equal("LOW", goal.CurrentBandCode);
        Assert.Equal("High band", goal.TargetBandName);
        Assert.Equal(250, goal.TargetMinScore);
        Assert.Equal(new DateOnly(2026, 12, 20), goal.TargetDate);
        Assert.Equal(6m, goal.WeeklyStudyHours);
        Assert.Equal("stretch", goal.Status);
        // Week 1 still has an unfinished step, so week 1 is "this week": 60 planned, 30 done.
        Assert.Equal(1, goal.CurrentWeekNumber);
        Assert.Equal(60, goal.WeekPlannedMinutes);
        Assert.Equal(30, goal.WeekCompletedMinutes);
        Assert.Equal(1, goal.CompletedPathSteps);
        Assert.Equal(3, goal.TotalPathSteps);
    }

    [Fact]
    public async Task Goal_standing_reports_no_week_when_the_path_carries_no_week_plan()
    {
        var code = Unique("M");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        // A path built before #154 has no WeekNumber. The block must say so, not invent a week 1.
        await SeedGoalWithPathAsync(userId, code, "HIGH",
            targetDate: null, weeklyHours: null, feasibility: null,
            weekSteps: [(null, 30, false)]);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Null(dashboard.Goal.CurrentWeekNumber);
        Assert.Equal(0, dashboard.Goal.WeekPlannedMinutes);
        Assert.Equal(1, dashboard.Goal.TotalPathSteps);
        // No deadline means no verdict - which is not the same as "unrealistic".
        Assert.Null(dashboard.Goal.Status);
    }

    // ---- Goal Standing: the goal's own starting band is a statement, not a blank (#159) ----

    [Fact]
    public async Task Goal_standing_reads_the_starting_band_from_the_goal_when_there_is_no_placement_or_mock()
    {
        // Found in the browser: with no attempt history the dashboard said "Not placed yet" beside a
        // goal row that said B1, while the path had been built from that B1. The goal is the only
        // thing that knows where the learner starts, so it has to be read.
        var code = Unique("F");
        var (certId, _, _) = await SeedCertWithNamedSkillsAsync(code, ("LIST", "Listening", "listening"));
        var userId = await SeedLearnerAsync(code);

        await database.SqlAsync($"""
            INSERT INTO TargetBands (UserId, CertId, CurrentBandCode, TargetBandCode, TargetDate, WeeklyStudyHours, FeasibilityStatus)
              VALUES ({userId}, {certId}, 'LOW', 'HIGH',
                      DATEADD(day, 210, CAST(SYSDATETIMEOFFSET() AS DATE)), 10.0, 'feasible');
            """);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal("LOW", dashboard.Goal!.CurrentBandCode);
        Assert.Equal("goal", dashboard.Goal.CurrentSource);
    }
}
