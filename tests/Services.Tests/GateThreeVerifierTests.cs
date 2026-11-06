using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services.Tests;

/// <summary>
/// #196: the seam gate three crosses.
/// <para>
/// <see cref="GateThree"/> is a pure function and is tested exhaustively without a provider. What only
/// the seam can show is the claim the ticket makes about the ablation: <b>it is a second solve with
/// the stimulus withheld, through the same verifier, not a fourth code path</b>. A gate whose ablation
/// went through a different builder would pass every threshold test above while reading a prompt
/// nobody has ever checked.
/// </para>
/// <para>
/// Nothing here calls a real model. The verifier is <see cref="FakeVerifierModel"/> or a recording
/// double, both in-process, so <c>dotnet test</c> spends nothing - the same split the repo already
/// uses for grading behind <c>AI_PROVIDER</c>.
/// </para>
/// </summary>
public sealed class GateThreeVerifierTests
{
    private static readonly GateThreeSolveRequest Item = new(
        "What must every visitor do at the gate?",
        "Every visitor must sign the night ledger before entering the depot.",
        [
            new GateThreeOption("A", "The night ledger."),
            new GateThreeOption("B", "The depot shutter."),
            new GateThreeOption("C", "The depot supervisor."),
            new GateThreeOption("D", "The office door."),
        ]);

    private static Dictionary<string, decimal> Confident => new(StringComparer.Ordinal)
    {
        ["A"] = 0.90m, ["B"] = 0.05m, ["C"] = 0.03m, ["D"] = 0.02m,
    };

    private static Dictionary<string, decimal> Flat => new(StringComparer.Ordinal)
    {
        ["A"] = 0.25m, ["B"] = 0.25m, ["C"] = 0.25m, ["D"] = 0.25m,
    };

    /// <summary>
    /// Records what it was asked, so the stimulus the ablation withholds is observable rather than
    /// assumed. Answers with a fixed distribution.
    /// </summary>
    private sealed class RecordingVerifier(string family, Dictionary<string, decimal> answer) : IVerifierModel
    {
        public List<GateThreeSolveRequest> Requests { get; } = [];

        public Task<GateThreeSolve> SolveAsync(
            GateThreeSolveRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new GateThreeSolve(family, $"{family}-model", answer));
        }
    }

    /// <summary>
    /// The ablation is the same seam with one argument removed. This is the ticket's own constraint:
    /// if the ablation had its own builder, <see cref="RecordingVerifier.Requests"/> would hold a
    /// request from a class this test never constructed.
    /// </summary>
    [Fact]
    public async Task The_ablation_is_a_second_solve_through_the_same_verifier_with_the_stimulus_removed()
    {
        var one = new RecordingVerifier("gpt", Confident);
        var two = new RecordingVerifier("gemini", Confident);
        var gate = new GateThreeVerifier(one, two);

        await gate.ReviewAsync("claude", Item, "A");

        // Two solves from verifier one: the item as authored, then the same item blind.
        Assert.Equal(2, one.Requests.Count);
        Assert.Single(two.Requests);

        Assert.NotNull(one.Requests[0].Stimulus);
        Assert.Null(one.Requests[1].Stimulus);

        // Only the stimulus is withheld. A blind solve that also dropped the stem or an option would
        // be a different question, and the 0.50 ceiling would mean nothing.
        Assert.Equal(Item.Stem, one.Requests[1].Stem);
        Assert.Equal(Item.Options, one.Requests[1].Options);
    }

    /// <summary>
    /// The ablation solver is configurable, and when it is, it is the one asked - not verifier one.
    /// <b>Which guard could this pass while being wrong:</b> an implementation that hard-codes
    /// verifier one as the ablation solver passes the test above and ignores this argument entirely.
    /// </summary>
    [Fact]
    public async Task An_explicit_ablation_solver_replaces_the_default()
    {
        var one = new RecordingVerifier("gpt", Confident);
        var two = new RecordingVerifier("gemini", Confident);
        var third = new RecordingVerifier("llama", Flat);
        var gate = new GateThreeVerifier(one, two, third);

        await gate.ReviewAsync("claude", Item, "A");

        Assert.Single(one.Requests);
        Assert.Single(two.Requests);
        var blind = Assert.Single(third.Requests);
        Assert.Null(blind.Stimulus);
    }

    /// <summary>
    /// The verdict the seam returns is the one the pure function computes: accepted when both agree
    /// and the blind solve finds nothing, refused when they do not. The seam adds no opinion of its
    /// own, which is what keeps the thresholds in one place.
    /// </summary>
    [Fact]
    public async Task The_seam_returns_the_pure_functions_verdict()
    {
        var agreeing = new GateThreeVerifier(
            new FakeVerifierModel("gpt", "gpt-model") { Probabilities = Confident },
            new FakeVerifierModel("gemini", "gemini-model") { Probabilities = Confident },
            new FakeVerifierModel("gemini", "gemini-model") { Probabilities = Flat });

        var accepted = await agreeing.ReviewAsync("claude", Item, "A");
        Assert.True(accepted.Accepted);

        var dissenting = new GateThreeVerifier(
            new FakeVerifierModel("gpt", "gpt-model") { Probabilities = Confident },
            new FakeVerifierModel("gemini", "gemini-model") { Probabilities = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["A"] = 0.02m, ["B"] = 0.90m, ["C"] = 0.05m, ["D"] = 0.03m,
            } },
            new FakeVerifierModel("gemini", "gemini-model") { Probabilities = Flat });

        var refused = await dissenting.ReviewAsync("claude", Item, "A");
        Assert.False(refused.Accepted);
        Assert.Contains(refused.Refusals, r => r.Rule == GateThreeRules.Dissent);
    }

    /// <summary>
    /// The ablation is defaulted to verifier one, so the third call cannot silently skip a solver and
    /// leave the world-knowledge question unasked. Verifier one is asked twice and the second request
    /// has no stimulus - the assertion is on the requests, because a refusal for world knowledge is
    /// also reachable by asking verifier one the same question twice.
    /// </summary>
    [Fact]
    public async Task The_default_ablation_solver_is_verifier_one()
    {
        var one = new FakeVerifierModel("gpt", "gpt-model")
        {
            Probabilities = Confident,
            BlindProbabilities = Flat,
        };
        var two = new FakeVerifierModel("gemini", "gemini-model") { Probabilities = Confident };
        var gate = new GateThreeVerifier(one, two);

        var verdict = await gate.ReviewAsync("claude", Item, "A");

        Assert.True(verdict.Accepted, string.Join(", ", verdict.Refusals.Select(r => r.Rule)));
        Assert.Equal(2, one.Requests.Count);
        Assert.NotNull(one.Requests[0].Stimulus);
        Assert.Null(one.Requests[1].Stimulus);
        Assert.Single(two.Requests);
    }

    /// <summary>
    /// An unconfigured fake refuses. <b>Which guard could this pass while being wrong:</b> a fake that
    /// invented a confident agreement would let an environment with no verifier configured report
    /// every item as verified. The even split is the fake's "I do not know", and the gate must read it
    /// as a failure rather than a pass.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_fake_verifier_refuses_rather_than_inventing_agreement()
    {
        var gate = new GateThreeVerifier(
            new FakeVerifierModel("gpt", "gpt-model"),
            new FakeVerifierModel("gemini", "gemini-model"));

        var verdict = await gate.ReviewAsync("claude", Item, "A");

        Assert.False(verdict.Accepted);
        Assert.Contains(verdict.Refusals, r => r.Rule == GateThreeRules.KeyConfidence);
    }

    // -----------------------------------------------------------------------------------------
    // the live verifier: what it sends, and what it does with an unreadable answer
    // -----------------------------------------------------------------------------------------

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

    private static string DistributionJson(string a, string b, string c, string d) =>
        $$"""{ "probabilities": { "A": {{a}}, "B": {{b}}, "C": {{c}}, "D": {{d}} } }""";

    /// <summary>
    /// The key and the explanation are not in the prompt. The request type has no field for them, so
    /// this is a check that the prompt builder did not reintroduce one - the defect it catches is a
    /// future edit that adds "[KEY]" to help the model.
    /// </summary>
    [Fact]
    public async Task The_live_prompt_carries_no_key_and_no_explanation()
    {
        var client = new ScriptedClaudeClient(_ => DistributionJson("0.9", "0.05", "0.03", "0.02"));
        var verifier = new ClaudeVerifierModel(client, "gpt", "gpt-model");

        await verifier.SolveAsync(Item);

        var prompt = client.LastPrompt;
        Assert.Contains(Item.Stem, prompt, StringComparison.Ordinal);
        Assert.Contains("night ledger", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("correct answer", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[KEY", prompt, StringComparison.OrdinalIgnoreCase);

        // The blind solve must not leak the stimulus into the prompt either.
        var blind = await verifier.SolveAsync(Item.Blind());
        Assert.DoesNotContain("before entering the depot", client.LastPrompt, StringComparison.Ordinal);
        Assert.All(blind.Probabilities.Values, v => Assert.True(v >= 0m));
    }

    /// <summary>A markdown-fenced answer is still an answer: models wrap JSON whether asked or not.</summary>
    [Fact]
    public async Task A_markdown_fenced_distribution_is_read()
    {
        var client = new ScriptedClaudeClient(_ =>
            "```json\n" + DistributionJson("0.9", "0.05", "0.03", "0.02") + "\n```");
        var verifier = new ClaudeVerifierModel(client, "gpt");

        var solve = await verifier.SolveAsync(Item);

        Assert.Equal(0.90m, solve.Probabilities["A"]);
    }

    /// <summary>
    /// An unreadable answer becomes an empty distribution after the retry - never a confident guess.
    /// <b>Which guard could this pass while being wrong:</b> a parser that fell back to
    /// <c>new { A = 1.0m, ... }</c> on a parse failure would make every unparseable response look like
    /// perfect agreement. The empty distribution is the assertion, and it is what the gate refuses.
    /// </summary>
    [Fact]
    public async Task An_unreadable_answer_yields_no_distribution_rather_than_a_guess()
    {
        var client = new ScriptedClaudeClient(_ => "I think the answer is A.");
        var verifier = new ClaudeVerifierModel(client, "gpt");

        var solve = await verifier.SolveAsync(Item);

        Assert.Empty(solve.Probabilities);
        Assert.Equal(2, client.Prompts.Count); // one retry, then it gives up

        // ... and the gate refuses it instead of reading silence as agreement.
        var verdict = GateThree.Review("claude", "A", solve, solve with { ModelFamily = "gemini" },
            new GateThreeSolve("gemini", "m", Flat));
        Assert.Contains(verdict.Refusals, r => r.Rule == GateThreeRules.UnusableDistribution);
    }

    /// <summary>A distribution missing an option is not a distribution over this item's options.</summary>
    [Fact]
    public async Task A_distribution_missing_an_option_is_rejected()
    {
        var client = new ScriptedClaudeClient(_ =>
            """{ "probabilities": { "A": 0.9, "B": 0.05, "C": 0.05 } }""");
        var verifier = new ClaudeVerifierModel(client, "gpt");

        var solve = await verifier.SolveAsync(Item);

        Assert.Empty(solve.Probabilities);
    }

    /// <summary>The family the verifier declares is what the distinctness check reads, so it is passed through.</summary>
    [Fact]
    public async Task The_declared_family_is_carried_on_the_solve()
    {
        var client = new ScriptedClaudeClient(_ => DistributionJson("0.9", "0.05", "0.03", "0.02"));
        var verifier = new ClaudeVerifierModel(client, "gpt", "gpt-4o");

        var solve = await verifier.SolveAsync(Item);

        Assert.Equal("gpt", solve.ModelFamily);
        Assert.Equal("gpt-4o", solve.ModelName);
    }
}
