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
            -- #259 / PRN-03: a placement is placed by its CUT-SCORE table, not by its band names. A
            -- fixture with bands alone used to pass only because the writer parsed the band code as a
            -- CEFR level, which is the crosswalk the rule forbids.

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
    public async Task A_skill_card_carries_the_latest_graded_score_and_its_three_weakest_topics()
    {
        var code = Unique("S");
        var (_, skillId, sectionId) = await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);

        // Five pool cells answered to five different accuracies. The card reads
        // vw_UserTopicAccuracy, the same view the weakness page ranks, so the two cannot disagree.
        // #271: the cells are topics at a level, not categories - the axis the pool actually has.
        var expectedWeakest = new List<string>();
        foreach (var (topicCode, weight, correct) in new[]
        {
            ("R-GIST", 1, 1), ("R-INF", 2, 2), ("R-ATT", 3, 3), ("R-COH", 4, 4), ("R-DET", 5, 10),
        })
        {
            expectedWeakest.Add(await SeedCellPracticeHistoryAsync(userId, code, sectionId,
                correctCount: correct, totalCount: 10, topicCode: topicCode));
            _ = weight;
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
        Assert.Equal(expectedWeakest.Take(3), card.WeakestTopics.Select(c => c.Name));
        Assert.Equal(new[] { 10, 20, 30 }, card.WeakestTopics.Select(c => c.AccuracyPercent));
        Assert.All(card.WeakestTopics, c => Assert.Equal(10, c.Answered));
        // #271: a card names the cell, so the level travels with the topic.
        Assert.All(card.WeakestTopics, c => Assert.Equal("B1", c.LevelCode));
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
        var topicName = await SeedCellPracticeHistoryAsync(userId, code, sectionId,
            correctCount: 3, totalCount: 10, topicCode: "R-GRAM");

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal("/practice", dashboard.NextAction.ActionUrl);
        Assert.Contains(topicName, dashboard.NextAction.Title);
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

    // ---- A stored score is not the number the learner reads (#159 review) ----

    [Fact]
    public async Task A_half_step_certificate_carries_its_display_factor_so_the_dashboard_can_divide()
    {
        // IELTS keeps marks in tenths: 8.5 is stored 85. The block used to print the stored integer,
        // so the dashboard said "85 pts" and "85 / 90 latest graded" while the exam result page for
        // the same attempt correctly said 8.5. ScoreDisplay is the one place the division happens;
        // the factor has to reach it from the certificate, not from the page.
        var code = Unique("G");
        var (certId, _, _) = await SeedCertWithNamedSkillsAsync(code, ("LIST", "Listening", "listening"));
        await database.SqlAsync($"""
            UPDATE Certificates SET ScoreAggregationRule = 'avg_half_step' WHERE CertId = {certId};
            """);
        var userId = await SeedLearnerAsync(code);
        await database.SqlAsync($"""
            INSERT INTO TargetBands (UserId, CertId, CurrentBandCode, TargetBandCode)
              VALUES ({userId}, {certId}, 'LOW', 'HIGH');
            """);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(10, dashboard.Goal!.ScoreDisplayFactor);
        Assert.All(dashboard.Skills, s => Assert.Equal(10, s.ScoreDisplayFactor));
    }

    [Fact]
    public async Task A_sum_certificate_keeps_factor_one()
    {
        var code = Unique("H");
        await SeedCertWithNamedSkillsAsync(code, ("LIST", "Listening", "listening"));
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        // The default is the ordinary case; only a half-step certificate divides by ten.
        Assert.All(dashboard.Skills, s => Assert.Equal(1, s.ScoreDisplayFactor));
    }
}
