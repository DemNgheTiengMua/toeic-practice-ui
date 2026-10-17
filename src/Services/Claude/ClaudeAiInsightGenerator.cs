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
    public ClaudeAiInsightGenerator(IClaudeClient claudeClient, ILogger<ClaudeAiInsightGenerator>? logger = null)
    {
        _claudeClient = claudeClient;
        _logger = logger;
    }

    public async Task<AiInsightResult> GenerateInsightAsync(AiInsightGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.SimulateFailure || request.ActivityTitle?.Contains(FakeAiInsightGenerator.SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true)
        {
            return new AiInsightResult(
                Success: false,
                FailureReason: "Simulated insight generation failure.",
                Model: "claude-sonnet-3.7",
                Narrative: null,
                Trend: AiInsightTrend.Flat,
                Recommendations: Array.Empty<GeneratedRecommendation>());
        }

        try
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Analyze the learner's test performance and generate an insightful, highly specific pedagogical diagnostic review.");
            prompt.AppendLine();
            prompt.AppendLine("[LEARNER PROFILE & TARGET]");
            prompt.AppendLine($"Certificate: {request.CertName} (ID: {request.CertId})");
            prompt.AppendLine($"Activity / Source: {request.ActivityTitle}");
            prompt.AppendLine($"Current Band / Score: {request.CurrentBandCode ?? "N/A"} ({request.ScaledScore})");
            prompt.AppendLine($"Target Band / Goal: {request.TargetBandCode ?? "Not specified"} ({request.TargetBandName})");
            var overallAccuracy = request.AnsweredCount > 0 ? (int)Math.Round((double)request.CorrectCount / request.AnsweredCount * 100) : 0;
            prompt.AppendLine($"Overall Accuracy: {request.CorrectCount} correct out of {request.AnsweredCount} answered ({overallAccuracy}%) across {request.TotalQuestions} questions");
            prompt.AppendLine();

            if (request.CategoryAccuracies is { Count: > 0 })
            {
                var sortedCats = request.CategoryAccuracies.OrderBy(c => c.AccuracyPercentage).ToList();
                prompt.AppendLine("[ACCURACY BY QUESTION CATEGORY - SORTED FROM WEAKEST TO STRONGEST]");
                foreach (var cat in sortedCats.Take(10))
                {
                    var tag = cat.AccuracyPercentage < 60 ? "[CRITICAL FOCUS]" : (cat.AccuracyPercentage < 80 ? "[MODERATE GAP]" : "[SOLID STRENGTH]");
                    prompt.AppendLine($"- {tag} {cat.CategoryName} ({cat.CategoryCode}): {cat.Correct}/{cat.Answered} ({cat.AccuracyPercentage}%)");
                }
                prompt.AppendLine();
            }

            if (request.SectionAccuracies is { Count: > 0 })
            {
                var sortedSecs = request.SectionAccuracies.OrderBy(s => s.AccuracyPercentage).ToList();
                prompt.AppendLine("[ACCURACY BY SECTION - SORTED FROM LOWEST ACCURACY]");
                foreach (var sec in sortedSecs.Take(8))
                {
                    prompt.AppendLine($"- {sec.SectionName} ({sec.SectionCode}): {sec.Correct}/{sec.Answered} ({sec.AccuracyPercentage}%)");
                }
                prompt.AppendLine();
            }

            prompt.AppendLine("Return a strictly valid JSON object with the following schema:");
            prompt.AppendLine("{");
            prompt.AppendLine("  \"narrative\": \"<3 to 4 detailed sentences: (1) Current standing and concrete score gap to target band; (2) Top strength category/section cited by name with accuracy; (3) Primary vulnerability category cited with exact error rate and diagnosed linguistic root cause; (4) Immediate tactical focus module to drill next>\",");
            prompt.AppendLine("  \"trend\": \"Improving\" | \"Steady\" | \"Declining\",");
            prompt.AppendLine("  \"recommendations\": [");
            prompt.AppendLine("    {");
            prompt.AppendLine("      \"text\": \"<prescriptive, concrete practice directive citing the exact section/category name and specific study method>\",");
            prompt.AppendLine("      \"severity\": \"High\" | \"Medium\" | \"Low\"");
            prompt.AppendLine("    }");
            prompt.AppendLine("  ]");
            prompt.AppendLine("}");
            prompt.AppendLine("Output JSON only, no markdown formatting.");

            var systemPrompt = "You are an elite AI diagnostic mentor in the ExtraEfficiency language test prep system. Your insights are razor-sharp, pedagogical, deeply grounded in the learner's actual numbers, and never use generic platitudes. Always cite specific categories, sections, and accuracy metrics.";

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

            _logger?.LogWarning("Claude response did not contain expected narrative: {Raw}", rawResult);
            return new AiInsightResult(
                Success: false,
                FailureReason: "Claude response did not contain expected narrative.",
                Model: "claude-sonnet-3.7",
                Narrative: null,
                Trend: AiInsightTrend.Flat,
                Recommendations: Array.Empty<GeneratedRecommendation>());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Claude live insight generation failed");
            return new AiInsightResult(
                Success: false,
                FailureReason: $"Claude insight generation failed: {ex.Message}",
                Model: "claude-sonnet-3.7",
                Narrative: null,
                Trend: AiInsightTrend.Flat,
                Recommendations: Array.Empty<GeneratedRecommendation>());
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
