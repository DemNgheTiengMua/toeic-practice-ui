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

public sealed partial class ClaudeAiInsightGenerator
{
    private AiInsightResult ParseResponse(string rawResult, AiInsightGenerationRequest request)
    {
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
