using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

/// <summary>
/// #196: the one seam a blind solve crosses. Gate three has two verifiers and one ablation, and all
/// three are calls to this method - the ablation differs only by a <see cref="GateThreeSolveRequest"/>
/// whose stimulus is null, so "without the passage" is not a second code path that can drift from the
/// first (D17).
/// <para>
/// The request carries no key, no explanation and no rationale, so a verifier cannot be handed the
/// answer by a caller that forgot to strip it. <see cref="GateThreeSolve.ModelFamily"/> is the
/// declaration the distinctness check reads; it is a claim by the operator, not something the wire
/// provider proves - see <see cref="GateThree"/>'s note on what the gate cannot see.
/// </para>
/// </summary>
public interface IVerifierModel
{
    Task<GateThreeSolve> SolveAsync(GateThreeSolveRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// #196: the fake verifier, which is what the test suite and <c>AI_PROVIDER=fake</c> run.
/// <para>
/// It has no model behind it and therefore no judgement: it returns the distribution in
/// <see cref="Scripted"/>, and a fixed family name. Every behaviour gate three can express is a
/// property of a distribution, so a fake that returns a chosen distribution exercises the whole gate
/// without a network call - and the gate's own arithmetic is what is under test, not the fake.
/// </para>
/// <para>
/// With no script the fake answers "no opinion": an even split, which every threshold in the gate
/// refuses. A fake that invented a confident answer would let an unconfigured environment pass a
/// gate nobody ran.
/// </para>
/// </summary>
public sealed class FakeVerifierModel(string family, string name) : IVerifierModel
{
    /// <summary>
    /// Returns <see cref="BlindProbabilities"/> for a stimulus-blind request and
    /// <see cref="Probabilities"/> otherwise; either falls back to an even split.
    /// <para>
    /// The two scripts exist because a fake that returned one answer to both calls would make every
    /// ablation equal to its solve: with a confident solve that is always a world-knowledge refusal,
    /// which would look like a passing test of the ablation and is not one. A fake that can be
    /// confident with the passage and unsure without it is what an ablation test needs.
    /// </para>
    /// </summary>
    public Task<GateThreeSolve> SolveAsync(
        GateThreeSolveRequest request,
        CancellationToken cancellationToken = default)
    {
        var scripted = request.Stimulus is null && BlindProbabilities is { Count: > 0 }
            ? BlindProbabilities
            : Probabilities is { Count: > 0 }
                ? Probabilities
                : null;

        Requests.Add(request);

        var distribution = scripted is not null
            ? new Dictionary<string, decimal>(scripted, StringComparer.Ordinal)
            : new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["A"] = 0.25m, ["B"] = 0.25m, ["C"] = 0.25m, ["D"] = 0.25m,
            };

        return Task.FromResult(new GateThreeSolve(family, name, distribution));
    }

    /// <summary>The distribution this fake reports for a solve that carries the stimulus.</summary>
    public Dictionary<string, decimal>? Probabilities { get; set; }

    /// <summary>
    /// The distribution it reports when the stimulus is withheld. Unset means "same as
    /// <see cref="Probabilities"/>", which for a confident script is a world-knowledge refusal - the
    /// honest answer for a fake that cannot change its mind.
    /// </summary>
    public Dictionary<string, decimal>? BlindProbabilities { get; set; }

    /// <summary>
    /// Every request this fake was asked, in order. A fake that answers without recording cannot show
    /// that the ablation was withheld the stimulus rather than simply asking for it twice.
    /// </summary>
    public List<GateThreeSolveRequest> Requests { get; } = [];
}

/// <summary>
/// #196: the live verifier. Asks a provider to pick one option blind and to say how much weight it
/// put on each, as a forced JSON shape.
/// <para>
/// <b>Where the probabilities come from, and what they are not.</b> The provider behind
/// <see cref="IClaudeClient"/> returns text, not log-probabilities, so the number is the model's own
/// reported distribution over the four labels, not a calibrated posterior. It is read as a
/// consistency signal - "is this option clearly above the others" - and never as the chance the item's
/// key is right. A provider that can return token logits should replace <see cref="ReadResponse"/>
/// and the thresholds stand; the thresholds are about the shape of the distribution, not its source.
/// </para>
/// <para>
/// An unreadable or missing distribution is returned as an empty one, never as a confident guess:
/// <see cref="GateThree"/> refuses an unusable distribution, which is the difference between "the
/// verifier said nothing" and "the verifier agreed".
/// </para>
/// </summary>
public sealed class ClaudeVerifierModel : IVerifierModel
{
    /// <summary>
    /// Two calls, then the item's verification fails. One retry only fires when the first answer could
    /// not be read, so the ordinary path is one request per solve.
    /// </summary>
    private const int AttemptsAllowed = 2;

    private readonly IClaudeClient _client;

    public ClaudeVerifierModel(IClaudeClient client, string family, string? name = null)
    {
        _client = client;
        Family = family;
        Name = name ?? family;
    }

    public string Family { get; }

    public string Name { get; }

    public async Task<GateThreeSolve> SolveAsync(
        GateThreeSolveRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(request);
        for (var attempt = 1; attempt <= AttemptsAllowed; attempt++)
        {
            var raw = await _client.CreateMessageAsync(
                prompt,
                systemPrompt: SystemPrompt,
                maxTokens: 400,
                cancellationToken: cancellationToken);

            var probabilities = ReadResponse(raw);
            if (probabilities is not null)
            {
                return new GateThreeSolve(Family, Name, probabilities);
            }
        }

        return new GateThreeSolve(Family, Name, new Dictionary<string, decimal>(StringComparer.Ordinal));
    }

    private const string SystemPrompt =
        "You answer multiple-choice questions. You never ask for the answer key, you never guess from "
        + "option position, and you report your own weight on every option as JSON.";

    /// <summary>
    /// The blind prompt. There is no branch that mentions the key or the explanation because the
    /// request type has neither; the stimulus is simply absent when the ablation runs.
    /// </summary>
    private static string BuildPrompt(GateThreeSolveRequest request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Solve this item as a candidate sitting the exam would: choose one option.");
        prompt.AppendLine();
        if (request.Stimulus is not null)
        {
            prompt.AppendLine("[PASSAGE OR TRANSCRIPT]");
            prompt.AppendLine(request.Stimulus);
            prompt.AppendLine();
        }

        prompt.AppendLine("[QUESTION]");
        prompt.AppendLine(request.Stem);
        prompt.AppendLine();
        prompt.AppendLine("[OPTIONS]");
        foreach (var option in request.Options)
        {
            prompt.AppendLine($"{option.Label}. {option.Text}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Return a strictly valid JSON object with exactly this shape:");
        prompt.AppendLine("{ \"probabilities\": { \"A\": <number>, \"B\": <number>, \"C\": <number>, \"D\": <number> } }");
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- Give one number per option, all four present.");
        prompt.AppendLine("- The four numbers must sum to 1.0 and express your own weight on each option.");
        prompt.AppendLine("- Output JSON only, no markdown wrapping, no commentary.");
        return prompt.ToString();
    }

    /// <summary>
    /// A distribution over A-D, or <c>null</c> when the answer carries none. Returns <c>null</c> rather
    /// than a default so the caller can tell "unreadable" from "an even spread".
    /// </summary>
    private static Dictionary<string, decimal>? ReadResponse(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            if (!doc.RootElement.TryGetProperty("probabilities", out var probabilities)
                || probabilities.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var property in probabilities.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetDecimal(out var value))
                {
                    return null;
                }

                values[property.Name.Trim().ToUpperInvariant()] = value;
            }

            // All four, or it is not a distribution over this item's options.
            foreach (var label in new[] { "A", "B", "C", "D" })
            {
                if (!values.ContainsKey(label))
                {
                    return null;
                }
            }

            return values;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Same fenced-block handling as the graders: a model occasionally wraps JSON in markdown.</summary>
    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 7)
            {
                return trimmed[7..end].Trim();
            }
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 3)
            {
                return trimmed[3..end].Trim();
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        return firstBrace >= 0 && lastBrace > firstBrace
            ? trimmed[firstBrace..(lastBrace + 1)]
            : trimmed;
    }
}
