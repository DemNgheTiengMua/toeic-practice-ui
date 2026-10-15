using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record AiInsightGenerationRequest(
    int UserId,
    int CertId,
    string CertName,
    AiInsightScope Scope,
    int? PracticeSessionId,
    int? AttemptId,
    string ActivityTitle,
    int TotalQuestions,
    int AnsweredCount,
    int CorrectCount,
    int? HintUsedCount,
    int? CorrectWithHintCount,
    string? TargetBandCode,
    string? TargetBandName,
    int? ScaledScore,
    string? CurrentBandCode,
    IReadOnlyList<InsightQuestionDetail> Questions,
    IReadOnlyList<InsightSectionAccuracy> SectionAccuracies,
    IReadOnlyList<InsightCategoryAccuracy> CategoryAccuracies,
    int AttemptCount = 1,
    bool SimulateFailure = false
);

public record InsightQuestionDetail(
    int QuestionId,
    string Stem,
    int SectionId,
    string SectionName,
    int? CategoryId,
    string? CategoryName,
    int? SelectedOptionId,
    string? SelectedOptionLabel,
    string? CorrectOptionLabel,
    bool? IsCorrect,
    bool HintUsed,
    string? Explanation
);

public record InsightSectionAccuracy(
    int SectionId,
    string SectionCode,
    string SectionName,
    int Answered,
    int Correct,
    int AccuracyPercentage
);

public record InsightCategoryAccuracy(
    int CategoryId,
    string CategoryCode,
    string CategoryName,
    int Answered,
    int Correct,
    int AccuracyPercentage
);

public record GeneratedRecommendation(
    string Text,
    RecommendationSeverity Severity,
    int? SkillId = null,
    string? TargetBandCode = null
);

public record AiInsightResult(
    bool Success,
    string? FailureReason,
    string? Model,
    string? Narrative,
    AiInsightTrend? Trend,
    IReadOnlyList<GeneratedRecommendation> Recommendations,
    int? PromptTokens = null,
    int? CompletionTokens = null
);

public interface IAiInsightGenerator
{
    Task<AiInsightResult> GenerateInsightAsync(AiInsightGenerationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic AI insight generator for offline execution and automated tests.
/// Evaluates luck vs competence, target band comparison, trend, and actionable recommendations.
/// Supports simulated failure for testing failure and retry semantics.
/// </summary>
public class FakeAiInsightGenerator : IAiInsightGenerator
{
    public const string DefaultModel = "fake-ai-insight-v1";
    public const string SimulateFailMarker = "[simulate_fail]";

    /// <summary>
    /// Global or instance override to simulate AI insight generation failure in automated tests.
    /// </summary>
    public bool ShouldSimulateFailure { get; set; }

    public virtual Task<AiInsightResult> GenerateInsightAsync(AiInsightGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var hasFailMarker = request.SimulateFailure
            || ShouldSimulateFailure
            || (request.ActivityTitle?.Contains(SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true)
            || request.Questions.Any(q => q.Stem.Contains(SimulateFailMarker, StringComparison.OrdinalIgnoreCase));

        if (hasFailMarker && request.AttemptCount <= 1)
        {
            return Task.FromResult(new AiInsightResult(
                Success: false,
                FailureReason: "Simulated AI insight generation failure for test verification.",
                Model: DefaultModel,
                Narrative: null,
                Trend: null,
                Recommendations: Array.Empty<GeneratedRecommendation>()));
        }

        var totalAnswered = request.AnsweredCount > 0
            ? request.AnsweredCount
            : request.Questions.Count(q => q.SelectedOptionId.HasValue || q.IsCorrect.HasValue);

        var totalCorrect = request.CorrectCount;
        var accuracyPct = totalAnswered > 0 ? (int)Math.Round((double)totalCorrect / totalAnswered * 100) : 0;

        var hintUsed = request.HintUsedCount ?? request.Questions.Count(q => q.HintUsed);
        var correctWithHint = request.CorrectWithHintCount ?? request.Questions.Count(q => q.HintUsed && q.IsCorrect == true);

        // Luck vs Competence analysis:
        // Distinguish solid competence from hint-assisted and fortuitous guesses on historically weak categories
        var luckyCategoryQuestions = request.Questions
            .Where(q => q.IsCorrect == true && !q.HintUsed && q.CategoryId.HasValue &&
                        request.CategoryAccuracies.Any(ca => ca.CategoryId == q.CategoryId.Value && ca.Answered >= 2 && ca.AccuracyPercentage < 45))
            .ToList();

        var luckyGuessCount = luckyCategoryQuestions.Count;
        var solidCompetenceCount = Math.Max(0, totalCorrect - correctWithHint - luckyGuessCount);

        string luckAnalysis;
        if (correctWithHint > 0 && luckyGuessCount > 0)
        {
            luckAnalysis = $"Of your {totalCorrect} correct answers, {solidCompetenceCount} reflect genuine unassisted competence. However, {correctWithHint} correct answers relied on hints, and {luckyGuessCount} occurred in historically challenging question categories (under 45% prior accuracy), pointing to fortunate guesses rather than durable mastery.";
        }
        else if (correctWithHint > 0)
        {
            luckAnalysis = $"You answered {totalCorrect} questions correctly, with {solidCompetenceCount} demonstrating unassisted competence. {correctWithHint} correct answers required hints; try drilling without hints next time to confirm independent recall.";
        }
        else if (luckyGuessCount > 0)
        {
            luckAnalysis = $"You answered {totalCorrect} questions correctly ({solidCompetenceCount} solid competence). {luckyGuessCount} correct answers were on question types where your historical accuracy is below 45%, suggesting fortunate guessing that needs further reinforcement.";
        }
        else if (totalCorrect > 0)
        {
            luckAnalysis = $"All {totalCorrect} correct answers show consistent competence without hint reliance or speculative guesses.";
        }
        else
        {
            luckAnalysis = "None of the answered questions were correct. Focus on core question patterns to build initial competence.";
        }

        // Target Band comparison
        string targetBandText;
        if (!string.IsNullOrEmpty(request.TargetBandCode))
        {
            var targetName = !string.IsNullOrEmpty(request.TargetBandName)
                ? $"{request.TargetBandCode} ({request.TargetBandName})"
                : request.TargetBandCode;

            if (accuracyPct >= 80)
            {
                targetBandText = $"Your accuracy of {accuracyPct}% comfortably meets the proficiency benchmark for target band {targetName}. Keep maintaining this standard on full-length sets.";
            }
            else if (accuracyPct >= 65)
            {
                targetBandText = $"Your accuracy of {accuracyPct}% puts you within striking distance of target band {targetName}. Eliminating unforced errors in missed categories will secure the required score.";
            }
            else
            {
                targetBandText = $"Your accuracy of {accuracyPct}% indicates a gap before reaching target band {targetName}. Targeted practice on fundamental rules is recommended before re-testing.";
            }
        }
        else
        {
            if (accuracyPct >= 80)
            {
                targetBandText = $"Session accuracy of {accuracyPct}% demonstrates strong command, placing you in an advanced tier. Set a target band in your profile to track precise CEFR milestones.";
            }
            else if (accuracyPct >= 60)
            {
                targetBandText = $"Session accuracy of {accuracyPct}% reflects solid intermediate proficiency with clear opportunities for refinement.";
            }
            else
            {
                targetBandText = $"Session accuracy of {accuracyPct}% indicates foundational stage. Consistent targeted practice will help build question familiarity.";
            }
        }

        // Trend calculation
        AiInsightTrend trend;
        var totalHistoricalAnswered = request.SectionAccuracies.Sum(s => s.Answered);
        if (totalHistoricalAnswered >= 5)
        {
            var totalHistoricalCorrect = request.SectionAccuracies.Sum(s => s.Correct);
            var historicalPct = (int)Math.Round((double)totalHistoricalCorrect / totalHistoricalAnswered * 100);

            if (accuracyPct >= historicalPct + 5)
            {
                trend = AiInsightTrend.Improving;
            }
            else if (accuracyPct <= historicalPct - 5)
            {
                trend = AiInsightTrend.Declining;
            }
            else
            {
                trend = AiInsightTrend.Flat;
            }
        }
        else
        {
            if (accuracyPct >= 75) trend = AiInsightTrend.Improving;
            else if (accuracyPct >= 50) trend = AiInsightTrend.Flat;
            else trend = AiInsightTrend.Declining;
        }

        // Actionable Recommendations
        var recommendations = new List<GeneratedRecommendation>();

        var missedCategories = request.Questions
            .Where(q => q.IsCorrect == false && !string.IsNullOrEmpty(q.CategoryName))
            .GroupBy(q => new { q.CategoryId, q.CategoryName })
            .OrderByDescending(g => g.Count())
            .ToList();

        if (missedCategories.Count > 0)
        {
            var top = missedCategories[0];
            recommendations.Add(new GeneratedRecommendation(
                $"Review {top.Key.CategoryName} rules and explanations to resolve recurring difficulties ({top.Count()} missed).",
                RecommendationSeverity.Major,
                TargetBandCode: request.TargetBandCode));
        }

        if (missedCategories.Count > 1)
        {
            var second = missedCategories[1];
            recommendations.Add(new GeneratedRecommendation(
                $"Practice additional questions in {second.Key.CategoryName} to reinforce pattern recognition.",
                RecommendationSeverity.Minor,
                TargetBandCode: request.TargetBandCode));
        }

        if (hintUsed > 0)
        {
            recommendations.Add(new GeneratedRecommendation(
                $"Complete the next practice set without using hints to establish an unassisted baseline.",
                RecommendationSeverity.Info,
                TargetBandCode: request.TargetBandCode));
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(new GeneratedRecommendation(
                "Continue with timed mock exams to test question pacing and endurance under exam conditions.",
                RecommendationSeverity.Info,
                TargetBandCode: request.TargetBandCode));
        }

        // Build full prose narrative
        var narrative = $"**Performance Summary**: {request.ActivityTitle} completed with {totalCorrect}/{totalAnswered} correct ({accuracyPct}%).\n\n"
            + $"**Competence vs. Luck**: {luckAnalysis}\n\n"
            + $"**Target Band Alignment**: {targetBandText}";

        return Task.FromResult(new AiInsightResult(
            Success: true,
            FailureReason: null,
            Model: DefaultModel,
            Narrative: narrative,
            Trend: trend,
            Recommendations: recommendations,
            PromptTokens: 140,
            CompletionTokens: 110));
    }
}
