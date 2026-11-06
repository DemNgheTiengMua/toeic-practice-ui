using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Services;

namespace Services.Tests;

/// <summary>
/// #197: gate four's two halves that do not fit in a pure function - the classifier, which is an AI
/// call, and the seven-response ladder, which is already a component (#193) and must be called rather
/// than copied.
/// <para>
/// Nothing here reaches a provider. The classifier is <see cref="FakeLevelClassifier"/> or a scripted
/// client, and the ladder's grader is a script, so <c>dotnet test</c> spends nothing.
/// </para>
/// </summary>
public sealed class GateFourIngestionTests
{
    // -----------------------------------------------------------------------------------------
    // the classifier seam
    // -----------------------------------------------------------------------------------------

    /// <summary>The accept direction through the seam, so a seam that refused everything is visible.</summary>
    [Fact]
    public async Task The_seam_passes_the_classifier_verdict_to_the_rule()
    {
        var classifier = new FakeLevelClassifier("gemini") { Predicted = CefrLevel.B1 };

        var verdict = await GateFourIngestion.ReviewLevelAsync(
            "claude", CefrLevel.B1, "A passage about a library move.", classifier);

        Assert.True(verdict.Accepted, string.Join(", ", verdict.Refusals.Select(r => r.Rule)));
        Assert.Equal(CefrLevel.B1, verdict.Predicted);
    }

    /// <summary>A disagreement through the seam names the predicted band, exactly as the pure rule does.</summary>
    [Fact]
    public async Task The_seam_refuses_a_disagreement_and_reports_the_predicted_band()
    {
        var classifier = new FakeLevelClassifier("gemini") { Predicted = CefrLevel.A2 };

        var verdict = await GateFourIngestion.ReviewLevelAsync("claude", CefrLevel.B1, "Text.", classifier);

        Assert.False(verdict.Accepted);
        Assert.Equal(CefrLevel.A2, verdict.Predicted);
        Assert.Contains(verdict.Refusals, r =>
            r.Rule == GateFourRules.LevelDisagreement && r.Message.Contains("A2", StringComparison.Ordinal));
    }

    /// <summary>
    /// An unconfigured fake refuses. <b>Which guard could this pass while being wrong:</b> a fake with
    /// no prediction that defaulted to the authored band would report every item level-consistent in an
    /// environment where no classifier exists.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_fake_classifier_refuses_rather_than_agreeing()
    {
        var verdict = await GateFourIngestion.ReviewLevelAsync(
            "claude", CefrLevel.B1, "Text.", new FakeLevelClassifier("gemini"));

        Assert.False(verdict.Accepted);
        Assert.Contains(verdict.Refusals, r => r.Rule == GateFourRules.ClassifierUnavailable);
    }

    private sealed class ScriptedClaudeClient(Func<int, string> respond) : IClaudeClient
    {
        public List<string> Prompts { get; } = [];

        public string LastPrompt => Prompts[^1];

        public Task<string> CreateMessageAsync(
            string userPrompt, string? systemPrompt = null, int maxTokens = 1500,
            CancellationToken cancellationToken = default)
        {
            Prompts.Add(userPrompt);
            return Task.FromResult(respond(Prompts.Count));
        }
    }

    /// <summary>
    /// The authored band does not reach the prompt. <b>Which guard could this pass while being wrong:</b>
    /// a prompt that names the band the author chose invites agreement and reports 100% consistency
    /// for a classifier that only ever repeats what it was told - the check would measure nothing.
    /// <para>
    /// Asserted by <em>equality of two prompts</em> over the same text at two different authored
    /// bands, because every band code appears in the forced-JSON enumeration and a substring check
    /// for "B1" would fire on the format rather than on a leak.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_live_classifier_is_not_told_the_authored_band()
    {
        var client = new ScriptedClaudeClient(_ => """{ "level": "B1" }""");
        var classifier = new ClaudeLevelClassifier(client, "gemini", "gemini-model");
        const string Text = "The library will move next month.";

        await classifier.ClassifyAsync(new GateFourClassifyRequest(Text, CefrLevel.A2));
        await classifier.ClassifyAsync(new GateFourClassifyRequest(Text, CefrLevel.C1));

        Assert.Contains("library will move", client.Prompts[0], StringComparison.Ordinal);
        Assert.Equal(client.Prompts[0], client.Prompts[1]);
        Assert.DoesNotContain("authored", client.Prompts[0], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A markdown-fenced answer is still an answer: models wrap JSON whether asked or not.</summary>
    [Fact]
    public async Task A_markdown_fenced_level_is_read()
    {
        var client = new ScriptedClaudeClient(_ => "```json\n{ \"level\": \"c2\" }\n```");
        var classifier = new ClaudeLevelClassifier(client, "gemini");

        var classification = await classifier.ClassifyAsync(new GateFourClassifyRequest("Text.", CefrLevel.C2));

        var predicted = Assert.IsType<GateFourClassification.Predicted>(classification);
        Assert.Equal(CefrLevel.C2, predicted.Level);
    }

    /// <summary>
    /// An unreadable answer is <em>unavailable</em> after the retry, never a guess at the authored
    /// band. <b>Which guard could this pass while being wrong:</b> a parser falling back to the
    /// authored level turns every unparseable response into perfect agreement.
    /// </summary>
    [Fact]
    public async Task An_unreadable_answer_is_unavailable_rather_than_a_guess()
    {
        var client = new ScriptedClaudeClient(_ => "I would say B1 or B2, it depends.");
        var classifier = new ClaudeLevelClassifier(client, "gemini");

        var classification = await classifier.ClassifyAsync(new GateFourClassifyRequest("Text.", CefrLevel.B1));

        Assert.IsType<GateFourClassification.Unavailable>(classification);
        Assert.Equal(2, client.Prompts.Count); // one retry, then it gives up
    }

    /// <summary>
    /// A band the scale does not carry is not a prediction. "B1+" or "pre_a1" would otherwise be read
    /// as an answer and then compared against an authored band with no meaning.
    /// </summary>
    [Theory]
    [InlineData("B1+")]
    [InlineData("pre_a1")]
    [InlineData("7")]
    public async Task A_level_outside_the_authored_scale_is_unavailable(string answer)
    {
        var client = new ScriptedClaudeClient(_ => $"{{ \"level\": \"{answer}\" }}");
        var classifier = new ClaudeLevelClassifier(client, "gemini");

        var classification = await classifier.ClassifyAsync(new GateFourClassifyRequest("Text.", CefrLevel.B1));

        Assert.IsType<GateFourClassification.Unavailable>(classification);
    }

    // -----------------------------------------------------------------------------------------
    // the seven-response ladder, enforced at ingestion
    // -----------------------------------------------------------------------------------------

    private static readonly string[] Codes = ["W_TR", "W_CC", "W_LR", "W_GR"];

    private static IReadOnlyList<GradingCriterionDetail> Criteria() =>
        Codes.Select((code, i) => new GradingCriterionDetail(
            i + 1, code, code, MaxScore: 6m, Weight: 1m, RubricDescription: $"Prose for {code}.")).ToList();

    private static string Benchmark(int band) => $"benchmark band {band}";

    private const string Trap = "verbose b1 trap";

    private static IReadOnlyList<RubricLadderResponse> Responses() =>
        [.. Enumerable.Range(1, 6).Select(b => new RubricLadderResponse((byte)b, Benchmark(b))),
         new RubricLadderResponse(3, Trap, IsVerboseTrap: true)];

    private static Dictionary<string, Func<GradingCriterionDetail, decimal>> FlatAll(decimal band) =>
        Responses().ToDictionary(r => r.ResponseText, _ => (Func<GradingCriterionDetail, decimal>)(_ => band));

    /// <summary>A calibrated rubric passes ingestion, so the ladder check is not a blanket refusal.</summary>
    [Fact]
    public async Task A_calibrated_rubric_passes_the_ingestion_gate()
    {
        var byText = new Dictionary<string, Func<GradingCriterionDetail, decimal>>
        {
            [Benchmark(1)] = _ => 1m, [Benchmark(2)] = _ => 2m, [Benchmark(3)] = _ => 3m,
            [Benchmark(4)] = _ => 4m, [Benchmark(5)] = _ => 5m, [Benchmark(6)] = _ => 6m,
            [Trap] = _ => 2m,
        };

        var refusals = await GateFourIngestion.ReviewProductiveAsync(Criteria(), new ScriptedGrader(byText), Responses());

        Assert.Empty(refusals);
    }

    /// <summary>
    /// The separation rule is the ladder's, not a second copy of it. <b>Which guard could this pass
    /// while being wrong:</b> a segment of 0.64 rises strictly, so a gate that only checked
    /// monotonicity accepts a rubric whose adjacent bands are one rounding apart.
    /// </summary>
    [Fact]
    public async Task A_ladder_that_rises_but_separates_by_less_than_the_threshold_is_refused()
    {
        var byText = new Dictionary<string, Func<GradingCriterionDetail, decimal>>
        {
            [Benchmark(1)] = _ => 1m,
            [Benchmark(2)] = _ => 2m,
            [Benchmark(3)] = _ => 3m,
            [Benchmark(4)] = _ => 3.64m,
            [Benchmark(5)] = _ => 5m,
            [Benchmark(6)] = _ => 6m,
            [Trap] = _ => 2m,
        };

        var refusals = await GateFourIngestion.ReviewProductiveAsync(Criteria(), new ScriptedGrader(byText), Responses());

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.LadderSeparation, refusal.Rule);
    }

    /// <summary>A flat segment is its own rule, so an author is not sent to widen a gap that is flat.</summary>
    [Fact]
    public async Task A_flat_segment_is_refused_as_non_monotonic_rather_than_as_separation()
    {
        var byText = new Dictionary<string, Func<GradingCriterionDetail, decimal>>
        {
            [Benchmark(1)] = _ => 1m, [Benchmark(2)] = _ => 2m, [Benchmark(3)] = _ => 3m,
            [Benchmark(4)] = _ => 3m, [Benchmark(5)] = _ => 5m, [Benchmark(6)] = _ => 6m,
            [Trap] = _ => 2m,
        };

        var refusals = await GateFourIngestion.ReviewProductiveAsync(Criteria(), new ScriptedGrader(byText), Responses());

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.LadderMonotonic, refusal.Rule);
    }

    /// <summary>
    /// The trap ceiling at ingestion. <b>Which guard could this pass while being wrong:</b> 3.25 is the
    /// ceiling, so a trap scoring exactly 3.26 must be refused while 3.25 is accepted - a check written
    /// as "&gt; 3.30" or as a rounded comparison passes the 280-word padding it exists to catch.
    /// </summary>
    [Fact]
    public async Task The_verbose_trap_above_the_ceiling_is_refused_and_at_the_ceiling_is_accepted()
    {
        Dictionary<string, Func<GradingCriterionDetail, decimal>> WithTrap(decimal trap) => new()
        {
            [Benchmark(1)] = _ => 1m, [Benchmark(2)] = _ => 2m, [Benchmark(3)] = _ => 3m,
            [Benchmark(4)] = _ => 4m, [Benchmark(5)] = _ => 5m, [Benchmark(6)] = _ => 6m,
            [Trap] = _ => trap,
        };

        var above = await GateFourIngestion.ReviewProductiveAsync(Criteria(), new ScriptedGrader(WithTrap(3.26m)), Responses());
        var at = await GateFourIngestion.ReviewProductiveAsync(Criteria(), new ScriptedGrader(WithTrap(3.25m)), Responses());

        var refusal = Assert.Single(above);
        Assert.Equal(GateFourRules.LadderTrapCeiling, refusal.Rule);
        Assert.Empty(at);
    }

    /// <summary>
    /// A grader that failed did not calibrate anything, and silence must not read as a pass. The
    /// refusal names the ladder so an author knows the rubric was not the thing that failed.
    /// </summary>
    [Fact]
    public async Task A_failed_grading_call_refuses_rather_than_passing_the_ladder()
    {
        var refusals = await GateFourIngestion.ReviewProductiveAsync(
            Criteria(), new ScriptedGrader(FlatAll(3m)) { FailOn = Benchmark(4) }, Responses());

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.LadderNotPlotted, refusal.Rule);
        Assert.Contains("band 4", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ladder that is missing a benchmark is refused by the ladder itself, and gate four must report
    /// that as a refusal rather than letting the exception escape an ingestion run: an exception per
    /// record loses the per-line report #198 owns.
    /// </summary>
    [Fact]
    public async Task A_ladder_missing_a_benchmark_refuses_rather_than_throwing_out_of_the_gate()
    {
        var partial = Responses().Where(r => r.Band != 6 || r.IsVerboseTrap).ToList();

        var refusals = await GateFourIngestion.ReviewProductiveAsync(Criteria(), new ScriptedGrader(FlatAll(3m)), partial);

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.LadderIncomplete, refusal.Rule);
        Assert.Contains("band 6", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Returns what the test scripted for a response's text.</summary>
    private sealed class ScriptedGrader(IReadOnlyDictionary<string, Func<GradingCriterionDetail, decimal>> byText)
        : IAiGrader
    {
        public string? FailOn { get; init; }

        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
        {
            if (request.ResponseText == FailOn)
            {
                return Task.FromResult(new AiGradingResult(
                    Success: false, FailureReason: "the scripted grader was told to fail", Model: "scripted", Scores: []));
            }

            var scores = request.Criteria
                .Select(c => new AiCriterionScoreResult(c.CriterionId, byText[request.ResponseText](c), "scripted"))
                .ToList();
            return Task.FromResult(new AiGradingResult(true, null, "scripted", scores));
        }
    }
}
