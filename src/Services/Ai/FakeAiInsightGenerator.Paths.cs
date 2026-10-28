using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public partial class FakeAiInsightGenerator
{
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
