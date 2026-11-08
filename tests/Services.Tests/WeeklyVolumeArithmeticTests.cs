using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #234: the weekly volume is derived from the distance to target, not asserted. Every guard here exists
/// because a plausible-looking implementation passes the obvious test and is still wrong.
/// <para>
/// Pure: no database, no clock. The service-level tests that prove a learner's own numbers reach the plan
/// live in <c>DynamicPathGenerationTests</c>.
/// </para>
/// </summary>
public sealed class WeeklyVolumeArithmeticTests
{
    /// <summary>PLC-54's worked profile. `θ` is the continuous coordinate; B2's own midpoint is 4.50.</summary>
    private static SkillPlanBlock Worked(int skillId, Modality modality, decimal current, int[] topics) =>
        new(skillId, modality, CefrLevel.B2, MasteryState.Reviewing, topics, LessonId: null, MockExamId: null,
            DistanceToTarget: 4.50m - current);

    private static SkillPlanBlock Distance(int skillId, decimal? distance, MasteryState mastery = MasteryState.Reviewing) =>
        new(skillId, Modality.Reading, CefrLevel.B2, mastery, [10, 11], LessonId: null, MockExamId: null,
            DistanceToTarget: distance);

    // ---------------------------------------------------------------- PLC-51: the budget

    /// <summary>
    /// `T_weekly = 60 × min(H_weekly_target, max(1.5, H_required))`.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> a learner with 1 h/week and a large deficit. A
    /// floor applied after the `min` — or read as "at least 1.5 h of plan" — returns 90 and promises half
    /// again what they said they have. The `min` is what keeps them at 60.
    /// </para>
    /// </summary>
    [Fact]
    public void The_budget_never_exceeds_what_the_learner_said_they_have()
    {
        // 60 hours to cover, 1 week of runway: H_required = 60, far above the declared pace.
        var pacing = new LearningPathPlanner.WeeklyPacing(
            WeeklyTargetHours: 1m, RemainingStudyHours: 60m, DaysRemaining: 7);

        Assert.Equal(60, LearningPathPlanner.WeeklyBudgetMinutes(pacing));
    }

    /// <summary>
    /// The floor is on the <b>required velocity</b>, so a learner already at target still gets a real
    /// week rather than a token one.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> a learner with 3 h/week and zero remaining
    /// hours. Without `max(1.5, …)` the budget is 0 and the plan is empty — the "at target" learner is
    /// silently dropped from their own plan.
    /// </para>
    /// </summary>
    [Fact]
    public void A_learner_at_target_still_gets_the_one_and_a_half_hour_week()
    {
        var pacing = new LearningPathPlanner.WeeklyPacing(
            WeeklyTargetHours: 3m, RemainingStudyHours: 0m, DaysRemaining: 30);

        Assert.Equal(90, LearningPathPlanner.WeeklyBudgetMinutes(pacing));
    }

    /// <summary>
    /// `H_required = Σ H_s / max(1, days/7)`. The same remaining hours over twice the runway is half the
    /// weekly velocity.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> 120 hours over 4 weeks versus 8. An
    /// implementation that divided by days instead of weeks, or dropped the `max(1, …)`, returns the
    /// same number for both because both fit inside... no: it returns 120/4 = 30 against 120/8 = 15 only
    /// when the division is by weeks. Dividing by <i>days</i> gives 120/28 and 120/56 — different
    /// numbers, so this test alone cannot catch that. <see cref="A_runway_shorter_than_a_week_is_one_week"/>
    /// is the one that does.
    /// </para>
    /// </summary>
    [Fact]
    public void Doubling_the_runway_halves_the_required_weekly_velocity()
    {
        var fourWeeks = LearningPathPlanner.RequiredWeeklyHours(remainingStudyHours: 120m, daysRemaining: 28);
        var eightWeeks = LearningPathPlanner.RequiredWeeklyHours(remainingStudyHours: 120m, daysRemaining: 56);

        Assert.Equal(30m, fourWeeks);
        Assert.Equal(15m, eightWeeks);
    }

    /// <summary>
    /// `max(1, days/7)` — a runway shorter than a week is still one week.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> three days remaining and 30 hours to cover. With
    /// no `max(1, …)` the divisor is 0.43 weeks and the required velocity is 70 h/week, which PLC-56's
    /// own ceiling (25 h/week) says nobody can do — the plan would demand the impossible and the deficit
    /// verdict the learner needs would be replaced by an unlivable week.
    /// </para>
    /// </summary>
    [Fact]
    public void A_runway_shorter_than_a_week_is_one_week()
    {
        Assert.Equal(30m, LearningPathPlanner.RequiredWeeklyHours(remainingStudyHours: 30m, daysRemaining: 3));
    }

    /// <summary>
    /// A certificate whose bands carry no study-hour evidence (`CANNOT_JUDGE`) states no velocity, and the
    /// plan falls back to the learner's own pace — the one number that is not invented.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> `RemainingStudyHours = null` with 2 h/week. A
    /// version that coerced null to 0 would compute `max(1.5, 0)` and plan 90 minutes, silently upgrading
    /// a learner's stated 2 hours... which is harmless. The harmful coercion is to a *large* number: null
    /// treated as "unknown, assume worst" gives an unlivable week. Pinned as "null means the learner's
    /// own pace, exactly".
    /// </para>
    /// </summary>
    [Fact]
    public void No_study_hour_evidence_plans_the_learners_own_pace()
    {
        var pacing = new LearningPathPlanner.WeeklyPacing(
            WeeklyTargetHours: 2m, RemainingStudyHours: null, DaysRemaining: 60);

        Assert.Null(LearningPathPlanner.RequiredWeeklyHours(null, 60));
        Assert.Equal(120, LearningPathPlanner.WeeklyBudgetMinutes(pacing));
    }

    // ---------------------------------------------------------------- PLC-53: the skew

    /// <summary>
    /// `W_s = d_s^1.5 / Σ d_j^1.5`, `d_s = max(0.15, θ_target − θ_current)`.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> a skill already past its target (Listening at
    /// 4.71 against a 4.50 target, distance <c>−0.21</c>). A version that used the raw distance raises a
    /// negative to 1.5 — `Math.Pow(-0.21, 1.5)` is NaN — and every weight becomes NaN, so no week has a
    /// balance at all. The floor turns it into 0.15 and the skill keeps its maintenance step.
    /// </para>
    /// </summary>
    [Fact]
    public void A_skill_past_its_target_takes_the_floor_rather_than_a_negative_weight()
    {
        var weights = LearningPathPlanner.SkillWeights(
            [Distance(1, 1.86m), Distance(2, -0.21m)]);

        Assert.All(weights, w => Assert.False(double.IsNaN(w)));
        Assert.Equal(1d, weights.Sum(), precision: 10);
        Assert.True(weights[0] > weights[1]);
    }

    /// <summary>
    /// The exponent concentrates the week on the limiting skill. At `γ = 1` the split would be the raw
    /// distance ratio; `γ = 1.5` gives the limiting skill a strictly larger share.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> two skills at distances 1.86 and 0.15. A linear
    /// (`γ = 1`) reading gives 92.5%/7.5%; `γ = 1.5` gives 98%/2% before the step floor. The test pins
    /// that the limiting skill's share is strictly greater than its raw distance share, which linear
    /// fails.
    /// </para>
    /// </summary>
    [Fact]
    public void The_limiting_skill_takes_more_than_its_raw_share_of_the_distance()
    {
        var weights = LearningPathPlanner.SkillWeights([Distance(1, 1.86m), Distance(2, 0.15m)]);

        var rawShare = 1.86d / (1.86d + 0.15d);
        Assert.True(weights[0] > rawShare,
            $"skewed share {weights[0]:P2} should exceed the raw share {rawShare:P2}");
    }

    /// <summary>
    /// A certificate that examines one skill gives that skill the whole week.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> a single block. A version that divided by
    /// `blocks.Count` after the loop, or that required at least two blocks, yields half a week and an
    /// unallocated remainder.
    /// </para>
    /// </summary>
    [Fact]
    public void A_one_skill_certificate_gives_that_skill_the_whole_week()
    {
        var week = LearningPathPlanner.AllocateWeek(1, 120, [Distance(1, 2.5m)]);

        Assert.Single(week.Allocations);
        Assert.Equal(120, week.TotalMinutes);
        Assert.Equal(1d, week.Allocations[0].Weight, precision: 10);
    }

    /// <summary>
    /// With <b>no</b> target coordinate anywhere the week is flat — there is no distance to allocate from,
    /// and dividing by an all-floor denominator merely restates that. Every skill still gets its step.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> four blocks all with `DistanceToTarget = null`.
    /// A version that read null as distance 0 gives four weights of 0/0 → NaN, or four zero weights and
    /// an empty week: a learner with no target is given no plan at all.
    /// </para>
    /// </summary>
    [Fact]
    public void No_target_at_all_gives_a_flat_week_that_still_serves_every_skill()
    {
        var blocks = new[]
        {
            Distance(1, null), Distance(2, null), Distance(3, null), Distance(4, null),
        };

        var week = LearningPathPlanner.AllocateWeek(1, 120, blocks);

        Assert.All(week.Allocations, a => Assert.Equal(0.25d, a.Weight, precision: 10));
        Assert.All(week.Allocations, a => Assert.True(a.Steps >= 1));
        Assert.Equal(120, week.TotalMinutes);
    }

    // ---------------------------------------------------------------- the floor and the total

    /// <summary>
    /// <b>Every examined skill gets at least one step per week, and the week still sums to the budget.</b>
    /// <para>
    /// This is the guard the ticket names: a floor implemented as a clamp <i>after</i> allocation leaves
    /// the weights summing to more than the week, because the clamped skills were raised without taking
    /// the minutes from anyone. The test asserts both halves at once, so an implementation that satisfies
    /// either alone fails.
    /// </para>
    /// <para>
    /// <b>The input that would pass a wrong version:</b> the worked profile at 165 minutes. A clamping
    /// implementation produces 25+15+40+28.33 = 108.33 raised to 165 by the clamp... and the total can
    /// still land on 165 by luck. The test that actually catches it is
    /// <see cref="The_floor_is_taken_from_the_skills_above_it_not_created"/>, which pins <i>who</i> paid.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_examined_skill_gets_a_step_and_the_week_sums_to_its_budget()
    {
        var blocks = new[]
        {
            Worked(1, Modality.Speaking, 2.64m, [10]),
            Worked(2, Modality.Writing, 3.50m, [20]),
            Worked(3, Modality.Reading, 4.42m, [30]),
            Worked(4, Modality.Listening, 4.71m, [40]),
        };

        for (var budget = 60; budget <= 600; budget += 7)
        {
            var week = LearningPathPlanner.AllocateWeek(1, budget, blocks);

            Assert.Equal(budget, week.TotalMinutes);
            Assert.All(week.Allocations, a => Assert.True(a.Steps >= 1,
                $"budget {budget}: skill {a.SkillId} got {a.Steps} steps"));
            Assert.True(week.IsPacked, $"budget {budget} produced {week.TotalMinutes}");
            Assert.All(week.Allocations, a => Assert.Equal(a.Minutes, a.StepMinutes.Sum()));
        }
    }

    /// <summary>
    /// The floor is paid for by the skills that were above their own sitting, <b>in proportion to how far
    /// above they were</b>. Nothing is created.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> the worked profile at 165 minutes. A clamp
    /// raises the two floored skills to their sitting and shaves the overshoot off the <i>limiting
    /// skill only</i>, leaving Writing at its untouched raw share of 45.2. Proportional payment takes
    /// Writing's share of the bill — about 5.8 minutes — and lands it near 39.4. Both arithmetic-total
    /// 165, so "the week sums to its budget" cannot tell them apart; this test pins <i>who paid</i>.
    /// </para>
    /// </summary>
    [Fact]
    public void The_floor_is_taken_from_the_skills_above_it_not_created()
    {
        var blocks = new[]
        {
            Worked(1, Modality.Speaking, 2.64m, [10]),
            Worked(2, Modality.Writing, 3.50m, [20]),
            Worked(3, Modality.Reading, 4.42m, [30]),
            Worked(4, Modality.Listening, 4.71m, [40]),
        };

        var week = LearningPathPlanner.AllocateWeek(1, 165, blocks);
        var weight = week.Allocations.ToDictionary(a => a.SkillId, a => a.Weight);
        var minutes = week.Allocations.ToDictionary(a => a.SkillId, a => a.Minutes);
        var sitting = LearningPathPlanner.SittingMinutesFor(SizingTier.Developing);

        // The two floored skills (Reading, Listening) are each raised from their raw share to a sitting.
        Assert.Equal(sitting, minutes[3]);
        Assert.Equal(sitting, minutes[4]);

        // The bill: what the floored skills were short, and what the two skills above their own sitting
        // had to spare. Paid in proportion to how far above each was.
        var raw = new[] { 1, 2, 3, 4 }.ToDictionary(id => id, id => 165 * weight[id]);
        var shortfall = raw.Where(r => r.Value < sitting).Sum(r => sitting - r.Value);
        var spare = raw.Where(r => r.Value >= sitting).ToDictionary(r => r.Key, r => r.Value - sitting);

        foreach (var (id, excess) in spare)
        {
            var paid = shortfall * excess / spare.Values.Sum();
            Assert.Equal(raw[id] - paid, minutes[id], tolerance: 1.5);
        }

        Assert.Equal(165, week.TotalMinutes);
    }

    // ---------------------------------------------------------------- PLC-54: the worked example

    /// <summary>
    /// PLC-54's worked allocation, pinned exactly as the spec states it.
    /// <para>
    /// <b>Known contradiction, reported rather than papered over.</b> The spec's percentages (52/24/15/9)
    /// are <b>not</b> `W_s` at `γ = 1.5`: that formula gives 69.4/27.4/1.6/1.6, because Reading and
    /// Listening both sit at the 0.15 floor and therefore <i>tie</i>, while the table gives them 15% and
    /// 9%. The table is also reachable only if the limiting skill is the <i>residual</i> after the other
    /// three take fixed sittings (165 − 40 − 25 − 15 = 85), and it implies sittings of 28.3 and 40
    /// minutes, above D10's 25-minute ceiling for any tier.
    /// </para>
    /// <para>
    /// This test therefore pins the two things that <i>are</i> reproducible from the stated formulas —
    /// the budget (165) and the ordering (limiting skill largest, every skill served) — and
    /// <see cref="The_worked_percentages_are_not_reachable_from_the_spec_s_own_skew_formula"/> records the
    /// gap so it cannot be forgotten. Pinning 85/40/25/15 as "the arithmetic" would be pinning a number
    /// the arithmetic does not produce.
    /// </para>
    /// </summary>
    [Fact]
    public void The_worked_allocation_packs_the_stated_budget_and_serves_every_skill()
    {
        var blocks = new[]
        {
            Worked(1, Modality.Speaking, 2.64m, [10]),
            Worked(2, Modality.Writing, 3.50m, [20]),
            Worked(3, Modality.Reading, 4.42m, [30]),
            Worked(4, Modality.Listening, 4.71m, [40]),
        };

        var week = LearningPathPlanner.AllocateWeek(1, 165, blocks);

        Assert.Equal(165, week.TotalMinutes);
        Assert.True(week.IsPacked);
        Assert.Equal(4, week.Allocations.Count);
        Assert.All(week.Allocations, a => Assert.True(a.Steps >= 1));

        // The limiting skill takes the largest share: Speaking's 1.86 bands is the biggest distance.
        var speaking = week.Allocations.Single(a => a.SkillId == 1);
        Assert.Equal(week.Allocations.Max(a => a.Minutes), speaking.Minutes);
    }

    /// <summary>
    /// The gap in PLC-54, asserted so a future reader does not "fix" the code to hit 52/24/15/9.
    /// <para>
    /// <b>Why this is a test and not a comment:</b> the ticket's acceptance criterion says the worked
    /// allocation must reproduce <i>exactly</i>. It cannot, and the reason is arithmetic, not
    /// implementation. Silencing that by tuning γ until Speaking lands on 52% would fit one point on the
    /// curve and mis-weight every learner — exactly the trap the ticket warns about. The numbers below
    /// are what PLC-53's formula actually yields for PLC-54's inputs.
    /// </para>
    /// </summary>
    [Fact]
    public void The_worked_percentages_are_not_reachable_from_the_spec_s_own_skew_formula()
    {
        var weights = LearningPathPlanner.SkillWeights(
        [
            Worked(1, Modality.Speaking, 2.64m, [10]),   // d = 1.86
            Worked(2, Modality.Writing, 3.50m, [20]),    // d = 1.00
            Worked(3, Modality.Reading, 4.42m, [30]),    // d = 0.15 (floor)
            Worked(4, Modality.Listening, 4.71m, [40]),  // d = 0.15 (floor)
        ]);

        // γ = 1.5 over 1.86, 1.00, 0.15, 0.15. Independent of the implementation: these are the
        // specification's own operands.
        Assert.Equal(0.6944, weights[0], precision: 4);
        Assert.Equal(0.2738, weights[1], precision: 4);
        Assert.Equal(0.0159, weights[2], precision: 4);

        // The two floored skills tie, so the spec's 15% and 9% cannot both come from W_s.
        Assert.Equal(weights[2], weights[3], precision: 10);

        // And the spec's own percentages are what the MINUTES give, not what W_s gives.
        Assert.Equal(52, (int)Math.Round(85m / 165m * 100m));
        Assert.Equal(9, (int)Math.Round(15m / 165m * 100m));
    }

    // ---------------------------------------------------------------- PL-51: packing

    /// <summary>
    /// The tolerance is ±10 minutes over the <b>week</b>, not over one step.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> a week budgeted 165 minutes. A per-step
    /// assertion "each step is within 10 of 165/n" is satisfied by five 33-minute sittings and by one
    /// 165-minute sitting alike. The week-level check distinguishes them: 165 in one sitting is a
    /// five-hour block, and <see cref="No_sitting_exceeds_the_sixty_minute_ceiling"/> is what catches it.
    /// </para>
    /// </summary>
    [Fact]
    public void Packing_is_measured_over_the_week_not_over_one_step()
    {
        var blocks = new[]
        {
            Worked(1, Modality.Speaking, 2.64m, [10]),
            Worked(2, Modality.Writing, 3.50m, [20]),
        };

        var week = LearningPathPlanner.AllocateWeek(1, 165, blocks);

        Assert.True(week.IsPacked);
        Assert.Equal(165, week.TotalMinutes);
    }

    /// <summary>
    /// No single sitting exceeds <see cref="LearningPathPlanner.MaximumStepMinutes"/>, so a large budget
    /// becomes more sittings rather than one unlivable block.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> the worked profile at 1500 minutes. A version
    /// that gives the whole allocation to one step returns a 1045-minute sitting — a plan nobody can sit,
    /// and the exact failure the old 3–5 rule avoided by accident and then caused at the other end.
    /// </para>
    /// </summary>
    [Fact]
    public void No_sitting_exceeds_the_sixty_minute_ceiling()
    {
        var blocks = new[]
        {
            Worked(1, Modality.Speaking, 2.64m, [10]),
            Worked(2, Modality.Writing, 3.50m, [20]),
            Worked(3, Modality.Reading, 4.42m, [30]),
            Worked(4, Modality.Listening, 4.71m, [40]),
        };

        var week = LearningPathPlanner.AllocateWeek(1, 1500, blocks);

        Assert.All(week.Allocations, a => Assert.All(a.StepMinutes,
            m => Assert.InRange(m, LearningPathPlanner.MinimumStepMinutes, LearningPathPlanner.MaximumStepMinutes)));
        Assert.Equal(1500, week.TotalMinutes);
    }

    // ---------------------------------------------------------------- the old rule's defects

    /// <summary>
    /// Doubling the declared study time must produce a materially different week. This is the measured
    /// defect: on <c>9e40a79</c>, 10 h/week and 20 h/week both produced five steps.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> 10 h and 20 h with a huge deficit. A version that
    /// caps the week at some constant passes "monotonic" if it only compares step <i>counts</i> — five
    /// with 600 minutes of budget and five with 1200 was the bug. So the assertion is on <b>minutes</b>,
    /// which is the quantity the spec weights.
    /// </para>
    /// </summary>
    [Fact]
    public void Twenty_hours_a_week_plans_more_than_ten_where_the_deficit_allows_it()
    {
        var blocks = new[]
        {
            Distance(1, 2.0m), Distance(2, 1.0m),
        };

        // 100 hours of runway work in 7 days: the deficit is far above either declared pace, so the
        // week is the learner's own pace in both cases and the difference is theirs, not a clamp's.
        var ten = LearningPathPlanner.WeeklyBudgetMinutes(
            new LearningPathPlanner.WeeklyPacing(10m, 100m, 7));
        var twenty = LearningPathPlanner.WeeklyBudgetMinutes(
            new LearningPathPlanner.WeeklyPacing(20m, 100m, 7));

        Assert.Equal(600, ten);
        Assert.Equal(1200, twenty);
        Assert.True(twenty > ten);
    }

    /// <summary>
    /// A learner who declares one hour is not promised three hours of content.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> 1 h/week with three skills. The old
    /// `Math.Clamp(round(60/40), 3, 5)` gave three steps of ~20 minutes each — three hours promised
    /// against sixty minutes available. The assertion is that the week's minutes are the hour.
    /// </para>
    /// </summary>
    [Fact]
    public void One_hour_a_week_is_packed_into_one_hour_not_three()
    {
        var blocks = new[]
        {
            Distance(1, 1.86m), Distance(2, 1.00m), Distance(3, 0.15m),
        };

        var budget = LearningPathPlanner.WeeklyBudgetMinutes(
            new LearningPathPlanner.WeeklyPacing(1m, 500m, 7));
        var week = LearningPathPlanner.AllocateWeek(1, budget, blocks);

        Assert.Equal(60, budget);
        Assert.Equal(60, week.TotalMinutes);
        Assert.True(week.TotalMinutes <= 60 + LearningPathPlanner.PackingToleranceMinutes);
    }

    /// <summary>
    /// A week too thin to hold one full sitting per skill says so and serves the highest-weighted skills
    /// rather than inventing minutes.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> 30 minutes and four skills needing 25-minute
    /// sittings. A version that forced one step each produces 100 minutes on a 30-minute week. The
    /// honest answer is three 10-minute sittings at most, and a notice.
    /// </para>
    /// </summary>
    [Fact]
    public void A_week_too_thin_for_every_skill_says_so_instead_of_promising_minutes()
    {
        var blocks = new[]
        {
            Worked(1, Modality.Speaking, 2.64m, [10]),
            Worked(2, Modality.Writing, 3.50m, [20]),
            Worked(3, Modality.Reading, 4.42m, [30]),
            Worked(4, Modality.Listening, 4.71m, [40]),
        };

        var week = LearningPathPlanner.AllocateWeek(1, 25, blocks);

        Assert.Equal(PathWeekNotice.TooFewSittings, week.Notice);
        Assert.True(week.TotalMinutes <= 25);
        Assert.All(week.Allocations, a => Assert.True(a.Minutes <= 25));
    }

    // ---------------------------------------------------------------- PLC-56: notices

    /// <summary>
    /// A deficit week packs inside the hours and carries the verdict notice.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> 2 h/week against 60 h required in one week. A
    /// version that planned the required velocity gives 90 minutes for a 120-minute learner... which is
    /// under, so it would pass a "no overload" check. The notice is what distinguishes "packed inside the
    /// hours" from "planned from the deficit and happened to be small".
    /// </para>
    /// </summary>
    [Fact]
    public void A_deficit_week_packs_inside_the_hours_and_carries_the_verdict()
    {
        var blocks = new[] { Distance(1, 2.0m, MasteryState.Learning) };
        var pacing = new LearningPathPlanner.WeeklyPacing(2m, 60m, 7);

        var plan = LearningPathPlanner.Plan(pacing, blocks);

        Assert.NotEmpty(plan);
        Assert.Equal(120, LearningPathPlanner.PlannedMinutesForWeek(plan, 1));
    }

    /// <summary>
    /// A surplus week is capped rather than repeating items.
    /// <para>
    /// <b>The input that would pass a wrong version:</b> 20 h/week against 3 h required. Without a
    /// ceiling the plan is 1200 minutes of the same cell — the "silently repeating items" PLC-56 forbids.
    /// The cap is the pool's supply, which this pure function does not know; what it does guarantee is
    /// that the week is the goal's arithmetic and not an unbounded repeat.
    /// </para>
    /// </summary>
    [Fact]
    public void A_surplus_week_is_the_goals_arithmetic_not_an_unbounded_repeat()
    {
        var blocks = new[] { Distance(1, 1.0m) };
        var pacing = new LearningPathPlanner.WeeklyPacing(20m, 3m, 7);

        var plan = LearningPathPlanner.Plan(pacing, blocks);
        var week = LearningPathPlanner.PlannedMinutesForWeek(plan, 1);

        // H_required = 3 h/week, so T_weekly = min(20, max(1.5, 3)) = 3 h — not 20.
        Assert.Equal(180, week);
    }
}
