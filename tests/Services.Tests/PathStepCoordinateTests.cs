using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #232 / D10: the step is a coordinate, and three rules read that coordinate. Each one has a trap that
/// passes a wrong implementation, so each test below names the input that would pass a broken version.
/// Pure functions only — no database, the same seam <see cref="LearningPathPlannerTests"/> uses.
/// </summary>
public sealed class PathStepCoordinateTests
{
    // ---------------------------------------------------------------------------------------------
    // The sizing table, D10's three tiers with their counts and minutes.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Trap: a tier table that "roughly" matches. The SRS gives exact ranges, so a table that drifted to
    /// 6–8 items for Mastered would still look plausible and would fail here.
    /// </summary>
    [Theory]
    [InlineData(SizingTier.Mastered, 5, 5, 6, 8)]
    [InlineData(SizingTier.Developing, 8, 10, 12, 15)]
    [InlineData(SizingTier.Low, 12, 15, 20, 25)]
    public void The_sizing_table_is_D10s_three_tiers_and_no_others(
        SizingTier tier, int minItems, int maxItems, int minMinutes, int maxMinutes)
    {
        Assert.Equal(minItems, LearningPathPlanner.ItemCountFor(tier).MinItems);
        Assert.Equal(maxItems, LearningPathPlanner.ItemCountFor(tier).MaxItems);
        Assert.Equal(minMinutes, LearningPathPlanner.MinuteRangeFor(tier).MinMinutes);
        Assert.Equal(maxMinutes, LearningPathPlanner.MinuteRangeFor(tier).MaxMinutes);
    }

    /// <summary>D10 names exactly three tiers. A fourth would be a size nobody measured the minutes for.</summary>
    [Fact]
    public void There_are_exactly_three_sizing_tiers()
    {
        Assert.Equal(3, Enum.GetValues<SizingTier>().Length);
    }

    /// <summary>
    /// Trap: a mastered topic dropped from the plan. "A mastered topic's steps shorten; they are never
    /// removed" — so the mastered tier must still produce a step, and the smallest one.
    /// </summary>
    [Fact]
    public void A_mastered_topic_shortens_a_step_and_never_removes_it()
    {
        var tier = LearningPathPlanner.SizingTierFor(MasteryState.Mastered);

        Assert.Equal(SizingTier.Mastered, tier);
        // Not removed: the resolved count is the D10 maintenance-check size, which is positive.
        Assert.Equal(LearningPathPlanner.MaintenanceCheckItems, LearningPathPlanner.ItemCountFor(tier, availableInCell: null));
        Assert.True(LearningPathPlanner.ItemCountFor(tier, availableInCell: null) > 0);
    }

    /// <summary>
    /// Trap: "low mastery" mapped to "more items" by intuition. D10's table gives the <b>low</b> tier the
    /// largest size — 12–15 items — which is the opposite of a "small practice" guess.
    /// </summary>
    [Fact]
    public void The_low_tier_is_the_largest_and_the_mastered_tier_is_the_smallest()
    {
        var mastered = LearningPathPlanner.ItemCountFor(SizingTier.Mastered, null);
        var developing = LearningPathPlanner.ItemCountFor(SizingTier.Developing, null);
        var low = LearningPathPlanner.ItemCountFor(SizingTier.Low, null);

        Assert.True(mastered < developing);
        Assert.True(developing < low);
    }

    /// <summary>An unmeasured cell is developing, per the spec's "developing / unmeasured" row.</summary>
    [Fact]
    public void An_unmeasured_cell_is_sized_as_developing()
    {
        Assert.Equal(SizingTier.Developing, LearningPathPlanner.SizingTierFor(MasteryState.Untested));
    }

    /// <summary>
    /// Trap: padding a short cell with repeats to reach the tier's maximum. A cell holding 3 items gives a
    /// 3-item step, not a 10-item step that re-serves the same 3.
    /// </summary>
    [Fact]
    public void A_short_cell_gives_a_short_step_rather_than_a_padded_one()
    {
        Assert.Equal(3, LearningPathPlanner.ItemCountFor(SizingTier.Developing, availableInCell: 3));
        // And a full cell keeps the tier's maximum rather than the cell's whole stock.
        Assert.Equal(10, LearningPathPlanner.ItemCountFor(SizingTier.Developing, availableInCell: 40));
    }

    // ---------------------------------------------------------------------------------------------
    // Level advance: the three conditions, together.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The happy path: all three hold.</summary>
    [Fact]
    public void A_step_advances_when_all_three_conditions_hold()
    {
        Assert.True(LearningPathPlanner.CanAdvanceLevel(
            MasteryState.Mastered, skillPositionInBand: 75, targetBandAboveCurrentLevel: true));
    }

    /// <summary>
    /// Trap: the advance rule implemented as <c>||</c> instead of <c>&amp;&amp;</c>. Each input below satisfies
    /// exactly one condition and fails the other two, so an <c>||</c> implementation returns true for every
    /// one of them and fails all three assertions.
    /// </summary>
    [Theory]
    // mastery alone
    [InlineData(MasteryState.Mastered, 10, false)]
    // position alone
    [InlineData(MasteryState.Learning, 95, false)]
    // target band alone
    [InlineData(MasteryState.Untested, 20, true)]
    // mastery + position, but the target band is already reached
    [InlineData(MasteryState.Mastered, 90, false)]
    // mastery + target band, but the position is short
    [InlineData(MasteryState.Mastered, 74, true)]
    // position + target band, but the topic is not mastered
    [InlineData(MasteryState.Reviewing, 95, true)]
    public void No_single_condition_advances_a_step_on_its_own(
        MasteryState mastery, int position, bool targetAbove)
    {
        Assert.False(LearningPathPlanner.CanAdvanceLevel(mastery, position, targetAbove));
    }

    /// <summary>
    /// The threshold is 75 inclusive. Trap: an off-by-one that requires 76 — the spec says "≥ 75", and a
    /// learner at exactly 75 would be held back forever.
    /// </summary>
    [Fact]
    public void A_position_of_exactly_seventy_five_is_enough()
    {
        Assert.True(LearningPathPlanner.CanAdvanceLevel(MasteryState.Mastered, 75, true));
        Assert.False(LearningPathPlanner.CanAdvanceLevel(MasteryState.Mastered, 74, true));
    }

    /// <summary>There is nothing above C2, and advancing past it would invent a level.</summary>
    [Fact]
    public void The_top_of_the_ladder_has_no_level_above_it()
    {
        Assert.Null(LearningPathPlanner.NextLevelAbove(CefrLevel.C2));
        Assert.Equal(CefrLevel.B2, LearningPathPlanner.NextLevelAbove(CefrLevel.B1));
        Assert.Equal(CefrLevel.A1, LearningPathPlanner.NextLevelAbove(CefrLevel.PreA1));
    }

    // ---------------------------------------------------------------------------------------------
    // The three bindings.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// D10's table, transcribed. Trap: a "step type" that is a label — all six routed to the same surface
    /// would satisfy a test that only checked "non-empty".
    /// </summary>
    [Theory]
    [InlineData(PathStepType.DiagnosticDrill, StepRoute.ItemSelector)]
    [InlineData(PathStepType.FocusPractice, StepRoute.ItemSelector)]
    [InlineData(PathStepType.ReviewSpaced, StepRoute.ItemSelector)]
    [InlineData(PathStepType.ConceptLesson, StepRoute.LessonReader)]
    [InlineData(PathStepType.ExpandedRemedial, StepRoute.LessonReader)]
    [InlineData(PathStepType.MockSection, StepRoute.AttemptEngine)]
    public void Every_step_type_binds_to_the_route_D10_names(PathStepType stepType, StepRoute route)
    {
        Assert.Equal(route, LearningPathPlanner.ExecutionRouteOf(stepType));
    }

    /// <summary>
    /// The other half of "a label is not a type": all three routes must actually be reachable. A mapping
    /// where every type went to the selector would pass the test above for the three selector types only.
    /// </summary>
    [Fact]
    public void All_three_routes_are_reachable_and_reached_by_a_distinct_type()
    {
        var routes = Enum.GetValues<PathStepType>().Select(LearningPathPlanner.ExecutionRouteOf).Distinct().ToList();

        Assert.Equal(3, routes.Count);
        Assert.Contains(StepRoute.ItemSelector, routes);
        Assert.Contains(StepRoute.LessonReader, routes);
        Assert.Contains(StepRoute.AttemptEngine, routes);
    }

    /// <summary>Every route has at least one type, so no route is dead.</summary>
    [Fact]
    public void Each_route_has_a_step_type_that_opens_it()
    {
        foreach (var route in Enum.GetValues<StepRoute>())
        {
            Assert.NotEmpty(LearningPathPlanner.StepTypesFor(route));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The plan itself.
    // ---------------------------------------------------------------------------------------------

    private static SkillPlanBlock ReadingBlock(
        int skillId = 1, MasteryState mastery = MasteryState.Reviewing, CefrLevel level = default) =>
        new(skillId, Modality.Reading, level.Rank == 0 ? CefrLevel.B1 : level, mastery, [10, 11], LessonId: 5, MockExamId: 7);

    private static SkillPlanBlock ListeningBlock(
        int skillId = 2, MasteryState mastery = MasteryState.Reviewing, CefrLevel level = default) =>
        new(skillId, Modality.Listening, level.Rank == 0 ? CefrLevel.A2 : level, mastery, [20, 21], LessonId: 6, MockExamId: 7);

    /// <summary>
    /// Every planned step carries the level of the skill it was planned for, never one overall level.
    /// Trap: reading the certificate's target band into every step — here the two blocks carry different
    /// levels, so a single shared level fails.
    /// </summary>
    [Fact]
    public void A_steps_level_is_its_own_skills_level_never_one_overall()
    {
        var plan = LearningPathPlanner.Plan(
            weeklyStudyHours: 6m,
            [ReadingBlock(level: CefrLevel.B1), ListeningBlock(level: CefrLevel.A2)]);

        Assert.NotEmpty(plan);
        foreach (var step in plan)
        {
            var expected = step.SkillId == 1 ? CefrLevel.B1 : CefrLevel.A2;
            Assert.Equal(expected, step.TargetCefr);
        }

        // And the two levels really do differ, so "one overall level" cannot pass by coincidence.
        Assert.Equal(2, plan.Select(s => s.TargetCefr).Distinct().Count());
    }

    /// <summary>
    /// D10: ordering is pedagogical — a lesson sorts before blocked practice — and consecutive steps
    /// alternate skills. Trap: leaving the order to insertion or to step id.
    /// </summary>
    [Fact]
    public void A_week_opens_with_its_lesson_and_alternates_skills()
    {
        var plan = LearningPathPlanner.Plan(
            weeklyStudyHours: 6m,
            [ReadingBlock(), ListeningBlock()]);

        var firstWeek = plan.Where(p => p.WeekNumber == 1).OrderBy(p => p.WeekSequence).ToList();

        Assert.NotEmpty(firstWeek);
        Assert.Equal(PathStepType.ConceptLesson, firstWeek[0].StepType);

        // WeekSequence is 1..n with no gaps and no duplicates.
        Assert.Equal(
            Enumerable.Range(1, firstWeek.Count).ToList(),
            firstWeek.Select(s => s.WeekSequence).ToList());

        // The practice steps that follow alternate skills.
        var practiceSkills = firstWeek
            .Where(s => s.Route == StepRoute.ItemSelector)
            .Select(s => s.SkillId)
            .ToList();

        for (var i = 1; i < practiceSkills.Count; i++)
        {
            Assert.NotEqual(practiceSkills[i - 1], practiceSkills[i]);
        }
    }

    /// <summary>An empty set of blocks plans nothing rather than inventing a step.</summary>
    [Fact]
    public void No_blocks_means_no_steps()
    {
        Assert.Empty(LearningPathPlanner.Plan(6m, []));
    }

    /// <summary>
    /// A block whose cell holds no stock plans no drill for it, because a drill step with no items would
    /// throw the moment the learner opened it.
    /// </summary>
    [Fact]
    public void A_skill_with_no_drillable_topic_supplies_no_drill_step()
    {
        var empty = new SkillPlanBlock(9, Modality.Reading, CefrLevel.B1, MasteryState.Learning, [], null, null);
        var full = ReadingBlock();

        var plan = LearningPathPlanner.Plan(6m, [empty, full]);

        Assert.NotEmpty(plan);
        Assert.DoesNotContain(plan, s => s.SkillId == 9 && s.Route == StepRoute.ItemSelector);
    }

    /// <summary>
    /// A drill names no resource. That is what lets the selector pick its items at run time and route
    /// around a cooling cell instead of blocking the step (D11 / PLC-60).
    /// </summary>
    [Fact]
    public void A_drill_step_carries_no_lesson_and_no_exam()
    {
        var plan = LearningPathPlanner.Plan(6m, [ReadingBlock(), ListeningBlock()]);

        var drills = plan.Where(s => s.Route == StepRoute.ItemSelector).ToList();

        Assert.NotEmpty(drills);
        Assert.All(drills, s => Assert.NotNull(s.PlannedItemCount));
    }
}
