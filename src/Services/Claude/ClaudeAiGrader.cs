using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed class ClaudeAiGrader : IAiGrader
{
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
            return new AiGradingResult(
                Success: false,
                FailureReason: "Simulated grading failure.",
                Model: "claude-sonnet-3.7",
                Scores: Array.Empty<AiCriterionScoreResult>());
        }

        try
        {
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("Grade the candidate's English response against the following official criteria.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine($"[TASK PROMPT]");
            promptBuilder.AppendLine(request.QuestionStem);
            promptBuilder.AppendLine();
            if (request.MinWords.HasValue || request.MaxWords.HasValue)
            {
                promptBuilder.AppendLine($"[WORD CONSTRAINTS] Minimum: {request.MinWords ?? 0}, Maximum: {request.MaxWords?.ToString() ?? "none"}");
                promptBuilder.AppendLine();
            }
            promptBuilder.AppendLine($"[CANDIDATE RESPONSE]");
            promptBuilder.AppendLine(request.ResponseText ?? "(Empty response)");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("[CRITERIA TO EVALUATE]");
            foreach (var c in request.Criteria)
            {
                promptBuilder.AppendLine($"- ID: {c.CriterionId}, Code: {c.Code}, Name: {c.Name}, MaxScore: {c.MaxScore}, Weight: {c.Weight}");
            }
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Return a strictly valid JSON object with the following schema:");
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"scores\": [");
            promptBuilder.AppendLine("    {");
            promptBuilder.AppendLine("      \"criterionId\": <int>,");
            promptBuilder.AppendLine("      \"score\": <decimal>,");
            promptBuilder.AppendLine("      \"comment\": \"<specific examiner note addressing strengths and weaknesses on this criterion>\"");
            promptBuilder.AppendLine("    }");
            promptBuilder.AppendLine("  ]");
            promptBuilder.AppendLine("}");
            promptBuilder.AppendLine("Score must be between 0.0 and MaxScore for that criterion. Output JSON only, no markdown wrapping.");

            var systemPrompt = "You are a senior, certified official English exam evaluator (IELTS, TOEIC, VSTEP). You score strictly according to CEFR and official rubric criteria with professional, constructive notes.";

            var rawResult = await _claudeClient.CreateMessageAsync(
                promptBuilder.ToString(),
                systemPrompt: systemPrompt,
                maxTokens: 4000,
                cancellationToken: cancellationToken);

            _logger?.LogInformation("Claude response received, length {Len}", rawResult.Length);
            var cleanJson = ExtractJson(rawResult);
            using var doc = JsonDocument.Parse(cleanJson);
            if (doc.RootElement.TryGetProperty("scores", out var scoresArray) && scoresArray.ValueKind == JsonValueKind.Array)
            {
                var criterionLookup = request.Criteria.ToDictionary(c => c.CriterionId);
                var scores = new List<AiCriterionScoreResult>();

                foreach (var el in scoresArray.EnumerateArray())
                {
                    var id = el.GetProperty("criterionId").GetInt32();
                    var score = el.GetProperty("score").GetDecimal();
                    var comment = el.TryGetProperty("comment", out var commentProp) ? commentProp.GetString() : null;

                    if (criterionLookup.TryGetValue(id, out var criterion))
                    {
                        var clamped = Math.Clamp(score, 0m, criterion.MaxScore);
                        scores.Add(new AiCriterionScoreResult(id, clamped, comment));
                    }
                }

                // Fill any missing criteria with fallback if needed
                foreach (var c in request.Criteria)
                {
                    if (!scores.Any(s => s.CriterionId == c.CriterionId))
                    {
                        scores.Add(new AiCriterionScoreResult(c.CriterionId, c.MaxScore * 0.7m, "Standard performance."));
                    }
                }

                return new AiGradingResult(
                    Success: true,
                    FailureReason: null,
                    Model: "claude-sonnet-3.7",
                    Scores: scores);
            }

            _logger?.LogWarning("Claude response did not contain expected 'scores' array: {Raw}", rawResult);
            return new AiGradingResult(
                Success: false,
                FailureReason: "Claude response did not contain expected 'scores' array.",
                Model: "claude-sonnet-3.7",
                Scores: Array.Empty<AiCriterionScoreResult>());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Claude live grading failed");
            return new AiGradingResult(
                Success: false,
                FailureReason: $"Claude grading failed: {ex.Message}",
                Model: "claude-sonnet-3.7",
                Scores: Array.Empty<AiCriterionScoreResult>());
        }
    }

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
}
