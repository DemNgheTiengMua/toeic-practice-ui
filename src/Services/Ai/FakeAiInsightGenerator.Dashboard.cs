using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public partial class FakeAiInsightGenerator
{
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

        if (request.ErrorFingerprints is { Count: > 0 })
        {
            var fp = request.ErrorFingerprints[0];
            narrative += $"\n\n**Error Diagnostics**: Analysis of error trap on \"{fp.Stem}\": You chose \"{fp.ChosenOptionText}\" while correct was \"{fp.CorrectOptionText}\". {fp.Explanation}";
        }

        if (request.WritingEvidences is { Count: > 0 })
        {
            var we = request.WritingEvidences[0];
            narrative += $"\n\n**Writing Evaluation**: Prompt \"{we.Stem}\" ({we.WordCount} words): Your response \"{we.ResponseText}\". {we.AiComments}";
        }

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
}
