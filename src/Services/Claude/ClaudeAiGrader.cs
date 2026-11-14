using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>
/// The live writing grader.
/// </summary>
/// <remarks>
/// E2.1 (#151): the prompt carries each criterion's band descriptors, and the checklist (length,
/// required keywords) is decided from the text before a single score is asked for. A criterion the
/// model leaves out is not filled in with a plausible number: the call is retried once, and a second
/// omission fails the grading with a reason the learner can read. That is the whole point - an
/// omission must never become a passing grade.
/// </remarks>
public sealed class ClaudeAiGrader : IAiGrader
{
    /// <summary>One retry, then the grading is Failed (#151). A second call only happens when the first
    /// answer was unusable, so the ordinary case still costs one request.</summary>
    private const int AttemptsAllowed = 2;

    /// <summary>Matches <c>AiGradings.FailureReason</c> NVARCHAR(400); a longer reason would be cut off by
    /// the column, and a reason the learner cannot read the end of is not a reason.</summary>
    private const int FailureReasonMaxLength = 400;

    /// <summary>The ticket's cap. More than five corrections is a wall, not feedback.</summary>
    private const int MaxErrorSpans = 5;

    private const string ModelName = "claude-sonnet-3.7";

    private readonly IClaudeClient _claudeClient;
    private readonly ILogger<ClaudeAiGrader>? _logger;

    public ClaudeAiGrader(IClaudeClient claudeClient, ILogger<ClaudeAiGrader>? logger = null)
    {
        _claudeClient = claudeClient;
        _logger = logger;
    }

    public async Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
    {
        // If request has simulated failure marker for test purposes, return explicit failure
        if (request.ResponseText?.Contains(FakeAiGrader.SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true)
        {
            return Fail("Simulated grading failure.");
        }

        if (request.Criteria.Count == 0)
        {
            return Fail("Nothing was scored: this task has no grading criteria at all.");
        }

        var gradeable = request.Criteria.ToList();

        var checklist = ChecklistResult.For(request);
        if (checklist.IsEmpty)
        {
            // Nothing was written. Saying so costs no request and no credit, and it cannot be a score:
            // every mark would be invented.
            return Fail("No answer was submitted for this task, so there is nothing to score.");
        }

        var prompt = BuildPrompt(request, gradeable, checklist);
        // Debug, not Information: the prompt carries the learner's own text, so it is off by default and
        // turned on only for the audit run that has to show what the model was actually asked
        // (Logging__LogLevel__Services.ClaudeAiGrader=Debug).
        _logger?.LogDebug("Grading prompt for FreeResponseId {FreeResponseId}:\n{Prompt}", request.FreeResponseId, prompt);
        var systemPrompt = "You are a senior, certified official English exam evaluator (IELTS, TOEIC, VSTEP). "
            + "You score strictly against the rubric you are given, you never award a mark for a criterion you "
            + "were not asked to score, and you quote the candidate's own words when you point at an error.";

        string? lastProblem = null;

        for (var attempt = 1; attempt <= AttemptsAllowed; attempt++)
        {
            var raw = await _claudeClient.CreateMessageAsync(
                prompt,
                systemPrompt: systemPrompt,
                maxTokens: 4000,
                cancellationToken: cancellationToken);

            _logger?.LogInformation("Claude grading response received, length {Len} (attempt {Attempt})", raw.Length, attempt);

            AiGradingResult? parsed;
            string? problem;
            try
            {
                (parsed, problem) = ReadResponse(raw, request, gradeable, checklist);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Claude grading response was not valid JSON: {Raw}", raw);
                (parsed, problem) = (null, "Claude returned a response that was not valid JSON.");
            }

            if (parsed is not null) return parsed;
            lastProblem = problem;
            _logger?.LogWarning("Claude grading attempt {Attempt} unusable: {Problem}", attempt, problem);
        }

        // The second answer was unusable too. No scores, no filler, and the reason says what the model
        // actually did, which is the difference between "retry may help" and "retry will not".
        return Fail(lastProblem ?? "Claude returned no usable grading.");
    }

    private static AiGradingResult Fail(string reason) =>
        new(
            Success: false,
            FailureReason: Truncate(reason),
            Model: ModelName,
            Scores: Array.Empty<AiCriterionScoreResult>());

    private static string Truncate(string reason) =>
        reason.Length <= FailureReasonMaxLength ? reason : reason[..(FailureReasonMaxLength - 1)] + "…";

    private static string BuildPrompt(
        AiGradingRequest request,
        IReadOnlyList<GradingCriterionDetail> gradeable,
        ChecklistResult checklist)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Grade the candidate's English response against the rubric below.");
        if (!string.IsNullOrWhiteSpace(request.CertName) || !string.IsNullOrWhiteSpace(request.CurrentBandCode) || !string.IsNullOrWhiteSpace(request.TargetBandCode))
        {
            prompt.AppendLine();
            prompt.AppendLine("[LEARNER CONTEXT]");
            if (!string.IsNullOrWhiteSpace(request.CertName))
                prompt.AppendLine($"- Certificate: {request.CertName}");
            if (!string.IsNullOrWhiteSpace(request.CurrentBandCode) || !string.IsNullOrWhiteSpace(request.TargetBandCode))
                prompt.AppendLine($"- Band Goal: {request.CurrentBandCode ?? "N/A"} -> {request.TargetBandCode ?? "N/A"}");
        }
        prompt.AppendLine();
        prompt.AppendLine("[TASK PROMPT]");
        prompt.AppendLine(request.QuestionStem);
        if (!string.IsNullOrWhiteSpace(request.Guide))
        {
            prompt.AppendLine();
            prompt.AppendLine("[TASK GUIDE / CHECKLIST]");
            prompt.AppendLine(request.Guide);
        }
        prompt.AppendLine();
        prompt.AppendLine("[CHECKLIST - already computed from the text. Do not recompute it, and do not award a score that contradicts it.]");
        prompt.AppendLine($"- wordCount: {checklist.WordCount}");
        prompt.AppendLine($"- meetsLength: {checklist.MeetsLength.ToString().ToLowerInvariant()}"
            + (request.MinWords.HasValue || request.MaxWords.HasValue
                ? $" (minimum {request.MinWords ?? 0}, maximum {request.MaxWords?.ToString() ?? "none"})"
                : ""));
        prompt.AppendLine($"- usesAllKeywords: {checklist.UsesAllKeywords.ToString().ToLowerInvariant()}"
            + (checklist.MissingKeywords.Count > 0
                ? $" (missing: {string.Join(", ", checklist.MissingKeywords)})"
                : ""));
        prompt.AppendLine("- addressesPrompt: answer this one yourself in the JSON below.");
        prompt.AppendLine();
        prompt.AppendLine("[CANDIDATE RESPONSE]");
        prompt.AppendLine(request.ResponseText);
        prompt.AppendLine();
        prompt.AppendLine("[CRITERIA TO EVALUATE]");
        foreach (var c in gradeable)
        {
            prompt.AppendLine($"- ID: {c.CriterionId}, Code: {c.Code}, Name: {c.Name}, MaxScore: {c.MaxScore}, Weight: {c.Weight}");
            // #192: a complete grid is the standard. Each band is listed with its gates, so the model
            // is asked which band's gates the response clears rather than where it "feels" on a scale.
            // The prose is kept alongside because it often carries the task-specific reading.
            if (c.Descriptors is { Count: 7 })
            {
                prompt.AppendLine("  Bands (0 = Pre-A1, 6 = C2). Award the highest band whose gates ALL hold:");
                foreach (var d in c.Descriptors.OrderBy(d => d.Band))
                {
                    prompt.AppendLine($"    Band {d.Band} - {d.Descriptor}");
                    prompt.AppendLine($"      gates: {d.BinaryGates}");
                }
                if (!string.IsNullOrWhiteSpace(c.RubricDescription))
                {
                    prompt.AppendLine($"  Rubric notes: {c.RubricDescription}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(c.RubricDescription))
            {
                prompt.AppendLine($"  Rubric: {c.RubricDescription}");
            }
            else
            {
                prompt.AppendLine($"  Rubric: Evaluate {c.Name} strictly on a 0 to {c.MaxScore} scale.");
            }

            // #193: the calibrated anchors go after the grid, so the model has the abstract ladder and
            // then three worked answers to compare the candidate against. Quoted with the band each was
            // written at, because an anchor without its band is just another response.
            if (c.Anchors is { Count: > 0 })
            {
                prompt.AppendLine("  Benchmark responses already scored at these bands (D0.6 anchors). Compare the candidate to these, not to an impression:");
                foreach (var a in c.Anchors.OrderBy(a => a.Band))
                {
                    prompt.AppendLine($"    Band {a.Band} anchor:");
                    prompt.AppendLine(a.ResponseText.Trim());
                }
            }
        }
        prompt.AppendLine();
        prompt.AppendLine("Return a strictly valid JSON object with exactly this shape:");
        prompt.AppendLine("{");
        prompt.AppendLine("  \"checklist\": { \"addressesPrompt\": <true|false> },");
        prompt.AppendLine("  \"scores\": [");
        prompt.AppendLine("    { \"criterionId\": <int>, \"score\": <decimal>, \"comment\": \"<specific note on this criterion>\" }");
        prompt.AppendLine("  ],");
        prompt.AppendLine("  \"errorSpans\": [");
        prompt.AppendLine("    { \"originalSnippet\": \"<exact fragment copied from the candidate response>\",");
        prompt.AppendLine("      \"correctedSnippet\": \"<the same fragment, corrected>\",");
        prompt.AppendLine("      \"category\": \"<grammar|vocabulary|spelling|punctuation|cohesion|task>\",");
        prompt.AppendLine("      \"explanationVi\": \"<one sentence, in Vietnamese, on why this is wrong>\" }");
        prompt.AppendLine("  ],");
        prompt.AppendLine("  \"priorityFix\": \"<the single change that would improve this text most, one sentence>\",");
        prompt.AppendLine("  \"modelRewrite\": \"<your rewrite of the weakest sentence>\"");
        prompt.AppendLine("}");
        prompt.AppendLine("Rules:");
        prompt.AppendLine($"- Return one entry in \"scores\" for every criterion listed above, and only those (IDs: {string.Join(", ", gradeable.Select(c => c.CriterionId))}).");
        prompt.AppendLine("- Score must be between 0.0 and MaxScore for that criterion.");
        prompt.AppendLine($"- Report at most {MaxErrorSpans} error spans. Every originalSnippet must appear in the candidate response exactly as written; if you cannot quote it, do not report it.");
        prompt.AppendLine("- If the response does not answer the task at all, return \"scores\": [] and explain in priorityFix. Do not guess a mark.");
        prompt.AppendLine("- Output JSON only, no markdown wrapping.");
        return prompt.ToString();
    }

    /// <summary>
    /// Turns one model answer into a result, or says why it cannot be one. Nothing is filled in: a
    /// criterion the model stayed silent on is a hole in the answer, not a standard performance.
    /// </summary>
    private static (AiGradingResult? Result, string? Problem) ReadResponse(
        string rawResult,
        AiGradingRequest request,
        IReadOnlyList<GradingCriterionDetail> gradeable,
        ChecklistResult checklist)
    {
        using var doc = JsonDocument.Parse(ExtractJson(rawResult));
        var root = doc.RootElement;

        if (!root.TryGetProperty("scores", out var scoresArray) || scoresArray.ValueKind != JsonValueKind.Array)
        {
            return (null, "Claude's response did not contain the expected 'scores' array.");
        }

        var addressesPrompt = checklist.AddressesPromptHint;
        if (root.TryGetProperty("checklist", out var chk)
            && chk.ValueKind == JsonValueKind.Object
            && chk.TryGetProperty("addressesPrompt", out var ap)
            && ap.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            addressesPrompt = ap.GetBoolean();
        }

        var reported = new Dictionary<int, AiCriterionScoreResult>();
        foreach (var el in scoresArray.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object
                || !el.TryGetProperty("criterionId", out var idProp)
                || !idProp.TryGetInt32(out var id)
                || !el.TryGetProperty("score", out var scoreProp)
                || !scoreProp.TryGetDecimal(out var score))
            {
                continue;
            }

            var criterion = gradeable.FirstOrDefault(c => c.CriterionId == id);
            if (criterion is null) continue;   // a mark for something we did not ask about is not usable

            var comment = el.TryGetProperty("comment", out var commentProp) && commentProp.ValueKind == JsonValueKind.String
                ? commentProp.GetString()
                : null;
            reported[id] = new AiCriterionScoreResult(id, Math.Clamp(score, 0m, criterion.MaxScore), comment);
        }

        var missing = gradeable.Where(c => !reported.ContainsKey(c.CriterionId)).ToList();

        // E2.1 (#151): the checklist runs before a score is finalised, so the model's own verdict on
        // "does this answer the task" decides first. A model that says no and then hands out marks
        // anyway is scoring the fluency of an answer to a question nobody asked; the live run did
        // exactly this on three nonsense words. No marks are kept.
        if (!addressesPrompt)
        {
            return (null, "The response does not answer the task, so it cannot be scored. "
                + "Write an answer that addresses the prompt and grade it again.");
        }

        if (missing.Count > 0)
        {
            return (null, $"Claude returned no score for {string.Join(", ", missing.Select(c => $"{c.Code} ({c.CriterionId})"))}.");
        }

        // Only spans that quote the candidate's own words survive: a correction of a sentence that is
        // not in the answer is worse than no correction at all.
        var spans = new List<AiErrorSpan>();
        if (root.TryGetProperty("errorSpans", out var spanArray) && spanArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in spanArray.EnumerateArray())
            {
                if (spans.Count >= MaxErrorSpans) break;
                if (el.ValueKind != JsonValueKind.Object) continue;

                var original = Text(el, "originalSnippet");
                var corrected = Text(el, "correctedSnippet");
                if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(corrected)) continue;
                if (!Quotes(request.ResponseText, original)) continue;

                spans.Add(new AiErrorSpan(
                    original.Trim(),
                    corrected.Trim(),
                    Text(el, "category") ?? "other",
                    Text(el, "explanationVi") ?? string.Empty));
            }
        }

        return (new AiGradingResult(
            Success: true,
            FailureReason: null,
            Model: ModelName,
            Scores: gradeable.Select(c => reported[c.CriterionId]).ToList(),
            ErrorSpans: spans,
            PriorityFix: Text(root, "priorityFix"),
            ModelRewrite: string.IsNullOrWhiteSpace(Text(root, "modelRewrite")) ? null : Text(root, "modelRewrite")), null);

        static string? Text(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    /// <summary>True when the model's snippet is really in the answer. Whitespace is collapsed on both
    /// sides first: a re-wrapped line is the same words, a re-worded one is not.</summary>
    private static bool Quotes(string? responseText, string snippet)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return false;
        var haystack = CollapseWhitespace(responseText);
        return haystack.Contains(CollapseWhitespace(snippet), StringComparison.Ordinal);
    }

    private static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
        if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 3)
            {
                return trimmed[3..end].Trim();
            }
        }
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }
        return trimmed;
    }

    /// <summary>
    /// E2.1 (#151): the binary facts about the answer, decided from the text before the model is asked
    /// for a mark - the same rules the practice drill uses (<see cref="AttemptEngine.EvaluateWritingResponse"/>),
    /// so one answer cannot be "long enough" on one page and short on another.
    /// </summary>
    private sealed record ChecklistResult(
        int WordCount,
        bool MeetsLength,
        bool UsesAllKeywords,
        IReadOnlyList<string> MissingKeywords,
        bool IsEmpty,
        bool AddressesPromptHint)
    {
        public static ChecklistResult For(AiGradingRequest request)
        {
            var words = string.IsNullOrWhiteSpace(request.ResponseText)
                ? 0
                : request.ResponseText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

            var meetsLength = (request.MinWords is null or <= 0 || words >= request.MinWords)
                && (request.MaxWords is null or <= 0 || words <= request.MaxWords);

            var missing = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.RequiredKeywords))
            {
                foreach (var keyword in AttemptEngine.ParseKeywords(request.RequiredKeywords))
                {
                    if (!AttemptEngine.ContainsKeywordStem(request.ResponseText ?? string.Empty, keyword))
                    {
                        missing.Add(keyword);
                    }
                }
            }

            return new ChecklistResult(
                words,
                meetsLength,
                missing.Count == 0,
                missing,
                IsEmpty: words == 0,
                AddressesPromptHint: words > 0);
        }
    }
}
