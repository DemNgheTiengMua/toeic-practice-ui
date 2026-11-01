using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>#154: what a generated step is made of, before it has an id. The planner decides the
/// shape; the service decides which real section / lesson / exam fills each slot.</summary>
public enum PathStepKind
{
    /// <summary>Drill one of the learner's weak sections.</summary>
    Practice,

    /// <summary>Read a lesson, then answer its micro-check.</summary>
    Lesson,

    /// <summary>Spaced review of a section drilled in an earlier week.</summary>
    Review,

    /// <summary>A timed mock exam, at the end of each two-week cycle.</summary>
    MockExam,
}

/// <summary>#154: one planned step. <see cref="SectionRank"/> is the position of the section in the
/// learner's weakness order (0 = weakest), so the service can pick the named section without
/// re-deciding which one is weak.</summary>
public sealed record PlannedPathStep(
    int WeekNumber,
    PathStepKind Kind,
    int EstimatedMinutes,
    int SectionRank);

/// <summary>
/// #154: the plan behind a four-week learning path — three to five steps a week, 60% practice on the
/// two weakest sections, 25% lesson plus spaced review, 15% mock at the end of each two-week cycle.
/// The split is the spec's; the minutes come from the goal's weekly hours.
/// Pure — no database, no clock, no AI. An insight narrative may describe this plan; it never
/// re-plans it.
/// </summary>
public static class LearningPathPlanner
{
    /// <summary>The window the ticket fixes at four weeks.</summary>
    public const int WindowWeeks = 4;

    public const int MinimumStepsPerWeek = 3;
    public const int MaximumStepsPerWeek = 5;

    /// <summary>#154: a step is a sitting, not an afternoon.</summary>
    public const int MinimumStepMinutes = 10;
    public const int MaximumStepMinutes = 45;

    /// <summary>#154: how many of the certificate's sections the plan drills. The spec says the two
    /// weakest, and a plan that drills eight sections spreads the learner thin.</summary>
    public const int DrilledSectionCount = 2;

    /// <summary>#154: the week a path plans when the goal recorded no pace. The handoff says a goal
    /// with no hours has no goal (#153 refuses to judge it), but the placement flow still writes a
    /// band pair, and refusing to build any path from that would take a working screen away. Four
    /// weeks at this pace is the smallest plan the ticket allows, and the screen says which pace it
    /// was built on.</summary>
    public const decimal DefaultWeeklyHours = 3m;

    /// <summary>
    /// Builds the plan for one learner.
    /// </summary>
    /// <param name="weeklyStudyHours">The goal's pace, already validated by #153 to be 0–25.</param>
    /// <param name="weakSectionCount">How many sections the certificate offers that the learner could
    /// drill by offset from the weakness order. Zero means the plan has no practice steps rather
    /// than an invented one.</param>
    /// <param name="lessonCount">How many lessons the certificate has. Zero means no lesson steps.</param>
    /// <param name="mockExamCount">How many mock exams exist. Zero means no mock steps, rather than
    /// a step that can never be finished.</param>
    public static IReadOnlyList<PlannedPathStep> Plan(
        decimal weeklyStudyHours,
        int weakSectionCount,
        int lessonCount,
        int mockExamCount)
    {
        var weekBudget = (int)Math.Round(weeklyStudyHours * 60m);
        if (weekBudget <= 0)
        {
            return [];
        }

        // A week holds 3–5 sittings. The pace decides how many: a 3 h week is four sittings of 45
        // minutes; a 6 h week is still five sittings, each capped at 45 — the leftover is the
        // learner's, and the plan says so rather than stretching a sitting past what a sitting is.
        var stepsPerWeek = Math.Clamp((int)Math.Round(weekBudget / 40m), MinimumStepsPerWeek, MaximumStepsPerWeek);

        var planned = new List<PlannedPathStep>(WindowWeeks * MaximumStepsPerWeek);

        for (var week = 1; week <= WindowWeeks; week++)
        {
            // 15% mock, at the end of each two-week cycle.
            var wantsMock = week % 2 == 0 && mockExamCount > 0;

            // 25% lesson plus spaced review; a three-step week is already spent on practice.
            var wantsLesson = lessonCount > 0 && stepsPerWeek > MinimumStepsPerWeek;

            // 60% practice; review is whatever the other three leave.
            var practiceCount = weakSectionCount > 0
                ? Math.Max(1, (int)Math.Round(stepsPerWeek * 0.60m, MidpointRounding.AwayFromZero))
                : 0;

            var reviewCount = stepsPerWeek - practiceCount - (wantsLesson ? 1 : 0) - (wantsMock ? 1 : 0);
            if (reviewCount < 0)
            {
                practiceCount += reviewCount;
                reviewCount = 0;
            }

            if (reviewCount > 0 && weakSectionCount == 0)
            {
                reviewCount = 0;
            }

            var kinds = new List<(PathStepKind Kind, int Rank)>(stepsPerWeek);

            for (var i = 0; i < practiceCount; i++)
            {
                kinds.Add((PathStepKind.Practice, i % DrilledSectionCount));
            }

            if (wantsLesson)
            {
                kinds.Add((PathStepKind.Lesson, 0));
            }

            for (var i = 0; i < reviewCount; i++)
            {
                kinds.Add((PathStepKind.Review, i % DrilledSectionCount));
            }

            if (wantsMock)
            {
                kinds.Add((PathStepKind.MockExam, 0));
            }

            if (kinds.Count == 0)
            {
                continue;
            }

            // The week's minutes add up to the goal's pace, shared evenly, so "3 h a week" is three
            // hours of plan and not a number the planner made up beside it.
            var baseMinutes = weekBudget / kinds.Count;
            var remainder = weekBudget % kinds.Count;
            for (var i = 0; i < kinds.Count; i++)
            {
                var minutes = Math.Clamp(
                    baseMinutes + (i < remainder ? 1 : 0),
                    MinimumStepMinutes,
                    MaximumStepMinutes);

                var (kind, rank) = kinds[i];
                planned.Add(new PlannedPathStep(
                    week,
                    kind,
                    minutes,
                    weakSectionCount > 0 ? Math.Min(rank, weakSectionCount - 1) : 0));
            }
        }

        return planned;
    }

    /// <summary>
    /// #154: whether a week counts as On Track. Both halves have to hold: enough of the week's steps
    /// finished, and the measured accuracy at or above the week's target. A week with no scored step
    /// is judged on the count alone — a lesson week has no accuracy to fall below.
    /// </summary>
    public static bool IsWeekOnTrack(
        int plannedSteps,
        int completedSteps,
        decimal? averageAccuracy,
        int targetAccuracyPct)
    {
        if (plannedSteps <= 0)
        {
            return false;
        }

        if (completedSteps * 100 < plannedSteps * 80)
        {
            return false;
        }

        return averageAccuracy is null || averageAccuracy.Value >= targetAccuracyPct;
    }

    /// <summary>#154: the accuracy a week is held to — 80% at the certificate's top band, 70% below
    /// it. Read off the certificate's own band order so a certificate with its own band vocabulary
    /// gets the same rule without a CEFR code map in code.</summary>
    public static int TargetAccuracyFor(bool isTopBand) => isTopBand ? 80 : 70;

    /// <summary>#154: the minutes a week's plan actually holds, which is never more than the learner
    /// has. A pace above five 45-minute sittings leaves the remainder unplanned.</summary>
    public static int PlannedMinutesForWeek(IReadOnlyList<PlannedPathStep> plan, int weekNumber) =>
        plan.Where(p => p.WeekNumber == weekNumber).Sum(p => p.EstimatedMinutes);
}
