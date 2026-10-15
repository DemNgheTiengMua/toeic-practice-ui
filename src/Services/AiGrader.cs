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
    IReadOnlyList<GradingCriterionDetail> Criteria
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
    IReadOnlyList<AiCriterionScoreResult> Scores
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
/// Supports simulated failures when text contains "[simulate_fail]".
/// </summary>
public sealed class FakeAiGrader : IAiGrader
{
    public const string DefaultModel = "fake-ai-grader-v1";
    public const string SimulateFailMarker = "[simulate_fail]";

    public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ResponseText?.Contains(SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true)
        {
            return Task.FromResult(new AiGradingResult(
                Success: false,
                FailureReason: "Simulated grading failure for test purposes.",
                Model: DefaultModel,
                Scores: Array.Empty<AiCriterionScoreResult>()));
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

        decimal factor = 0.8m;
        if (request.MinWords.HasValue && request.MinWords.Value > 0)
        {
            if (words < request.MinWords.Value / 2)
            {
                factor = 0.4m;
            }
            else if (words < request.MinWords.Value)
            {
                factor = 0.7m;
            }
            else
            {
                factor = 0.85m;
            }
        }

        if (request.MaxWords.HasValue && request.MaxWords.Value > 0 && words > request.MaxWords.Value)
        {
            factor = Math.Max(0.5m, factor - 0.15m);
        }

        foreach (var criterion in request.Criteria)
        {
            var raw = Math.Round(criterion.MaxScore * factor, 1, MidpointRounding.AwayFromZero);
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
