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
    bool SimulateFailure = false,
    PreviousInsightSummary? PreviousInsight = null,
    int? LearnerPathId = null
);

public record PreviousInsightSummary(
    int InsightId,
    string? Narrative,
    AiInsightTrend? Trend,
    DateTimeOffset CreatedAt
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

        if (request.Scope == AiInsightScope.Dashboard)
        {
            return GenerateDashboardInsight(request, totalAnswered, totalCorrect, accuracyPct);
        }

        if (request.Scope == AiInsightScope.Path)
        {
            return GeneratePathInsight(request);
        }

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

    private Task<AiInsightResult> GenerateDashboardInsight(
        AiInsightGenerationRequest request,
        int totalAnswered,
        int totalCorrect,
        int accuracyPct)
    {
        // 1. Trend calculation and comparison with previous insight
        AiInsightTrend trend;
        string trendComparison;

        if (request.PreviousInsight != null)
        {
            var prevDate = request.PreviousInsight.CreatedAt.ToString("MMM d");
            var prevTrendStr = request.PreviousInsight.Trend?.ToCode() ?? "flat";

            int? prevAccuracy = null;
            if (!string.IsNullOrEmpty(request.PreviousInsight.Narrative))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    request.PreviousInsight.Narrative,
                    @"overall accuracy of (\d+)%",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    match = System.Text.RegularExpressions.Regex.Match(request.PreviousInsight.Narrative, @"(\d+)%");
                }
                if (match.Success && int.TryParse(match.Groups[1].Value, out var pa))
                {
                    prevAccuracy = pa;
                }
            }

            if (prevAccuracy.HasValue)
            {
                if (accuracyPct >= prevAccuracy.Value + 3)
                {
                    trend = AiInsightTrend.Improving;
                    trendComparison = $"Compared to your previous review on {prevDate} (trend was {prevTrendStr}), your performance is **improving**, with overall accuracy rising from {prevAccuracy.Value}% to {accuracyPct}%.";
                }
                else if (accuracyPct <= prevAccuracy.Value - 3)
                {
                    trend = AiInsightTrend.Declining;
                    trendComparison = $"Compared to your previous review on {prevDate} (trend was {prevTrendStr}), your performance has **declined** from {prevAccuracy.Value}% to {accuracyPct}%, indicating areas needing immediate reinforcement.";
                }
                else
                {
                    trend = AiInsightTrend.Flat;
                    trendComparison = $"Compared to your previous review on {prevDate} (trend was {prevTrendStr}), your performance is **holding steady** at {accuracyPct}%.";
                }
            }
            else
            {
                if (accuracyPct >= 75)
                {
                    trend = AiInsightTrend.Improving;
                    trendComparison = $"Compared to your previous review on {prevDate} (trend was {prevTrendStr}), your performance is **improving** with strong mastery at {accuracyPct}%.";
                }
                else if (accuracyPct >= 50)
                {
                    trend = AiInsightTrend.Flat;
                    trendComparison = $"Compared to your previous review on {prevDate} (trend was {prevTrendStr}), your performance is **holding steady** at {accuracyPct}%.";
                }
                else
                {
                    trend = AiInsightTrend.Declining;
                    trendComparison = $"Compared to your previous review on {prevDate} (trend was {prevTrendStr}), your performance is **declining** at {accuracyPct}%.";
                }
            }
        }
        else
        {
            if (totalAnswered == 0) trend = AiInsightTrend.Flat;
            else if (accuracyPct >= 75) trend = AiInsightTrend.Improving;
            else if (accuracyPct >= 50) trend = AiInsightTrend.Flat;
            else trend = AiInsightTrend.Declining;

            trendComparison = $"Initial diagnostic baseline established across your practice and exam history with an overall accuracy of {accuracyPct}%.";
        }

        // 2. Section Breakdown
        var attemptedSections = request.SectionAccuracies.Where(s => s.Answered > 0).ToList();
        var weakestSection = attemptedSections.OrderBy(s => s.AccuracyPercentage).ThenByDescending(s => s.Answered).FirstOrDefault();
        var strongestSection = attemptedSections.OrderByDescending(s => s.AccuracyPercentage).ThenByDescending(s => s.Answered).FirstOrDefault();

        string sectionAnalysis;
        if (attemptedSections.Count > 0)
        {
            if (weakestSection != null && strongestSection != null && weakestSection.SectionId != strongestSection.SectionId)
            {
                sectionAnalysis = $"Your strongest performance is in **{strongestSection.SectionName}** ({strongestSection.AccuracyPercentage}% across {strongestSection.Answered} questions), while **{weakestSection.SectionName}** represents your biggest area for growth ({weakestSection.AccuracyPercentage}% across {weakestSection.Answered} questions).";
            }
            else if (weakestSection != null)
            {
                sectionAnalysis = $"Performance recorded in **{weakestSection.SectionName}** with {weakestSection.AccuracyPercentage}% accuracy ({weakestSection.Correct}/{weakestSection.Answered} correct).";
            }
            else
            {
                sectionAnalysis = "Continue practicing across sections to uncover detailed patterns.";
            }
        }
        else
        {
            sectionAnalysis = "No questions have been completed yet for this certificate. Take a practice session or placement test to begin.";
        }

        // 3. Target Band Alignment
        string targetBandText;
        if (!string.IsNullOrEmpty(request.TargetBandCode))
        {
            var targetName = !string.IsNullOrEmpty(request.TargetBandName)
                ? $"{request.TargetBandCode} ({request.TargetBandName})"
                : request.TargetBandCode;

            if (accuracyPct >= 80)
            {
                targetBandText = $"Your accuracy of {accuracyPct}% comfortably meets the proficiency benchmark for target band {targetName}. Keep maintaining this standard on full-length mock exams.";
            }
            else if (accuracyPct >= 65)
            {
                targetBandText = $"Your accuracy of {accuracyPct}% puts you within striking distance of target band {targetName}. Eliminating errors in {weakestSection?.SectionName ?? "missed sections"} will help secure your target band.";
            }
            else
            {
                targetBandText = $"Your accuracy of {accuracyPct}% indicates a gap before reaching target band {targetName}. Targeted drilling in fundamental rules is recommended.";
            }
        }
        else
        {
            targetBandText = $"With overall accuracy of {accuracyPct}%, set a target band in your profile to track precise CEFR milestone progression.";
        }

        // 4. Actionable Recommendations
        var recommendations = new List<GeneratedRecommendation>();
        if (weakestSection != null)
        {
            var text = weakestSection.AccuracyPercentage < 100
                ? $"Drill questions in {weakestSection.SectionName} to improve your lowest accuracy area ({weakestSection.AccuracyPercentage}%)."
                : $"Maintain your high performance in {weakestSection.SectionName} ({weakestSection.AccuracyPercentage}%) with mixed practice drills.";

            recommendations.Add(new GeneratedRecommendation(
                text,
                RecommendationSeverity.Major,
                TargetBandCode: request.TargetBandCode));
        }

        var missedCategories = request.CategoryAccuracies
            .Where(c => c.Answered > 0 && c.AccuracyPercentage < 70)
            .OrderBy(c => c.AccuracyPercentage)
            .ToList();

        if (missedCategories.Count > 0)
        {
            var top = missedCategories[0];
            recommendations.Add(new GeneratedRecommendation(
                $"Review explanations and question rules for {top.CategoryName} ({top.AccuracyPercentage}% accuracy).",
                RecommendationSeverity.Minor,
                TargetBandCode: request.TargetBandCode));
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(new GeneratedRecommendation(
                "Complete a practice session to begin gathering accuracy data across skills.",
                RecommendationSeverity.Info,
                TargetBandCode: request.TargetBandCode));
        }

        if (recommendations.Count < 2)
        {
            recommendations.Add(new GeneratedRecommendation(
                "Take a timed mock exam to test overall endurance and pacing under full test conditions.",
                RecommendationSeverity.Info,
                TargetBandCode: request.TargetBandCode));
        }

        // 5. Full prose narrative
        var narrative = $"**Performance Summary**: Overall accuracy of {accuracyPct}% ({totalCorrect}/{totalAnswered} questions answered across {request.CertName}).\n\n"
            + $"**Progress & Trend**: {trendComparison}\n\n"
            + $"**Section Breakdown**: {sectionAnalysis}\n\n"
            + $"**Target Band Alignment**: {targetBandText}";

        return Task.FromResult(new AiInsightResult(
            Success: true,
            FailureReason: null,
            Model: DefaultModel,
            Narrative: narrative,
            Trend: trend,
            Recommendations: recommendations,
            PromptTokens: 160,
            CompletionTokens: 130));
    }

    private Task<AiInsightResult> GeneratePathInsight(AiInsightGenerationRequest request)
    {
        var totalSteps = request.TotalQuestions;
        var completedSteps = request.AnsweredCount;
        var completionPct = totalSteps > 0 ? (int)Math.Round((double)completedSteps / totalSteps * 100) : 0;

        AiInsightTrend trend;
        string trendNarrative;

        if (completedSteps == 0)
        {
            trend = AiInsightTrend.Flat;
            trendNarrative = $"You have initiated your **{request.CertName}** path toward **{request.TargetBandName ?? request.TargetBandCode ?? "your target band"}**. Complete your initial module steps to establish a diagnostic trajectory.";
        }
        else
        {
            var attemptedSections = request.SectionAccuracies.Where(s => s.Answered > 0).ToList();
            var avgAccuracy = attemptedSections.Count > 0 ? (int)Math.Round(attemptedSections.Average(s => s.AccuracyPercentage)) : 75;

            if (avgAccuracy >= 75 || completionPct >= 50)
            {
                trend = AiInsightTrend.Improving;
                trendNarrative = $"You have completed **{completedSteps} of {totalSteps} steps** ({completionPct}%) with strong accuracy (averaging {avgAccuracy}%). Your progress is **improving** and tracking well toward target band **{request.TargetBandName ?? request.TargetBandCode}**.";
            }
            else if (avgAccuracy >= 50)
            {
                trend = AiInsightTrend.Flat;
                trendNarrative = $"You have completed **{completedSteps} of {totalSteps} steps** ({completionPct}%) with steady accuracy (averaging {avgAccuracy}%). Progress is **flat**; reinforce weaker categories in upcoming modules to build momentum.";
            }
            else
            {
                trend = AiInsightTrend.Declining;
                trendNarrative = $"You have completed **{completedSteps} of {totalSteps} steps** ({completionPct}%), but accuracy is averaging {avgAccuracy}%. Trend is currently **declining**; review module fundamentals before advancing to the next steps.";
            }
        }

        var recommendations = new List<GeneratedRecommendation>();

        var weakestSection = request.SectionAccuracies.Where(s => s.Answered > 0).OrderBy(s => s.AccuracyPercentage).FirstOrDefault();
        if (weakestSection != null && weakestSection.AccuracyPercentage < 80)
        {
            recommendations.Add(new GeneratedRecommendation(
                $"Prioritize practice in {weakestSection.SectionName} ({weakestSection.AccuracyPercentage}% accuracy) in upcoming module steps.",
                RecommendationSeverity.Major,
                TargetBandCode: request.TargetBandCode));
        }
        else
        {
            recommendations.Add(new GeneratedRecommendation(
                $"Continue sequential module progression toward target band {request.TargetBandName ?? request.TargetBandCode}.",
                RecommendationSeverity.Info,
                TargetBandCode: request.TargetBandCode));
        }

        var weakestCategory = request.CategoryAccuracies.Where(c => c.Answered > 0 && c.AccuracyPercentage < 70).OrderBy(c => c.AccuracyPercentage).FirstOrDefault();
        if (weakestCategory != null)
        {
            recommendations.Add(new GeneratedRecommendation(
                $"Focus on {weakestCategory.CategoryName} question types to eliminate unforced errors.",
                RecommendationSeverity.Minor,
                TargetBandCode: request.TargetBandCode));
        }

        var narrative = $"{trendNarrative}\n\nSteps are sequenced by template order to systematically build proficiency.";

        return Task.FromResult(new AiInsightResult(
            Success: true,
            FailureReason: null,
            Model: DefaultModel,
            Narrative: narrative,
            Trend: trend,
            Recommendations: recommendations,
            PromptTokens: 150,
            CompletionTokens: 120));
    }
}
