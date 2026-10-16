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

public sealed class ClaudeAiInsightGenerator : IAiInsightGenerator
{
    private readonly IClaudeClient _claudeClient;
    private readonly ILogger<ClaudeAiInsightGenerator>? _logger;
    private readonly FakeAiInsightGenerator _fallbackGenerator;

    public ClaudeAiInsightGenerator(IClaudeClient claudeClient, ILogger<ClaudeAiInsightGenerator>? logger = null)
    {
        _claudeClient = claudeClient;
        _logger = logger;
        _fallbackGenerator = new FakeAiInsightGenerator();
    }

    public async Task<AiInsightResult> GenerateInsightAsync(AiInsightGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.SimulateFailure || request.ActivityTitle?.Contains(FakeAiInsightGenerator.SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _fallbackGenerator.GenerateInsightAsync(request, cancellationToken);
        }

        try
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Analyze the learner's test performance and generate an insightful, encouraging pedagogical review.");
            prompt.AppendLine($"[CONTEXT]");
            prompt.AppendLine($"Certificate: {request.CertName} (ID: {request.CertId})");
            prompt.AppendLine($"Activity: {request.ActivityTitle}");
            prompt.AppendLine($"Current Band / Score: {request.CurrentBandCode ?? "N/A"} ({request.ScaledScore})");
            prompt.AppendLine($"Target Band: {request.TargetBandCode ?? "Not specified"} ({request.TargetBandName})");
            prompt.AppendLine($"Accuracy: {request.CorrectCount} correct out of {request.AnsweredCount} answered (Total: {request.TotalQuestions})");
            prompt.AppendLine();

            if (request.CategoryAccuracies is { Count: > 0 })
            {
                prompt.AppendLine("[ACCURACY BY QUESTION CATEGORY]");
                foreach (var cat in request.CategoryAccuracies.Take(8))
                {
                    prompt.AppendLine($"- {cat.CategoryName} ({cat.CategoryCode}): {cat.Correct}/{cat.Answered} ({cat.AccuracyPercentage}%)");
                }
                prompt.AppendLine();
            }

            if (request.SectionAccuracies is { Count: > 0 })
            {
                prompt.AppendLine("[ACCURACY BY SECTION]");
                foreach (var sec in request.SectionAccuracies.Take(8))
                {
                    prompt.AppendLine($"- {sec.SectionName} ({sec.SectionCode}): {sec.Correct}/{sec.Answered} ({sec.AccuracyPercentage}%)");
                }
                prompt.AppendLine();
            }

            prompt.AppendLine("Return a strictly valid JSON object with the following schema:");
            prompt.AppendLine("{");
            prompt.AppendLine("  \"narrative\": \"<2 to 3 sentences summarizing performance, strengths, and specific areas requiring focus>\",");
            prompt.AppendLine("  \"trend\": \"Improving\" | \"Steady\" | \"Declining\",");
            prompt.AppendLine("  \"recommendations\": [");
            prompt.AppendLine("    {");
            prompt.AppendLine("      \"text\": \"<concrete, actionable practice recommendation>\",");
            prompt.AppendLine("      \"severity\": \"High\" | \"Medium\" | \"Low\"");
            prompt.AppendLine("    }");
            prompt.AppendLine("  ]");
            prompt.AppendLine("}");
            prompt.AppendLine("Output JSON only, no markdown formatting.");

            var systemPrompt = "You are an AI diagnostic mentor in the extra efficient exam prep system. Your insights are clear, precise, and actionable, addressing what the learner did well and where the exact score gap is.";

            var rawResult = await _claudeClient.CreateMessageAsync(
                prompt.ToString(),
                systemPrompt: systemPrompt,
                maxTokens: 4000,
                cancellationToken: cancellationToken);

            var cleanJson = ExtractJson(rawResult);
            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            var narrative = root.TryGetProperty("narrative", out var nProp) ? nProp.GetString() : null;
            AiInsightTrend trend = AiInsightTrend.Flat;
            if (root.TryGetProperty("trend", out var tProp))
            {
                var tStr = tProp.GetString()?.ToLowerInvariant();
                if (tStr == "improving") trend = AiInsightTrend.Improving;
                else if (tStr == "declining") trend = AiInsightTrend.Declining;
            }

            var recommendations = new List<GeneratedRecommendation>();
            if (root.TryGetProperty("recommendations", out var rArray) && rArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rArray.EnumerateArray())
                {
                    var text = r.GetProperty("text").GetString() ?? "Practice recommendation";
                    var sevStr = r.TryGetProperty("severity", out var sProp) ? sProp.GetString()?.ToLowerInvariant() : "info";
                    var severity = (sevStr == "major" || sevStr == "high")
                        ? RecommendationSeverity.Major 
                        : (sevStr == "minor" || sevStr == "medium")
                            ? RecommendationSeverity.Minor 
                            : RecommendationSeverity.Info;
                    recommendations.Add(new GeneratedRecommendation(text, severity, TargetBandCode: request.TargetBandCode));
                }
            }

            if (!string.IsNullOrEmpty(narrative))
            {
                return new AiInsightResult(
                    Success: true,
                    FailureReason: null,
                    Model: "claude-sonnet-3.7",
                    Narrative: narrative,
                    Trend: trend,
                    Recommendations: recommendations);
            }

            return await _fallbackGenerator.GenerateInsightAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Claude live insight generation failed, falling back to deterministic generator");
            return await _fallbackGenerator.GenerateInsightAsync(request, cancellationToken);
        }
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 7) return trimmed[7..end].Trim();
        }
        if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 3) return trimmed[3..end].Trim();
        }
        var first = trimmed.IndexOf('{');
        var last = trimmed.LastIndexOf('}');
        if (first >= 0 && last > first) return trimmed[first..(last + 1)];
        return trimmed;
    }
}
