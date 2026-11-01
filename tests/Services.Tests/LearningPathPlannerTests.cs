using System;
using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #154: the plan behind a path is arithmetic, not a template lookup. These tests pin the rules the
/// ticket fixes — four weeks, 3–5 steps a week, the 60/25/15 split — and the one thing that makes two
/// learners with the same band pair differ: which sections the plan drills.
/// </summary>
public sealed class LearningPathPlannerTests
{
    [Fact]
    public void Six_hours_a_week_plans_four_weeks_of_three_to_five_steps()
    {
        var plan = LearningPathPlanner.Plan(
            weeklyStudyHours: 6m, weakSectionCount: 6, lessonCount: 4, mockExamCount: 2);

        Assert.Equal(LearningPathPlanner.WindowWeeks, plan.Select(p => p.WeekNumber).Distinct().Count());

        for (var week = 1; week <= LearningPathPlanner.WindowWeeks; week++)
        {
            var steps = plan.Count(p => p.WeekNumber == week);
            Assert.InRange(steps, LearningPathPlanner.MinimumStepsPerWeek, LearningPathPlanner.MaximumStepsPerWeek);
        }
    }

    [Fact]
    public void Every_step_is_a_sitting_of_no_more_than_forty_five_minutes()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, weakSectionCount: 6, lessonCount: 4, mockExamCount: 2);

        Assert.All(plan, step =>
            Assert.InRange(step.EstimatedMinutes, LearningPathPlanner.MinimumStepMinutes, LearningPathPlanner.MaximumStepMinutes));
    }

    [Fact]
    public void A_heavier_pace_plans_more_minutes_per_week_than_a_lighter_one()
    {
        // The volume is the goal's, not a constant: 10 h/week must plan materially more than 3 h/week.
        var light = LearningPathPlanner.Plan(weeklyStudyHours: 3m, weakSectionCount: 6, lessonCount: 4, mockExamCount: 2);
        var heavy = LearningPathPlanner.Plan(weeklyStudyHours: 10m, weakSectionCount: 6, lessonCount: 4, mockExamCount: 2);

        var lightWeek = LearningPathPlanner.PlannedMinutesForWeek(light, 1);
        var heavyWeek = LearningPathPlanner.PlannedMinutesForWeek(heavy, 1);

        Assert.True(heavyWeek > lightWeek, $"10 h/week planned {heavyWeek} min, 3 h/week planned {lightWeek} min");
        Assert.Equal(180, lightWeek);
    }

    [Fact]
    public void A_week_never_plans_more_minutes_than_the_learner_said_they_have()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 2m, weakSectionCount: 6, lessonCount: 4, mockExamCount: 2);

        for (var week = 1; week <= LearningPathPlanner.WindowWeeks; week++)
        {
            Assert.True(LearningPathPlanner.PlannedMinutesForWeek(plan, week) <= 120,
                $"week {week} planned more than the 2 h the learner has");
        }
    }

    [Fact]
    public void Practice_outnumbers_lesson_and_review_which_outnumber_the_mock()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, weakSectionCount: 6, lessonCount: 4, mockExamCount: 2);

        var practice = plan.Count(p => p.Kind == PathStepKind.Practice);
        var lessonAndReview = plan.Count(p => p.Kind is PathStepKind.Lesson or PathStepKind.Review);
        var mock = plan.Count(p => p.Kind == PathStepKind.MockExam);

        Assert.True(practice > lessonAndReview, $"practice {practice}, lesson+review {lessonAndReview}");
        Assert.True(lessonAndReview > mock, $"lesson+review {lessonAndReview}, mock {mock}");
    }

    [Fact]
    public void A_mock_is_planned_at_the_end_of_each_two_week_cycle_and_never_in_week_one()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, weakSectionCount: 6, lessonCount: 4, mockExamCount: 2);

        Assert.DoesNotContain(plan, p => p.WeekNumber == 1 && p.Kind == PathStepKind.MockExam);
        Assert.Contains(plan, p => p.WeekNumber == 2 && p.Kind == PathStepKind.MockExam);
        Assert.Contains(plan, p => p.WeekNumber == 4 && p.Kind == PathStepKind.MockExam);
    }

    [Fact]
    public void The_plan_drills_only_the_two_weakest_sections()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, weakSectionCount: 6, lessonCount: 4, mockExamCount: 2);

        var ranks = plan.Where(p => p.Kind is PathStepKind.Practice or PathStepKind.Review)
            .Select(p => p.SectionRank)
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        Assert.Equal([0, 1], ranks);
    }

    [Fact]
    public void A_certificate_with_no_lessons_or_mocks_gets_a_plan_without_them()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, weakSectionCount: 3, lessonCount: 0, mockExamCount: 0);

        Assert.NotEmpty(plan);
        Assert.DoesNotContain(plan, p => p.Kind == PathStepKind.Lesson);
        Assert.DoesNotContain(plan, p => p.Kind == PathStepKind.MockExam);
    }

    [Fact]
    public void A_certificate_with_nothing_to_drill_plans_no_practice_rather_than_inventing_one()
    {
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, weakSectionCount: 0, lessonCount: 0, mockExamCount: 0);

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
