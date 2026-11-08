using BusinessObjects;
using Microsoft.EntityFrameworkCore;
namespace Services.Tests;

/// <summary>
/// #241 / LRN-14: the next action is chosen by a fixed six-step cascade, and this file is the proof
/// that the ORDER decides rather than "the first non-empty list".
/// <para>
/// <b>The defect these tests exist for.</b> An implementation that gathers candidates and takes
/// whichever it found first agrees with the cascade on most data and disagrees after a seed change,
/// because which list is longer is not the rule. So each test below seeds TWO plausible actions at
/// once and asserts the higher step wins - and then seeds the <em>opposite</em> arrangement to prove
/// the winner is not an artefact of seed order.
/// </para>
/// <para>
/// The cascade (LRN-14), quoted in order:
/// <list type="number">
/// <item>resume an in-progress placement</item>
/// <item>retry an unmeasured skill</item>
/// <item>show the week's next available step</item>
/// <item>show the recommended free drill</item>
/// <item>show the day's ceiling state</item>
/// <item>show completion</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class LearnerServiceTests
{
    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> two skills measured, two not, and a path
    /// with an open step. "First non-empty" can return the step (step 3) and still look reasonable;
    /// the cascade says the unmeasured skill (step 2) outranks it. This asserts step 2 by name.
    /// </summary>
    [Fact]
    public async Task An_unmeasured_skill_outranks_an_open_path_step()
    {
        var code = Unique("C2");
        await SeedCertWithNamedSkillsAsync(code,
            ("LIST", "Listening", "listening"),
            ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);

        // LIST measured, READ not: exactly one skill outstanding, and a path with an open step that
        // would otherwise be the next action.
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");
        await SeedSetTargetBandAsync(userId, code, "HIGH");
        await SeedGoalWithPathAsync(userId, code, "HIGH",
            targetDate: "2026-12-20", weeklyHours: 6m, feasibility: "stretch",
            weekSteps: [(1, 30, false)]);
        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{code}');
            DELETE FROM LearnerSkillStates
              WHERE UserId = {userId} AND CertId = @cert
                AND SkillId = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');
            """);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(NextActionCascade.RetryUnmeasuredSkill, dashboard.NextAction.CascadeStep);
        // The step WAS available - that is what makes this a test of order rather than of emptiness.
        Assert.Contains("Reading", dashboard.NextAction.Title);
        Assert.Equal("/placement", dashboard.NextAction.ActionUrl);
    }

    /// <summary>
    /// <b>The same two conditions, seeded the other way round.</b> Both skills measured means step 2
    /// cannot fire, so the intake action is step 3. If the previous test passed because "unmeasured"
    /// happened to be checked first in the source, this one would too - and neither would prove an
    /// order. Together they prove the condition, not the code path.
    /// </summary>
    [Fact]
    public async Task A_fully_measured_certificate_falls_through_to_the_open_path_step()
    {
        var code = Unique("C3");
        await SeedCertWithNamedSkillsAsync(code,
            ("LIST", "Listening", "listening"),
            ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");
        await SeedSetTargetBandAsync(userId, code, "HIGH");
        await SeedGoalWithPathAsync(userId, code, "HIGH",
            targetDate: "2026-12-20", weeklyHours: 6m, feasibility: "stretch",
            weekSteps: [(1, 30, false)]);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(NextActionCascade.NextStep, dashboard.NextAction.CascadeStep);
        Assert.StartsWith("/learning-path/steps/", dashboard.NextAction.ActionUrl);
        Assert.All(dashboard.Skills, s => Assert.NotNull(s.Standing));
    }

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> a placement sitting in progress AND an
    /// open path step. A cascade evaluated bottom-up, or "pick the shortest list", returns the step.
    /// LRN-14 puts resume first, and this asserts that by name rather than by URL shape.
    /// </summary>
    [Fact]
    public async Task An_in_progress_attempt_outranks_an_unmeasured_skill_and_an_open_step()
    {
        var code = Unique("C1");
        await SeedCertWithNamedSkillsAsync(code,
            ("LIST", "Listening", "listening"),
            ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);

        // Three of the six steps' conditions hold at once: an in-progress attempt, an unmeasured
        // skill (nothing measured at all), and a path step. Only one can be said.
        await SeedSetTargetBandAsync(userId, code, "HIGH");
        await SeedGoalWithPathAsync(userId, code, "HIGH",
            targetDate: "2026-12-20", weeklyHours: 6m, feasibility: "stretch",
            weekSteps: [(1, 30, false)]);
        await SeedInProgressAttemptAsync(userId, code, "2026-10-01T10:00:00+07:00");

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(NextActionCascade.ResumeInProgress, dashboard.NextAction.CascadeStep);
        Assert.Contains("/exams/take/", dashboard.NextAction.ActionUrl);
    }

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> a measured learner with no path and no
    /// weak topic - step 3 has nothing to offer, so the cascade must fall to step 4 rather than
    /// inventing a step. The action is a drill, and it is named as step 4.
    /// </summary>
    [Fact]
    public async Task With_no_path_and_no_weak_topic_the_recommended_drill_is_step_four()
    {
        var code = Unique("C4");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");
        await SeedSetTargetBandAsync(userId, code, "HIGH");

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(NextActionCascade.FreeDrill, dashboard.NextAction.CascadeStep);
        // The recommended drill may be a typed section drill or the certificate's productive flow
        // (#150) - both are step 4's "recommended free drill". Never a path step, which has no path.
        Assert.True(
            dashboard.NextAction.ActionUrl.StartsWith("/practice", StringComparison.Ordinal)
            || dashboard.NextAction.ActionUrl.StartsWith("/speaking/", StringComparison.Ordinal)
            || dashboard.NextAction.ActionUrl.StartsWith("/writing/", StringComparison.Ordinal),
            dashboard.NextAction.ActionUrl);
    }

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> a learner who has answered 60 items today
    /// and has no path. A cascade that checks "is there a drill to offer" without checking the ceiling
    /// recommends a drill that cannot start - the learner clicks and is refused. Step 5 is the day's
    /// ceiling state, and this asserts that the drill lost to it.
    /// </summary>
    [Fact]
    public async Task A_full_day_outranks_the_drill_and_the_action_is_the_ceiling_state()
    {
        var code = Unique("C5");
        var (_, _, sectionId) = await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");
        await SeedSetTargetBandAsync(userId, code, "HIGH");

        // A weak topic exists - so step 4 WOULD fire on its own condition. The ceiling is what stops it.
        await SeedSectionPracticeHistoryAsync(userId, code, sectionId, correctCount: 3, totalCount: 10,
            categoryCode: "TENSE", categoryName: "Verb tense");
        await SeedAnsweredTodayAsync(userId, code, sectionId,
            FreeDrillState.DailyObjectiveCeiling);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(NextActionCascade.DailyCeiling, dashboard.NextAction.CascadeStep);
        // The ceiling state offers review, not another drill: a recommendation that cannot start is
        // the defect step 5 exists to prevent.
        Assert.DoesNotContain("Drill", dashboard.NextAction.Title);
        Assert.DoesNotContain("Start practice", dashboard.NextAction.ActionLabel);
    }

    /// <summary>
    /// <b>Every cascade step is one of LRN-14's six.</b> A new branch that returns a seventh name
    /// compiles and renders, and nothing else notices - so the set is asserted against the published
    /// order rather than against itself.
    /// </summary>
    [Fact]
    public async Task Every_action_names_a_step_on_the_published_cascade()
    {
        var code = Unique("C6");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Contains(dashboard.NextAction.CascadeStep, NextActionCascade.Order);
    }

    /// <summary>
    /// LRN-14's order is data, and its sequence is the contract: six steps, in this order. Asserted as
    /// a list rather than by six inline comparisons so a reordering fails in one readable place.
    /// </summary>
    [Fact]
    public void The_cascade_order_is_the_six_steps_in_the_published_sequence()
    {
        Assert.Equal(
            new[]
            {
                "resume_in_progress",
                "retry_unmeasured_skill",
                "next_step",
                "free_drill",
                "daily_ceiling",
                "completion",
            },
            NextActionCascade.Order);
    }

    // ---- LRN-13: no placement is ONE block, one action, and a sentence ----

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> an unplaced learner treated as placed, so
    /// the page renders three blocks of empty figures - what the product used to do, and what LRN-13
    /// forbids. The state the page keys on is <see cref="LearnerDashboard.IsUnplaced"/>, asserted here;
    /// <c>DashboardRenderedBlocksTests</c> asserts the one-block markup it produces.
    /// </summary>
    [Fact]
    public async Task An_unplaced_learner_is_marked_unplaced_and_no_skill_is_measured()
    {
        var code = Unique("U1");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        // A published placement exam, because "how long it takes" is the stored duration and a
        // certificate with no placement paper cannot state one.
        await SeedPlacementExamAsync(code, minutes: 45);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.True(dashboard.IsUnplaced);
        Assert.Equal(NextActionCascade.RetryUnmeasuredSkill, dashboard.NextAction.CascadeStep);
        // How long it takes, from the exam row - not an invented "about an hour".
        Assert.Contains("45 minutes", dashboard.NextAction.Description);
        Assert.Equal(0, dashboard.Skills.Count(s => s.Standing is { IsMeasured: true }));
        // #211: the overall is absent, not A1.
        Assert.Null(dashboard.Standing!.Overall);
    }

    /// <summary>
    /// LRN-13's sentence must say how long placement takes. With no placement paper there is no
    /// duration to state, so the hero says "one sitting" rather than inventing a number - the same
    /// no-proration rule every other derived figure follows.
    /// </summary>
    [Fact]
    public async Task With_no_placement_paper_the_sentence_states_no_invented_duration()
    {
        var code = Unique("U3");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(NextActionCascade.RetryUnmeasuredSkill, dashboard.NextAction.CascadeStep);
        Assert.Contains("one sitting", dashboard.NextAction.Description);
        Assert.DoesNotContain("minutes", dashboard.NextAction.Description);
    }

    /// <summary>
    /// LRN-13's positive half: once a placement measures the vector, the learner is no longer
    /// unplaced and the page owes all three blocks. The contrast with the test above is the whole
    /// point - one block is a state, not a template.
    /// </summary>
    [Fact]
    public async Task A_placed_learner_is_not_unplaced_and_has_a_measured_vector()
    {
        var code = Unique("U2");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.False(dashboard.IsUnplaced);
        Assert.All(dashboard.Skills, s => Assert.True(s.Standing!.IsMeasured));
        Assert.NotNull(dashboard.Standing!.Overall);
    }

    // ---- #211: a placed learner's levels come from the stored state, and absence is absence ----

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> a vector where one skill has no row, and
    /// the page fills it with A1 because "every CEFR thing needs a level". #211 stores nothing for an
    /// unmeasured skill and this must stay null all the way to the card.
    /// </summary>
    [Fact]
    public async Task A_skill_with_no_stored_state_has_no_level_and_is_not_A1()
    {
        var code = Unique("S1");
        await SeedCertWithNamedSkillsAsync(code,
            ("LIST", "Listening", "listening"),
            ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");
        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{code}');
            DELETE FROM LearnerSkillStates
              WHERE UserId = {userId} AND CertId = @cert
                AND SkillId = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');
            """);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        var reading = Assert.Single(dashboard.Skills, s => s.Code == "READ");
        Assert.Null(reading.Standing);
        // The other skill still reports its measured level, so this is not "the vector is empty".
        Assert.Equal("B1", Assert.Single(dashboard.Skills, s => s.Code == "LIST").Standing!.Level!.Value.Code);
    }

    /// <summary>
    /// #211's overall is the weakest MEASURED skill and its limiting skill is named. Asserted here
    /// because the dashboard's block 2 prints it, and a second MIN computed on this side would be the
    /// two-writers defect the standing record exists to prevent.
    /// </summary>
    [Fact]
    public async Task The_overall_is_the_weakest_measured_skill_and_its_name_travels_with_it()
    {
        var code = Unique("S2");
        await SeedCertWithNamedSkillsAsync(code,
            ("LIST", "Listening", "listening"),
            ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");

        // LIST weaker than READ, with distinct positions so the tie-break is not what decides.
        await SeedSkillStateAsync(userId, code, "LIST", "A2", 40, 2.40m, "2026-09-01T10:00:00+07:00");
        await SeedSkillStateAsync(userId, code, "READ", "B2", 70, 4.70m, "2026-09-01T10:00:00+07:00");

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(CefrLevel.A2, dashboard.Standing!.Overall);
        Assert.Equal("LIST", dashboard.Standing.LimitingSkillCode);
        Assert.Equal("Listening", dashboard.Standing.LimitingSkillName);
    }

    /// <summary>
    /// The position travels as a POSITION, and the measured date travels with it so an old result
    /// cannot present itself as today's (LRN-19 / LRN-20's provenance rule, on this screen).
    /// </summary>
    [Fact]
    public async Task A_measured_skill_carries_its_position_and_its_measurement_date()
    {
        var code = Unique("S3");
        await SeedCertWithNamedSkillsAsync(code, ("READ", "Reading", "reading"));
        var userId = await SeedLearnerAsync(code);
        await SeedCompletedPlacementAsync(userId, code, 120, "LOW", "2026-09-01T10:00:00+07:00");
        await SeedSkillStateAsync(userId, code, "READ", "B2", 78, 4.78m, "2026-09-01T10:00:00+07:00");

        var card = Assert.Single((await CreateService().GetDashboardAsync(userId)).Skills);

        Assert.Equal("B2", card.Standing!.Level!.Value.Code);
        Assert.Equal(78, card.Standing.PositionInBand);
        Assert.NotNull(card.Standing.MeasuredAt);
    }
}
