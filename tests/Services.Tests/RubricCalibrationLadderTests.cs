using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #193: the seven-response ladder (D0.6 mechanism 5, D17 gate 4). Six benchmark responses at bands
/// 1-6 must produce strictly rising composites, each adjacent pair at least 0.65 apart, and the
/// adversarial 280-word verbose trap must not climb above 3.25.
/// <para>
/// No database and no model: the ladder's job is arithmetic and diagnosis over what a grader returned,
/// so the grader is scripted here and every verdict is stated in the test, not recomputed.
/// </para>
/// </summary>
public sealed class RubricCalibrationLadderTests
{
    private static readonly string[] Codes = ["W_TR", "W_CC", "W_LR", "W_GR"];

    private static IReadOnlyList<GradingCriterionDetail> Criteria() =>
        Codes.Select((code, i) => new GradingCriterionDetail(
            i + 1, code, code, MaxScore: 6m, Weight: 1m, RubricDescription: $"Prose for {code}.")).ToList();

    private static string Benchmark(int band) => $"benchmark band {band}";

    private const string Trap = "verbose b1 trap, 280 words of repetitive simple clauses";

    /// <summary>Six benchmarks at bands 1-6 plus the trap, all seven named the way the tests script them.</summary>
    private static IReadOnlyList<RubricLadderResponse> Responses() =>
        [.. Enumerable.Range(1, 6).Select(b => new RubricLadderResponse((byte)b, Benchmark(b))),
         new RubricLadderResponse(3, Trap, IsVerboseTrap: true)];

    /// <summary>Every criterion on a band, so the composite of four criteria is that band.</summary>
    private static Func<GradingCriterionDetail, decimal> Flat(decimal band) => _ => band;

    private static async Task<RubricCalibrationResult> RunAsync(
        IReadOnlyDictionary<string, Func<GradingCriterionDetail, decimal>> byText,
        IReadOnlyList<RubricLadderResponse>? responses = null) =>
        await RubricCalibrationLadder.RunAsync(
            Criteria(), new ScriptedGrader(byText), responses ?? Responses());

    [Fact]
    public async Task Six_benchmarks_on_their_bands_and_a_low_trap_plot_the_ladder_and_are_accepted()
    {
        // The accept direction: 1..6 exactly on band, a one-band separation, and a trap that stays
        // below the ceiling. Without this a ladder that refuses everything would look like a guard.
        var byText = new Dictionary<string, Func<GradingCriterionDetail, decimal>>
        {
            [Benchmark(1)] = Flat(1m),
            [Benchmark(2)] = Flat(2m),
            [Benchmark(3)] = Flat(3m),
            [Benchmark(4)] = Flat(4m),
            [Benchmark(5)] = Flat(5m),
            [Benchmark(6)] = Flat(6m),
            [Trap] = Flat(3m),
        };

        var result = await RunAsync(byText);

        Assert.True(result.Accepted, result.Refusal);
        Assert.Equal(RubricCalibrationOutcome.Accepted, result.Outcome);
        // The ladder plots one composite per benchmark band, in band order, plus the trap.
        Assert.Equal([1, 2, 3, 4, 5, 6], result.Ladder.Where(p => !p.IsVerboseTrap).Select(p => p.Band));
        Assert.Equal([1m, 2m, 3m, 4m, 5m, 6m], result.Ladder.Where(p => !p.IsVerboseTrap).Select(p => p.Composite));
        Assert.Equal(3m, Assert.Single(result.Ladder, p => p.IsVerboseTrap).Composite);
        Assert.Empty(result.ResponsibleCriteria);
    }

    [Fact]
    public async Task A_segment_that_does_not_rise_is_refused_as_non_monotonic_and_not_as_separation()
    {
        // The mutation first: composites 1,2,3,3,5,6 rise from first to last, so a lazy "last > first"
        // guard passes it while band 4 measures exactly what band 3 did. The refusal is its own outcome
        // so a flat segment is never reported as a mere threshold miss.
        var byText = new Dictionary<string, Func<GradingCriterionDetail, decimal>>
        {
            [Benchmark(1)] = Flat(1m),
            [Benchmark(2)] = Flat(2m),
            [Benchmark(3)] = Flat(3m),
            [Benchmark(4)] = Flat(3m),
            [Benchmark(5)] = Flat(5m),
            [Benchmark(6)] = Flat(6m),
            [Trap] = Flat(2m),
        };

        var result = await RunAsync(byText);

        Assert.False(result.Accepted);
        Assert.Equal(RubricCalibrationOutcome.NotStrictlyMonotonic, result.Outcome);
        Assert.Contains("band 3", result.Refusal, StringComparison.Ordinal);
        Assert.Contains("band 4", result.Refusal, StringComparison.Ordinal);
        // All four criteria stayed put across that segment, so all four are named, in rubric order.
        Assert.Equal(Codes, result.ResponsibleCriteria);
        // The plot is still returned: an author needs to see where it flattened.
        Assert.Equal([1m, 2m, 3m, 3m, 5m, 6m], result.Ladder.Where(p => !p.IsVerboseTrap).Select(p => p.Composite));
    }

    [Fact]
    public async Task A_rising_segment_short_of_the_threshold_is_refused_and_names_the_criterion_holding_it_down()
    {
        // The mutation first: every segment rises, so "is it monotonic" says yes - and 0.55 is below
        // 0.65 without being zero, so a guard that only catches a flat segment says yes too. Three
        // criteria move 0.70 across band 2 -> band 3 and W_GR moves 0.10, so the composites read
        // 1.00, 1.70, 2.25, 2.95, 3.65, 4.35: monotonic, one segment at 0.55.
        var byText = new Dictionary<string, Func<GradingCriterionDetail, decimal>>
        {
            [Benchmark(1)] = _ => 1.00m,
            [Benchmark(2)] = _ => 1.70m,
            [Benchmark(3)] = c => c.Code == "W_GR" ? 1.80m : 2.40m,
            [Benchmark(4)] = c => c.Code == "W_GR" ? 2.50m : 3.10m,
            [Benchmark(5)] = c => c.Code == "W_GR" ? 3.20m : 3.80m,
            [Benchmark(6)] = c => c.Code == "W_GR" ? 3.90m : 4.50m,
            [Trap] = Flat(2m),
        };

        var result = await RunAsync(byText);

        Assert.False(result.Accepted);
        Assert.Equal(RubricCalibrationOutcome.SeparationBelowThreshold, result.Outcome);
        Assert.Contains("0.65", result.Refusal, StringComparison.Ordinal);
        Assert.Contains("W_GR", result.Refusal, StringComparison.Ordinal);
        Assert.Equal(["W_GR"], result.ResponsibleCriteria);
    }

    [Fact]
    public async Task A_separation_of_exactly_the_threshold_and_a_trap_exactly_at_its_ceiling_are_accepted()
    {
        // Both comparisons are "at least", not "more than". A guard written with > would refuse a
        // rubric that meets the stated numbers exactly, which is stricter than the spec and would push
        // authors to over-separate a rubric to get past an off-by-one.
        var byText = new Dictionary<string, Func<GradingCriterionDetail, decimal>>
        {
            [Benchmark(1)] = _ => 1.00m,
            [Benchmark(2)] = _ => 1.65m,
            [Benchmark(3)] = _ => 2.30m,
            [Benchmark(4)] = _ => 2.95m,
            [Benchmark(5)] = _ => 3.60m,
            [Benchmark(6)] = _ => 4.25m,
            [Trap] = Flat(3.25m),
        };

        var result = await RunAsync(byText);

        Assert.True(result.Accepted, result.Refusal);
    }

    [Fact]
    public async Task A_verbose_trap_above_its_ceiling_is_refused_though_the_ladder_rises()
    {
        // The mutation first: the trap scores 3.60, which is below band 6 (6.00) and below band 4
        // (4.00) - so "is the trap below the top of the ladder" and "is the trap below band 4" both
        // pass a verbosity-rewarding rubric. Only the 3.25 ceiling catches it.
        var byText = new Dictionary<string, Func<GradingCriterionDetail, decimal>>
        {
            [Benchmark(1)] = Flat(1m),
            [Benchmark(2)] = Flat(2m),
            [Benchmark(3)] = Flat(3m),
            [Benchmark(4)] = Flat(4m),
            [Benchmark(5)] = Flat(5m),
            [Benchmark(6)] = Flat(6m),
            [Trap] = Flat(3.6m),
        };

        var result = await RunAsync(byText);

        Assert.False(result.Accepted);
        Assert.Equal(RubricCalibrationOutcome.VerboseTrapAboveCeiling, result.Outcome);
        Assert.Contains("3.25", result.Refusal, StringComparison.Ordinal);
        Assert.Contains("3.60", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_ladder_missing_a_benchmark_band_is_refused_rather_than_scored_over_five()
    {
        // The input that would pass a loop: five benchmarks. Every pair it has separates, so a guard
        // that only compares adjacent pairs accepts a ladder that never measured A1..C2 as a set.
        var partial = Responses().Where(r => r.Band != 6 || r.IsVerboseTrap).ToList();
        var byText = new Dictionary<string, Func<GradingCriterionDetail, decimal>>
        {
            [Benchmark(1)] = Flat(1m),
            [Benchmark(2)] = Flat(2m),
            [Benchmark(3)] = Flat(3m),
            [Benchmark(4)] = Flat(4m),
            [Benchmark(5)] = Flat(5m),
            [Trap] = Flat(2m),
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => RunAsync(byText, partial));

        Assert.Contains("band 6", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_responses_at_all_is_refused_rather_than_accepted_vacuously()
    {
        // Zero adjacent pairs and no trap means every check passes; a ladder over nothing would report
        // a calibrated rubric. The exception is the guard.
        var error = await Assert.ThrowsAsync<ArgumentException>(() => RunAsync(
            new Dictionary<string, Func<GradingCriterionDetail, decimal>>(), []));

        Assert.Contains("benchmark response for band 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_grading_that_failed_names_the_band_instead_of_plotting_a_zero()
    {
        // A failed call has no scores, so its composite would be 0.00 and the ladder would look like a
        // rubric that collapses at that band - the wrong diagnosis, pointing at the rubric instead of
        // at the grader.
        var byText = new Dictionary<string, Func<GradingCriterionDetail, decimal>>
        {
            [Benchmark(1)] = Flat(1m),
            [Benchmark(2)] = Flat(2m),
            [Benchmark(3)] = Flat(3m),
            [Benchmark(4)] = _ => throw new InvalidOperationException(),
            [Benchmark(5)] = Flat(5m),
            [Benchmark(6)] = Flat(6m),
            [Trap] = Flat(2m),
        };
        var responses = Responses();

        var result = await RubricCalibrationLadder.RunAsync(
            Criteria(), new ScriptedGrader(byText) { FailOn = Benchmark(4) }, responses);

        Assert.False(result.Accepted);
        Assert.Equal(RubricCalibrationOutcome.GraderFailed, result.Outcome);
        Assert.Contains("band 4", result.Refusal, StringComparison.Ordinal);
    }

    /// <summary>Returns what the test scripted for a response's text, for every criterion it was asked about.</summary>
    private sealed class ScriptedGrader(IReadOnlyDictionary<string, Func<GradingCriterionDetail, decimal>> byText)
        : IAiGrader
    {
        /// <summary>A response text this grader answers with a failure, the way a model call can fail.</summary>
        public string? FailOn { get; init; }

        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
        {
            if (request.ResponseText == FailOn)
            {
                return Task.FromResult(new AiGradingResult(
                    Success: false,
                    FailureReason: "The scripted grader was told to fail on this response.",
                    Model: "scripted-grader",
                    Scores: []));
            }

            var scores = request.Criteria
                .Select(c => new AiCriterionScoreResult(c.CriterionId, byText[request.ResponseText](c), "scripted"))
                .ToList();
            return Task.FromResult(new AiGradingResult(true, null, "scripted-grader", scores));
        }
    }
}
