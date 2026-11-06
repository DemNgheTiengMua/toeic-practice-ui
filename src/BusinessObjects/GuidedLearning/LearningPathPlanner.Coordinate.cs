using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #232 / D10: where a step sits, and the three rules that read that position. Everything here is a
/// pure function — no database, no clock — so the numbers the SRS wrote down can be asserted without a
/// server, the same seam <c>LearningPathPlannerTests</c> already tests at.
/// <para>
/// Three rules live here because each one has a trap that a test has to catch:
/// </para>
/// <list type="number">
/// <item><see cref="SizingTierFor"/> — the tier follows mastery, and a mastered topic's steps
/// <b>shorten</b>, they are never removed.</item>
/// <item><see cref="CanAdvanceLevel"/> — level advance needs <b>all three</b> conditions together. A
/// version that advanced on mastery alone is the failure #232 exists to prevent, so the three are
/// separate arguments and the implementation is a conjunction of all three, in that order.</item>
/// <item><see cref="ExecutionRouteOf"/> — the three step types reach three different surfaces. A type
/// that all three routes went through alike would not be three types.</item>
/// </list>
/// </summary>
public static partial class LearningPathPlanner
{
    /// <summary>D10: how far inside a band a skill must be before a step moves up a level.</summary>
    public const int AdvancePositionInBand = 75;

    /// <summary>D10: "5-item maintenance checks so a mastered topic does not decay unnoticed".</summary>
    public const int MaintenanceCheckItems = 5;

    /// <summary>D10's item counts, per tier. Lesson steps carry a count too — the items a lesson is
    /// followed by — so one table serves both shapes.</summary>
    public static (int MinItems, int MaxItems) ItemCountFor(SizingTier tier) => tier switch
    {
        SizingTier.Mastered => (5, 5),
        SizingTier.Developing => (8, 10),
        SizingTier.Low => (12, 15),
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    /// <summary>D10's minutes, per tier. Closed ranges as written: 6–8, 12–15, 20–25.</summary>
    public static (int MinMinutes, int MaxMinutes) MinuteRangeFor(SizingTier tier) => tier switch
    {
        SizingTier.Mastered => (6, 8),
        SizingTier.Developing => (12, 15),
        SizingTier.Low => (20, 25),
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    /// <summary>
    /// D10: the size a step takes, from the topic's mastery at the current level.
    /// <para>
    /// A mastered topic does not drop out of the plan — it shortens to a maintenance check. That is why
    /// this returns <see cref="SizingTier.Mastered"/> rather than null, and why a caller that treated
    /// "mastered" as "nothing left to do" would be wrong in a way only a test catches.
    /// </para>
    /// </summary>
    public static SizingTier SizingTierFor(MasteryState mastery) => mastery switch
    {
        MasteryState.Mastered => SizingTier.Mastered,
        MasteryState.Learning => SizingTier.Low,
        // Reviewing is "developing": the topic is measurably improving but is not yet mastered.
        MasteryState.Reviewing => SizingTier.Developing,
        // An unmeasured cell is developing, per the spec's "developing / unmeasured" row.
        _ => SizingTier.Developing,
    };

    /// <summary>
    /// D10: how many items a step at this tier actually contains.
    /// <para>
    /// The upper bound of the tier's range is what a full cell supplies; when the cell holds fewer, the
    /// step is <b>as long as the cell allows</b> and says so — it is never padded with repeats, and it
    /// never claims a count it cannot fill. <paramref name="availableInCell"/> is the count the selector
    /// found; pass <c>null</c> when nothing constrains it.
    /// </para>
    /// </summary>
    public static int ItemCountFor(SizingTier tier, int? availableInCell)
    {
        var (_, max) = ItemCountFor(tier);
        return availableInCell is { } available
            ? Math.Max(1, Math.Min(max, available))
            : max;
    }

    /// <summary>
    /// D10: whether a step moves to <c>level + 1</c>.
    /// <para>
    /// <b>All three conditions hold together.</b> They are three separate facts from three different
    /// tables — the cell's mastery (D4), the skill's position inside its band (D15), and the certificate's
    /// target band (D6) — and any one of them alone is a wrong answer:
    /// </para>
    /// <list type="bullet">
    /// <item>Mastery alone advances a learner the certificate's target has already passed, spending
    /// weeks one level above the point of the plan.</item>
    /// <item>Position alone advances a topic never measured at this level, on evidence from another
    /// topic.</item>
    /// <item>Target-band alone advances a learner who has not mastered what the current level
    /// contains.</item>
    /// </list>
    /// <para>
    /// <paramref name="targetBandAboveCurrentLevel"/> is passed in already resolved, rather than a band
    /// code: the certificate's band and the CEFR level are different quantities and no crosswalk exists
    /// (D1/D6). Whoever resolves it must have argued the comparison; this function only requires it.
    /// </para>
    /// </summary>
    public static bool CanAdvanceLevel(
        MasteryState masteryAtCurrentLevel,
        int skillPositionInBand,
        bool targetBandAboveCurrentLevel) =>
        masteryAtCurrentLevel == MasteryState.Mastered
        && skillPositionInBand >= AdvancePositionInBand
        && targetBandAboveCurrentLevel;

    /// <summary>D10: the next authored level above <paramref name="current"/>, or null at C2 — there is
    /// nothing above the top of the ladder, and advancing past it would invent a level. The reportable
    /// floor advances to A1, which is the bottom rung of the authored ladder.</summary>
    public static CefrLevel? NextLevelAbove(CefrLevel current)
    {
        if (current.IsReportableFloor)
        {
            return CefrLevel.A1;
        }

        var ladder = CefrLevel.AuthoredLevels;
        for (var i = 0; i < ladder.Count - 1; i++)
        {
            if (ladder[i].Rank == current.Rank)
            {
                return ladder[i + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// D10: which surface a step type opens. The three routes are the ticket's bindings; a new step type
    /// must choose one of them, and the default arm throws so a type added without a route fails at the
    /// first call rather than silently opening the selector.
    /// </summary>
    public static StepRoute ExecutionRouteOf(PathStepType stepType) => stepType switch
    {
        PathStepType.DiagnosticDrill or PathStepType.FocusPractice or PathStepType.ReviewSpaced
            => StepRoute.ItemSelector,
        PathStepType.ConceptLesson or PathStepType.ExpandedRemedial
            => StepRoute.LessonReader,
        PathStepType.MockSection
            => StepRoute.AttemptEngine,
        _ => throw new ArgumentOutOfRangeException(nameof(stepType)),
    };

    /// <summary>D10: the step type a route is opened by, for the reverse check. Every route has at
    /// least one type, so no route is unreachable.</summary>
    public static IReadOnlyList<PathStepType> StepTypesFor(StepRoute route)
    {
        var types = new List<PathStepType>();
        foreach (var candidate in Enum.GetValues<PathStepType>())
        {
            if (ExecutionRouteOf(candidate) == route)
            {
                types.Add(candidate);
            }
        }

        return types;
    }
}
