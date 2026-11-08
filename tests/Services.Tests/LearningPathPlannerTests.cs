using System;
using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #154 / #232: the plan behind a path is arithmetic, not a template lookup. These tests pin the rules
/// the tickets fix — four weeks, 3–5 steps a week, the 60/25/15 split — and the one thing that makes two
/// learners with the same band pair differ: which skill and topic the plan drills.
/// <para>
/// #232 replaced the section-rank input with a set of <see cref="SkillPlanBlock"/>s, because a step is a
/// coordinate. The behaviours below are the same behaviours, expressed at the new seam.
/// </para>
/// </summary>
public sealed class LearningPathPlannerTests
{
    /// <summary>Four skills with two topics each, one lesson and one mock: the shape the 60/25/15 split
    /// was decided against.</summary>
    private static SkillPlanBlock[] FourSkills() =>
    [
        Block(1, Modality.Reading, [10, 11]),
        Block(2, Modality.Reading, [12, 13]),
        Block(3, Modality.Listening, [20, 21]),
        Block(4, Modality.Listening, [22, 23]),
    ];

    private static SkillPlanBlock Block(int skillId, Modality modality, int[] topics, int? lessonId = 4, int? mockId = 2) =>
        new(skillId, modality, CefrLevel.B1, MasteryState.Reviewing, topics, lessonId, mockId);

    /// <summary>
    /// #234: the old "3–5 steps a week" bound is replaced by the budget: four weeks are planned, and each
    /// one's <b>minutes</b> are the goal's. What this now pins is that the window is four weeks and no
    /// week is empty — the step <i>count</i> is no longer a constant, which was the defect.
    /// </summary>
    [Fact]
    public void Six_hours_a_week_plans_four_weeks_of_steps_that_sum_to_the_budget()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, FourSkills());

        Assert.Equal(LearningPathPlanner.WindowWeeks, plan.Select(p => p.WeekNumber).Distinct().Count());

        for (var week = 1; week <= LearningPathPlanner.WindowWeeks; week++)
        {
            var minutes = LearningPathPlanner.PlannedMinutesForWeek(plan, week);
            Assert.Equal(360, minutes);
            Assert.True(plan.Count(p => p.WeekNumber == week) >= 1, $"week {week} had no steps");
        }
    }

    /// <summary>
    /// #234: unchanged in intent and now backed by arithmetic — every step is still a sitting. What was
    /// previously true by construction (the budget was divided by the step count) is now a property of
    /// the packing: minutes are split so no sitting exceeds the ceiling.
    /// </summary>
    [Fact]
    public void Every_step_is_a_sitting_of_no_more_than_forty_five_minutes()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, FourSkills());

        Assert.All(plan, step =>
            Assert.InRange(step.EstimatedMinutes, LearningPathPlanner.MinimumStepMinutes, LearningPathPlanner.MaximumStepMinutes));
    }

    [Fact]
    public void A_heavier_pace_plans_more_minutes_per_week_than_a_lighter_one()
    {
        // The volume is the goal's, not a constant: 10 h/week must plan materially more than 3 h/week.
        var light = LearningPathPlanner.Plan(weeklyStudyHours: 3m, FourSkills());
        var heavy = LearningPathPlanner.Plan(weeklyStudyHours: 10m, FourSkills());

        var lightWeek = LearningPathPlanner.PlannedMinutesForWeek(light, 1);
        var heavyWeek = LearningPathPlanner.PlannedMinutesForWeek(heavy, 1);

        Assert.True(heavyWeek > lightWeek, $"10 h/week planned {heavyWeek} min, 3 h/week planned {lightWeek} min");
        Assert.Equal(180, lightWeek);
    }

    [Fact]
    public void A_week_never_plans_more_minutes_than_the_learner_said_they_have()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 2m, FourSkills());

        for (var week = 1; week <= LearningPathPlanner.WindowWeeks; week++)
        {
            Assert.True(LearningPathPlanner.PlannedMinutesForWeek(plan, week) <= 120,
                $"week {week} planned more than the 2 h the learner has");
        }
    }

    [Fact]
    public void Practice_outnumbers_lesson_and_review_which_outnumber_the_mock()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, FourSkills());

        var practice = plan.Count(p => p.StepType == PathStepType.FocusPractice);
        var lessonAndReview = plan.Count(p => p.StepType is PathStepType.ConceptLesson or PathStepType.ReviewSpaced);
        var mock = plan.Count(p => p.StepType == PathStepType.MockSection);

        Assert.True(practice > lessonAndReview, $"practice {practice}, lesson+review {lessonAndReview}");
        Assert.True(lessonAndReview > mock, $"lesson+review {lessonAndReview}, mock {mock}");
    }

    [Fact]
    public void A_mock_is_planned_at_the_end_of_each_two_week_cycle_and_never_in_week_one()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, FourSkills());

        Assert.DoesNotContain(plan, p => p.WeekNumber == 1 && p.StepType == PathStepType.MockSection);
        Assert.Contains(plan, p => p.WeekNumber == 2 && p.StepType == PathStepType.MockSection);
        Assert.Contains(plan, p => p.WeekNumber == 4 && p.StepType == PathStepType.MockSection);
    }

    /// <summary>
    /// #232: the plan drills only the blocks it is given, and only the topics inside them. The old test
    /// asserted "ranks 0 and 1"; the coordinate equivalent is "the drilled topics are a subset of the
    /// supplied ones", which is what makes a plan personal rather than universal.
    /// </summary>
    [Fact]
    public void The_plan_drills_only_topics_it_was_handed()
    {
        var blocks = FourSkills();
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, blocks);

        var supplied = blocks.SelectMany(b => b.TopicIds).ToHashSet();
        var drilled = plan
            .Where(p => p.Route == StepRoute.ItemSelector)
            .Select(p => p.TopicId)
            .Distinct()
            .ToList();

        Assert.NotEmpty(drilled);
        Assert.All(drilled, topicId => Assert.Contains(topicId, supplied));
    }

    [Fact]
    public void A_certificate_with_no_lessons_or_mocks_gets_a_plan_without_them()
    {
        var blocks = new[]
        {
            Block(1, Modality.Reading, [10, 11], lessonId: null, mockId: null),
            Block(2, Modality.Listening, [20, 21], lessonId: null, mockId: null),
        };

        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, blocks);

        Assert.NotEmpty(plan);
        Assert.DoesNotContain(plan, p => p.StepType == PathStepType.ConceptLesson);
        Assert.DoesNotContain(plan, p => p.StepType == PathStepType.MockSection);
    }

    [Fact]
    public void A_certificate_with_nothing_to_drill_plans_no_practice_rather_than_inventing_one()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, []);

        Assert.Empty(plan);
    }

    [Theory]
    [InlineData(5, 4, 100d, 70, true)]     // 4 of 5 is exactly the 80% rule
    [InlineData(3, 2, 100d, 70, false)]    // 67% of the steps is below the 80% rule
    [InlineData(3, 3, 65d, 70, false)]     // all done but accuracy missed
    [InlineData(3, 3, 70d, 70, true)]      // accuracy exactly at the bar counts
    [InlineData(3, 3, null, 70, true)]     // a lesson week has no accuracy to fall below
    [InlineData(0, 0, null, 70, false)]    // an empty week is not "on track"
    public void A_week_is_on_track_only_when_both_the_count_and_the_accuracy_hold(
        int planned, int completed, double? accuracy, int target, bool expected)
    {
        var onTrack = LearningPathPlanner.IsWeekOnTrack(
            planned, completed, accuracy is null ? null : (decimal)accuracy.Value, target);

        Assert.Equal(expected, onTrack);
    }
}
