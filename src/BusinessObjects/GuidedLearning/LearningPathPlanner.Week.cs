using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>
/// #234 / PLC-51, PLC-53, PLC-56: the week, built from the arithmetic.
/// <para>
/// This is the replacement for <c>weekBudget / 40</c> clamped to 3–5 steps. The old rule was measured
/// on <c>9e40a79</c>: 10 h/week and 20 h/week both produced five steps, and a learner who declared one
/// hour was given three. Both are the same root cause — the step count was asserted, then the minutes
/// were divided out of it, so the budget could neither grow the week nor shrink it.
/// </para>
/// <para>
/// Here the order is reversed: the minutes are computed, the sittings are packed into them, and the step
/// count is whatever falls out. <see cref="PlannedWeek.TotalMinutes"/> always equals the budget.
/// </para>
/// </summary>
public static partial class LearningPathPlanner
{
    /// <summary>
    /// #234: the plan for one learner, paced by their goal.
    /// </summary>
    /// <param name="pacing">The goal's hours, the remaining study hours and the runway.</param>
    /// <param name="blocks">The skills to plan, weakest first. Empty means the plan has no steps.</param>
    public static IReadOnlyList<PlannedPathStep> Plan(
        WeeklyPacing pacing, IReadOnlyList<SkillPlanBlock> blocks)
    {
        var budget = WeeklyBudgetMinutes(pacing);

        // A block with no drillable topic has no coordinate to name — the service already refuses to
        // build one, and a caller that passes one gets no step rather than a throw from `DefaultTopicId`.
        var plannedBlocks = blocks.Where(b => b.TopicIds.Count > 0).ToList();
        if (budget <= 0 || plannedBlocks.Count == 0)
        {
            return [];
        }

        var planned = new List<PlannedPathStep>();
        for (var week = 1; week <= WindowWeeks; week++)
        {
            var steps = WeekSteps(week, AllocateWeek(week, budget, plannedBlocks), plannedBlocks);
            for (var i = 0; i < steps.Count; i++)
            {
                planned.Add(steps[i] with { WeekSequence = i + 1 });
            }
        }

        return planned;
    }

    /// <summary>
    /// #233: one week on its own, for the far end of a window's slide.
    /// <para>
    /// A slide appends <b>one</b> week and must not regenerate the three it keeps, so it asks for one
    /// week rather than taking a four-week plan and discarding three quarters of it. The arithmetic is
    /// <see cref="Plan"/>'s — <see cref="WeekSteps"/> over <see cref="AllocateWeek"/> — so a synthesised
    /// week and a freshly generated one cannot be sized differently.
    /// </para>
    /// <para>
    /// <paramref name="requeueTopicIds"/> are the topics the closing week left unmastered. They are
    /// moved to the front of each block's topic order, which is the same mechanism
    /// <see cref="FirstWeekTopicOrder"/> uses for a placement hint: a re-queue <b>reorders</b> the topics
    /// the block can actually serve, it never adds a topic the block has no stock for. A re-queued topic
    /// that does appear in the new week is asserted by the caller's test.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PlannedPathStep> PlanWeek(
        int weekNumber,
        WeeklyPacing pacing,
        IReadOnlyList<SkillPlanBlock> blocks,
        IReadOnlyList<int>? requeueTopicIds = null)
    {
        var budget = WeeklyBudgetMinutes(pacing);
        var plannedBlocks = blocks.Where(b => b.TopicIds.Count > 0).ToList();
        if (budget <= 0 || plannedBlocks.Count == 0)
        {
            return [];
        }

        plannedBlocks = RequeueFirst(plannedBlocks, requeueTopicIds ?? []).ToList();

        var steps = WeekSteps(weekNumber, AllocateWeek(weekNumber, budget, plannedBlocks), plannedBlocks);
        var sequenced = steps.Select((s, i) => s with { WeekSequence = i + 1 }).ToList();

        // #233: the re-queued topics LEAD the new week. Reordering the block alone is not enough —
        // `TopicFor` offsets into the block's order, so a topic moved to the front can still fall
        // outside the week's few sittings and never appear. The ticket's trap names exactly this: a
        // topic re-queued but never planned is a row written somewhere and a hole in the week.
        return requeueTopicIds is { Count: > 0 } ? LeadWithRequeued(sequenced, requeueTopicIds, plannedBlocks) : sequenced;
    }

    /// <summary>
    /// #233: the far-end week's drill steps, re-pointed at the closing week's unmastered topics first.
    /// A drill step is the only shape that can be re-pointed — a lesson names its own topic and a mock
    /// names its exam — so only selector-driven steps are touched, and the week's minutes, count and
    /// sequence are left exactly as the allocation produced them.
    /// </summary>
    private static IReadOnlyList<PlannedPathStep> LeadWithRequeued(
        IReadOnlyList<PlannedPathStep> week,
        IReadOnlyList<int> requeueTopicIds,
        IReadOnlyList<SkillPlanBlock> blocks)
    {
        var pending = new Queue<int>(requeueTopicIds);
        var result = new List<PlannedPathStep>(week.Count);

        foreach (var step in week)
        {
            if (pending.Count == 0
                || !LearningPathPlanner.StepTypesFor(StepRoute.ItemSelector).Contains(step.StepType))
            {
                result.Add(step);
                continue;
            }

            var block = blocks.FirstOrDefault(b => b.SkillId == step.SkillId);
            var topic = pending.Peek();

            // A re-queued topic can only be drilled by a block that holds stock at that cell. A block
            // that cannot serve it keeps its planned topic, and the topic stays pending for the next
            // step rather than being dropped into a cell with nothing behind it.
            if (block is null || !block.TopicIds.Contains(topic))
            {
                result.Add(step);
                continue;
            }

            pending.Dequeue();
            result.Add(step with { TopicId = topic });
        }

        return result;
    }

    /// <summary>
    /// #233: the closing week's unmastered topics, moved to the front of each block that can serve
    /// them. A topic absent from every block is dropped, for <see cref="FirstWeekTopicOrder"/>'s
    /// reason: the week must never name a cell with nothing to serve.
    /// </summary>
    private static IEnumerable<SkillPlanBlock> RequeueFirst(
        IReadOnlyList<SkillPlanBlock> blocks, IReadOnlyList<int> requeueTopicIds)
    {
        if (requeueTopicIds.Count == 0)
        {
            return blocks;
        }

        var requeued = requeueTopicIds.ToHashSet();
        return blocks.Select(b =>
        {
            if (!b.TopicIds.Any(requeued.Contains))
            {
                return b;
            }

            // The block's own position is the tie-break, the same rule FirstWeekTopicOrder keeps, so
            // two slides on the same data plan the same week.
            var position = b.TopicIds.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
            return b with
            {
                TopicIds = b.TopicIds.OrderByDescending(requeued.Contains).ThenBy(t => position[t]).ToList(),
            };
        });
    }

    /// <summary>
    /// #234: the plan for a goal that states only a pace. Kept so a caller that has no remaining-hours
    /// figure yet plans the learner's own pace rather than nothing — the one branch that invents no
    /// number, because the learner already supplied it.
    /// </summary>
    public static IReadOnlyList<PlannedPathStep> Plan(
        decimal weeklyStudyHours, IReadOnlyList<SkillPlanBlock> blocks) =>
        Plan(PacingFromGoal(weeklyStudyHours), blocks);

    /// <summary>
    /// PLC-56: which notice this week carries. A goal the bands cannot measure (`CANNOT_JUDGE`) says
    /// nothing, because there is no verdict to report.
    /// </summary>
    private static PathWeekNotice? NoticeFor(WeeklyPacing pacing)
    {
        var required = RequiredWeeklyHours(pacing.RemainingStudyHours, pacing.DaysRemaining);
        if (required is null)
        {
            return null;
        }

        return required > pacing.WeeklyTargetHours
            ? PathWeekNotice.DeficitPacked
            : required < pacing.WeeklyTargetHours
                ? PathWeekNotice.SurplusCapped
                : null;
    }

    /// <summary>
    /// One week's steps, in D10's pedagogical order and alternating skills. The lesson and the mock do
    /// not add minutes — they <b>take</b> one of the sittings the allocation already granted, so the week
    /// still sums to its budget.
    /// </summary>
    private static IReadOnlyList<PlannedPathStep> WeekSteps(
        int week,
        PlannedWeek allocated,
        IReadOnlyList<SkillPlanBlock> blocks)
    {
        if (allocated.Allocations.Count == 0)
        {
            return [];
        }

        var slots = Interleave(allocated).ToList();
        if (slots.Count == 0)
        {
            return [];
        }

        // The lesson is declarative framing before practice, so it is week one's first sitting — and
        // only when the week can afford one sitting per skill. A too-thin week has no room to spend a
        // sitting on a lesson without dropping a skill's guarantee.
        var lessonAt = -1;
        if (week == 1 && allocated.Notice != PathWeekNotice.TooFewSittings
            && blocks.FirstOrDefault(b => b.LessonId is not null) is { } lessonBlock)
        {
            lessonAt = slots.FindIndex(s => s.Block == IndexOf(blocks, lessonBlock.SkillId));
        }

        // 15% mock, at the end of each two-week cycle, and only when a mock exists to sit.
        var mockAt = -1;
        if (week % 2 == 0 && blocks.FirstOrDefault(b => b.MockExamId is not null) is { } mockBlock)
        {
            mockAt = slots.FindLastIndex(s => s.Block == IndexOf(blocks, mockBlock.SkillId));
        }

        var seenPerBlock = new int[blocks.Count];
        var steps = new List<PlannedPathStep>(slots.Count);
        for (var i = 0; i < slots.Count; i++)
        {
            var (blockIndex, minutes) = slots[i];
            var block = blocks[blockIndex];
            var tier = SizingTierFor(block.Mastery);
            var occurrence = seenPerBlock[blockIndex]++;

            if (i == lessonAt)
            {
                steps.Add(new PlannedPathStep(
                    week, 0, PathStepType.ConceptLesson, block.SkillId,
                    block.LessonTopicId ?? FirstTopicOf(block),
                    block.TargetCefr, tier, PlannedItemCount: null, EstimatedMinutes: minutes));
                continue;
            }

            if (i == mockAt)
            {
                steps.Add(new PlannedPathStep(
                    week, 0, PathStepType.MockSection, block.SkillId,
                    FirstTopicOf(block), block.TargetCefr, SizingTier.Developing,
                    PlannedItemCount: null, EstimatedMinutes: minutes));
                continue;
            }

            // Blocked practice before interleaved review: the first pass through a topic is a focus
            // drill, and only a later week reviews it spaced.
            var type = week == 1
                ? PathStepType.FocusPractice
                : i % 2 == 0 ? PathStepType.FocusPractice : PathStepType.ReviewSpaced;

            steps.Add(new PlannedPathStep(
                week, 0, type, block.SkillId,
                TopicFor(block, week, occurrence),
                block.TargetCefr, tier,
                PlannedItemCount: ItemCountFor(tier, availableInCell: null),
                EstimatedMinutes: minutes));
        }

        return steps;
    }

    /// <summary>The week's sittings, skills alternating. Two sittings on one skill never sit next to
    /// each other while another skill still has minutes to spend.</summary>
    private static IEnumerable<(int Block, int Minutes)> Interleave(PlannedWeek allocated)
    {
        var queues = allocated.Allocations
            .Select(a => new Queue<int>(a.StepMinutes))
            .ToArray();

        var remaining = queues.Count(q => q.Count > 0);
        while (remaining > 0)
        {
            for (var i = 0; i < queues.Length; i++)
            {
                if (queues[i].Count == 0)
                {
                    continue;
                }

                yield return (i, queues[i].Dequeue());
                if (queues[i].Count == 0)
                {
                    remaining--;
                }
            }
        }
    }

    private static int IndexOf(IReadOnlyList<SkillPlanBlock> blocks, int skillId)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].SkillId == skillId)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FirstTopicOf(SkillPlanBlock block) =>
        block.TopicIds.Count > 0 ? FirstWeekTopicOrder(block)[0] : DefaultTopicId(block);

    /// <summary>
    /// #219 / #234: which topic a skill's <paramref name="occurrence"/>-th drill of
    /// <paramref name="week"/> names.
    /// <para>
    /// Week one walks the hint-first order (<see cref="FirstWeekTopicOrder"/>), so a placement's weak
    /// topic is drilled first. Every later week walks the block's own order, offset by the week, so a
    /// plan does not drill one cell four weeks running.
    /// </para>
    /// </summary>
    private static int TopicFor(SkillPlanBlock block, int week, int occurrence)
    {
        var order = week == 1 ? FirstWeekTopicOrder(block) : block.TopicIds;
        var offset = week == 1 ? 0 : week - 1;
        return order[(offset + occurrence) % order.Count];
    }
}
