using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class ClaudeAiInsightGenerator : IAiInsightGenerator
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
            var systemPrompt = "You are an elite AI diagnostic mentor in the ExtraEfficiency language test prep system. Your insights are razor-sharp, pedagogical, deeply grounded in the learner's actual numbers, and never use generic platitudes. Always cite specific categories, sections, and accuracy metrics.";

            var rawResult = await _claudeClient.CreateMessageAsync(
                BuildPrompt(request),
                systemPrompt: systemPrompt,
                maxTokens: 4000,
                cancellationToken: cancellationToken);

            return ParseResponse(rawResult, request);
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
}
