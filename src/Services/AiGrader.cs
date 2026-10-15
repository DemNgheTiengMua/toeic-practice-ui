using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

public record AiGradingRequest(
    int FreeResponseId,
    int SkillId,
    int CertId,
    string QuestionStem,
    int? MinWords,
    int? MaxWords,
    string ResponseText,
    IReadOnlyList<GradingCriterionDetail> Criteria,
    int AttemptCount = 1,
    string? AudioPath = null,
    int? DurationMs = null
);

public record GradingCriterionDetail(
    int CriterionId,
    string Code,
    string Name,
    decimal MaxScore,
    decimal Weight
);

public record AiGradingResult(
    bool Success,
    string? FailureReason,
    string? Model,
    IReadOnlyList<AiCriterionScoreResult> Scores,
    string? Transcript = null
);

public record AiCriterionScoreResult(
    int CriterionId,
    decimal Score,
    string? Comment
);

public interface IAiGrader
{
    Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic fake grader for offline execution and testing.
/// Scores responses against criteria within [0, MaxScore] bounds.
/// Supports simulated failures when text or AudioPath contains "[simulate_fail]".
/// When AttemptCount > 1 (e.g. after a retry), the simulated failure resolves.
/// Generates realistic transcripts and pronunciation feedback for spoken responses.
/// </summary>
public sealed class FakeAiGrader : IAiGrader
{
    public const string DefaultModel = "fake-ai-grader-v1";
    public const string SimulateFailMarker = "[simulate_fail]";

    public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
    {
        var hasFailMarker = (request.ResponseText?.Contains(SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true)
            || (request.AudioPath?.Contains(SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true);

        if (hasFailMarker && request.AttemptCount <= 1)
        {
            return Task.FromResult(new AiGradingResult(
                Success: false,
                FailureReason: "Simulated grading failure for test purposes.",
                Model: DefaultModel,
                Scores: Array.Empty<AiCriterionScoreResult>()));
        }

        // Handle spoken responses (AudioPath is present)
        if (!string.IsNullOrEmpty(request.AudioPath))
        {
            var transcript = string.IsNullOrWhiteSpace(request.ResponseText)
                ? $"Transcript for prompt '{request.QuestionStem}': In this response, the speaker clearly articulated the key concepts with appropriate pronunciation and fluent structure."
                : request.ResponseText;

            var speakingScores = new List<AiCriterionScoreResult>();
            foreach (var criterion in request.Criteria)
            {
                var code = criterion.Code.ToUpperInvariant();
                var name = criterion.Name.ToLowerInvariant();
                decimal factor;
                string comment;

                if (code.Contains("PRON") || name.Contains("pronunciation"))
                {
                    factor = 0.85m;
                    comment = "Clear pronunciation overall with accurate vowel length and distinct consonant endings.";
                }
                else if (code.Contains("INT") || name.Contains("intonation") || name.Contains("stress"))
                {
                    factor = 0.8m;
                    comment = "Natural sentence stress and pitch variation across key clauses.";
                }
                else if (code.Contains("GRAM") || name.Contains("grammar") || code.Contains("LR") || name.Contains("vocabulary"))
                {
                    factor = 0.85m;
                    comment = "Accurate grammatical constructions and diverse task-appropriate vocabulary.";
                }
                else if (code.Contains("REL") || name.Contains("relevance") || name.Contains("content") || name.Contains("completeness"))
                {
                    factor = 0.8m;
                    comment = "Directly addresses the question with adequate detail and clear logical cohesion.";
                }
                else
                {
                    factor = 0.8m;
                    comment = $"Solid response addressing the prompt criteria effectively for {criterion.Name}.";
                }

                var raw = Math.Round(criterion.MaxScore * factor, 1, MidpointRounding.AwayFromZero);
                var boundedScore = Math.Clamp(raw, 0m, criterion.MaxScore);
                speakingScores.Add(new AiCriterionScoreResult(criterion.CriterionId, boundedScore, comment));
            }

            return Task.FromResult(new AiGradingResult(
                Success: true,
                FailureReason: null,
                Model: DefaultModel,
                Scores: speakingScores,
                Transcript: transcript));
        }

        var isBlank = string.IsNullOrWhiteSpace(request.ResponseText);
        var words = isBlank
            ? 0
            : request.ResponseText!.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

        var scores = new List<AiCriterionScoreResult>();

        if (isBlank)
        {
            foreach (var criterion in request.Criteria)
            {
                scores.Add(new AiCriterionScoreResult(
                    criterion.CriterionId,
                    0.0m,
                    "No response was submitted for this question."));
            }

            return Task.FromResult(new AiGradingResult(
                Success: true,
                FailureReason: null,
                Model: DefaultModel,
                Scores: scores));
        }

        decimal writingFactor = 0.8m;
        if (request.MinWords.HasValue && request.MinWords.Value > 0)
        {
            if (words < request.MinWords.Value / 2)
            {
                writingFactor = 0.4m;
            }
            else if (words < request.MinWords.Value)
            {
                writingFactor = 0.7m;
            }
            else
            {
                writingFactor = 0.85m;
            }
        }

        if (request.MaxWords.HasValue && request.MaxWords.Value > 0 && words > request.MaxWords.Value)
        {
            writingFactor = Math.Max(0.5m, writingFactor - 0.15m);
        }

        foreach (var criterion in request.Criteria)
        {
            var raw = Math.Round(criterion.MaxScore * writingFactor, 1, MidpointRounding.AwayFromZero);
            var boundedScore = Math.Clamp(raw, 0m, criterion.MaxScore);

            var comment = words >= (request.MinWords ?? 10)
                ? $"Clear response demonstrating good proficiency in {criterion.Name} ({words} words)."
                : $"Adequate attempt for {criterion.Name}, but fell short of the recommended length ({words} words).";

            scores.Add(new AiCriterionScoreResult(
                criterion.CriterionId,
                boundedScore,
                comment));
        }

        return Task.FromResult(new AiGradingResult(
            Success: true,
            FailureReason: null,
            Model: DefaultModel,
            Scores: scores));
    }
}
