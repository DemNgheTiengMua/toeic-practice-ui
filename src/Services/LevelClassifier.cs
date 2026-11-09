using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

/// <summary>
/// #197: the one seam a level classification crosses. There is deliberately no confidence number:
/// no provider in this solution returns a calibrated posterior over six bands, and forcing one out of
/// a text-only model would produce a figure the product then has to trust. The declaration is a band,
/// which is exactly what D17's rule compares.
/// </summary>
public interface ILevelClassifier
{
    Task<GateFourClassification> ClassifyAsync(
        GateFourClassifyRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// #197: what the classifier is given. <b>The authored band is a separate field and is not sent</b>:
/// a prompt naming the band the author chose invites agreement, and a check that measures agreement
/// with its own input measures nothing. The field exists on the request so the record is
/// self-describing and so <c>Services.GateFourIngestion</c> can say what was compared.
/// </summary>
public sealed record GateFourClassifyRequest(string Text, CefrLevel AuthoredLevel);

/// <summary>
/// #197: the fake classifier, which is what the test suite and <c>AI_PROVIDER=fake</c> run. With no
/// <see cref="Predicted"/> it answers <see cref="GateFourClassification.Unavailable"/> - never the
/// authored band, which would let an unconfigured environment report every item level-consistent.
/// </summary>
public sealed class FakeLevelClassifier(string family, string? name = null) : ILevelClassifier
{
    /// <summary>The band this fake reports. Unset means "no classifier", which the gate refuses.</summary>
    public CefrLevel? Predicted { get; set; }

    /// <summary>Every request this fake was asked, so a seam test can see what was actually sent.</summary>
    public List<GateFourClassifyRequest> Requests { get; } = [];

    public Task<GateFourClassification> ClassifyAsync(
        GateFourClassifyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);

        return Task.FromResult<GateFourClassification>(Predicted is { } level
            ? new GateFourClassification.Predicted(family, name ?? family, level)
            : new GateFourClassification.Unavailable(
                "no classifier is configured, so the item's level was not predicted"));
    }
}

/// <summary>
/// #197: the live classifier. Asks a provider for one of A1-C2 as a forced JSON shape and reads the
/// band out of it.
/// <para>
/// <b>What the answer is and is not.</b> The provider returns text, not a distribution over the six
/// bands, so this is the model's own choice rendered as a label. It is read as a second opinion about
/// a text, which is what D17 asks for - "an independent classifier must predict L̂_i = L_i" - and not
/// as a probability that the band is right. A provider that can return logits over band tokens should
/// replace <see cref="ReadLevel"/> and the rule stands unchanged.
/// </para>
/// <para>
/// An unreadable, missing or out-of-scale answer is <see cref="GateFourClassification.Unavailable"/>,
/// never a fallback to the authored band: silence must not read as agreement.
/// </para>
/// </summary>
public sealed class ClaudeLevelClassifier : ILevelClassifier
{
    /// <summary>Two calls, then the item's level check fails. One retry fires only on an unreadable answer.</summary>
    private const int AttemptsAllowed = 2;

    private readonly IClaudeClient _client;

    public ClaudeLevelClassifier(IClaudeClient client, string family, string? name = null)
    {
        _client = client;
        Family = family;
        Name = name ?? family;
    }

    public string Family { get; }

    public string Name { get; }

    public async Task<GateFourClassification> ClassifyAsync(
        GateFourClassifyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prompt = BuildPrompt(request.Text);
        string? lastReason = null;

        for (var attempt = 1; attempt <= AttemptsAllowed; attempt++)
        {
            // #258: the classifier's family is the model it asks for, so a deployment that names one
            // family actually runs that one rather than whatever the client was configured with.
            var raw = await _client.CreateMessageAsync(
                prompt,
                systemPrompt: SystemPrompt,
                maxTokens: 200,
                model: Family,
                cancellationToken: cancellationToken);

            var (level, reason) = ReadLevel(raw);
            if (level is { } found)
            {
                return new GateFourClassification.Predicted(Family, Name, found);
            }

            lastReason = reason;
        }

        return new GateFourClassification.Unavailable(
            $"{Family}/{Name} returned no band ({lastReason})");
    }

    private const string SystemPrompt =
        "You assess the CEFR level of a short English text. You answer with one band and nothing else.";

    /// <summary>
    /// The prompt. <paramref name="text"/> is all it carries: no authored band, no topic, no item. The
    /// band's word-count envelope is deliberately not quoted either - telling the model "B1 is
    /// 220-300 words" turns the classifier into a word counter, and gate two already measures that.
    /// </summary>
    private static string BuildPrompt(string text)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Read this English text and decide the CEFR band a learner must hold to read or");
        prompt.AppendLine("listen to it comfortably.");
        prompt.AppendLine();
        prompt.AppendLine("[TEXT]");
        prompt.AppendLine(text);
        prompt.AppendLine();
        prompt.AppendLine("Return a strictly valid JSON object with exactly this shape:");
        prompt.AppendLine("{ \"level\": \"A1\" | \"A2\" | \"B1\" | \"B2\" | \"C1\" | \"C2\" }");
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- Exactly one band, from that list.");
        prompt.AppendLine("- Output JSON only, no markdown wrapping, no commentary.");
        return prompt.ToString();
    }

    /// <summary>
    /// The band in the answer, or the reason there is none. Returns a reason rather than a null so the
    /// caller's refusal can say what the model actually did.
    /// </summary>
    private static (CefrLevel? Level, string Reason) ReadLevel(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            if (!doc.RootElement.TryGetProperty("level", out var level)
                || level.ValueKind != JsonValueKind.String)
            {
                return (null, "the answer carries no 'level'");
            }

            var code = level.GetString();
            if (CefrLevel.TryParseAuthored(code, out var parsed))
            {
                return (parsed, string.Empty);
            }

            // The reportable floor and anything invented are both out of scale here: content is
            // authored at A1-C2, so "pre_a1" is not a band an item can be checked against.
            return (null, $"'{code}' is not one of A1-C2");
        }
        catch (JsonException)
        {
            return (null, "the answer is not JSON");
        }
    }

    /// <summary>Same fenced-block handling as the grader and the verifier: a model wraps JSON whether asked or not.</summary>
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
