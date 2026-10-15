using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BusinessObjects;
using Services;
using Xunit;

namespace Services.Tests;

public sealed class FakeAiInsightGeneratorTests
{
    [Fact]
    public async Task Generates_deterministic_narrative_luck_analysis_and_recommendations()
    {
        var generator = new FakeAiInsightGenerator();

        var questions = new List<InsightQuestionDetail>
        {
            new(1, "Question 1", 10, "Incomplete Sentences", 101, "Prepositions", 1, "A", "A", true, false, "Preposition rule"),
            new(2, "Question 2", 10, "Incomplete Sentences", 101, "Prepositions", 2, "B", "A", false, false, "Preposition rule"),
            new(3, "Question 3", 10, "Incomplete Sentences", 102, "Conjunctions", 3, "A", "A", true, true, "Conjunction hint used"),
            new(4, "Question 4", 10, "Incomplete Sentences", 103, "Verb Tense", 4, "B", "B", true, false, "Verb tense rule"),
        };

        var sectionAccuracies = new List<InsightSectionAccuracy>
        {
            new(10, "P5", "Incomplete Sentences", 20, 10, 50)
        };

        var categoryAccuracies = new List<InsightCategoryAccuracy>
        {
            new(101, "PREP", "Prepositions", 10, 3, 30), // historically weak
            new(102, "CONJ", "Conjunctions", 10, 6, 60),
            new(103, "TENSE", "Verb Tense", 10, 8, 80)
        };

        var request = new AiInsightGenerationRequest(
            UserId: 1,
            CertId: 1,
            CertName: "TOEIC",
            Scope: AiInsightScope.PracticeSession,
            PracticeSessionId: 100,
            AttemptId: null,
            ActivityTitle: "Incomplete Sentences",
            TotalQuestions: 4,
            AnsweredCount: 4,
            CorrectCount: 3,
            HintUsedCount: 1,
            CorrectWithHintCount: 1,
            TargetBandCode: "B2",
            TargetBandName: "Vantage",
            ScaledScore: null,
            CurrentBandCode: null,
            Questions: questions,
            SectionAccuracies: sectionAccuracies,
            CategoryAccuracies: categoryAccuracies);

        var result = await generator.GenerateInsightAsync(request);

        Assert.True(result.Success);
        Assert.Equal(FakeAiInsightGenerator.DefaultModel, result.Model);
        Assert.NotNull(result.Narrative);
        Assert.Contains("Performance Summary", result.Narrative);
        Assert.Contains("Competence vs. Luck", result.Narrative);
        Assert.Contains("Target Band Alignment", result.Narrative);
        Assert.Contains("target band B2", result.Narrative);

        // Trend: current 75% vs historical 50% (+25%) -> Improving
        Assert.Equal(AiInsightTrend.Improving, result.Trend);

        // Recommendations generated
        Assert.NotEmpty(result.Recommendations);
        Assert.Contains(result.Recommendations, r => r.Severity == RecommendationSeverity.Major);
        Assert.Contains(result.Recommendations, r => r.Severity == RecommendationSeverity.Info);
    }

    [Fact]
    public async Task Trend_is_declining_when_accuracy_drops_below_history()
    {
        var generator = new FakeAiInsightGenerator();

        var sectionAccuracies = new List<InsightSectionAccuracy>
        {
            new(10, "P5", "Incomplete Sentences", 30, 27, 90)
        };

        var request = new AiInsightGenerationRequest(
            UserId: 1,
            CertId: 1,
            CertName: "TOEIC",
            Scope: AiInsightScope.PracticeSession,
            PracticeSessionId: 101,
            AttemptId: null,
            ActivityTitle: "Incomplete Sentences",
            TotalQuestions: 10,
            AnsweredCount: 10,
            CorrectCount: 5, // 50% vs 90% historical -> Declining
            HintUsedCount: 0,
            CorrectWithHintCount: 0,
            TargetBandCode: null,
            TargetBandName: null,
            ScaledScore: null,
            CurrentBandCode: null,
            Questions: Array.Empty<InsightQuestionDetail>(),
            SectionAccuracies: sectionAccuracies,
            CategoryAccuracies: Array.Empty<InsightCategoryAccuracy>());

        var result = await generator.GenerateInsightAsync(request);

        Assert.True(result.Success);
        Assert.Equal(AiInsightTrend.Declining, result.Trend);
    }

    [Fact]
    public async Task Simulated_failure_fails_on_first_attempt_and_resolves_on_retry()
    {
        var generator = new FakeAiInsightGenerator();

        var questions = new List<InsightQuestionDetail>
        {
            new(1, "Question stem with [simulate_fail] marker", 10, "P5", null, null, 1, "A", "A", true, false, null)
        };

        var requestAttempt1 = new AiInsightGenerationRequest(
            UserId: 1,
            CertId: 1,
            CertName: "TOEIC",
            Scope: AiInsightScope.PracticeSession,
            PracticeSessionId: 102,
            AttemptId: null,
            ActivityTitle: "Practice with [simulate_fail]",
            TotalQuestions: 1,
            AnsweredCount: 1,
            CorrectCount: 1,
            HintUsedCount: 0,
            CorrectWithHintCount: 0,
            TargetBandCode: null,
            TargetBandName: null,
            ScaledScore: null,
            CurrentBandCode: null,
            Questions: questions,
            SectionAccuracies: Array.Empty<InsightSectionAccuracy>(),
            CategoryAccuracies: Array.Empty<InsightCategoryAccuracy>(),
            AttemptCount: 1);

        var resultAttempt1 = await generator.GenerateInsightAsync(requestAttempt1);

        Assert.False(resultAttempt1.Success);
        Assert.NotNull(resultAttempt1.FailureReason);
        Assert.Null(resultAttempt1.Narrative);

        // Attempt 2 (Retry)
        var requestAttempt2 = requestAttempt1 with { AttemptCount = 2 };
        var resultAttempt2 = await generator.GenerateInsightAsync(requestAttempt2);

        Assert.True(resultAttempt2.Success);
        Assert.NotNull(resultAttempt2.Narrative);
        Assert.Equal(FakeAiInsightGenerator.DefaultModel, resultAttempt2.Model);
    }

    [Fact]
    public async Task Global_should_simulate_failure_flag_triggers_failure()
    {
        var generator = new FakeAiInsightGenerator { ShouldSimulateFailure = true };

        var request = new AiInsightGenerationRequest(
            UserId: 1,
            CertId: 1,
            CertName: "TOEIC",
            Scope: AiInsightScope.PracticeSession,
            PracticeSessionId: 103,
            AttemptId: null,
            ActivityTitle: "Practice",
            TotalQuestions: 5,
            AnsweredCount: 5,
            CorrectCount: 4,
            HintUsedCount: 0,
            CorrectWithHintCount: 0,
            TargetBandCode: "B2",
            TargetBandName: "Vantage",
            ScaledScore: null,
            CurrentBandCode: null,
            Questions: Array.Empty<InsightQuestionDetail>(),
            SectionAccuracies: Array.Empty<InsightSectionAccuracy>(),
            CategoryAccuracies: Array.Empty<InsightCategoryAccuracy>(),
            AttemptCount: 1);

        var result = await generator.GenerateInsightAsync(request);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task Dashboard_scope_generates_baseline_narrative_when_no_previous_insight()
    {
        var generator = new FakeAiInsightGenerator();

        var sections = new List<InsightSectionAccuracy>
        {
            new(1, "P1", "Photographs", 20, 18, 90),
            new(2, "P5", "Incomplete Sentences", 20, 10, 50)
        };

        var categories = new List<InsightCategoryAccuracy>
        {
            new(101, "PREP", "Prepositions", 10, 4, 40)
        };

        var request = new AiInsightGenerationRequest(
            UserId: 1,
            CertId: 1,
            CertName: "TOEIC",
            Scope: AiInsightScope.Dashboard,
            PracticeSessionId: null,
            AttemptId: null,
            ActivityTitle: "TOEIC Dashboard",
            TotalQuestions: 40,
            AnsweredCount: 40,
            CorrectCount: 28,
            HintUsedCount: null,
            CorrectWithHintCount: null,
            TargetBandCode: "B2",
            TargetBandName: "Vantage",
            ScaledScore: 750,
            CurrentBandCode: "B2",
            Questions: Array.Empty<InsightQuestionDetail>(),
            SectionAccuracies: sections,
            CategoryAccuracies: categories,
            PreviousInsight: null);

        var result = await generator.GenerateInsightAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Narrative);
        Assert.Contains("Performance Summary", result.Narrative);
        Assert.Contains("Progress & Trend", result.Narrative);
        Assert.Contains("Initial diagnostic baseline", result.Narrative);
        Assert.Contains("Section Breakdown", result.Narrative);
        Assert.Contains("Target Band Alignment", result.Narrative);
        Assert.NotEmpty(result.Recommendations);
        Assert.Contains(result.Recommendations, r => r.Severity == RecommendationSeverity.Major);
    }

    [Fact]
    public async Task Dashboard_scope_compares_against_previous_insight_and_detects_improving_trend()
    {
        var generator = new FakeAiInsightGenerator();

        var sections = new List<InsightSectionAccuracy>
        {
            new(1, "P1", "Photographs", 20, 18, 90),
            new(2, "P5", "Incomplete Sentences", 20, 16, 80)
        };

        var previousInsight = new PreviousInsightSummary(
            InsightId: 99,
            Narrative: "**Performance Summary**: Overall accuracy of 60% (24/40 questions answered across TOEIC).",
            Trend: AiInsightTrend.Flat,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-7));

        var request = new AiInsightGenerationRequest(
            UserId: 1,
            CertId: 1,
            CertName: "TOEIC",
            Scope: AiInsightScope.Dashboard,
            PracticeSessionId: null,
            AttemptId: null,
            ActivityTitle: "TOEIC Dashboard",
            TotalQuestions: 40,
            AnsweredCount: 40,
            CorrectCount: 34, // 85% vs previous 60%
            HintUsedCount: null,
            CorrectWithHintCount: null,
            TargetBandCode: "B2",
            TargetBandName: "Vantage",
            ScaledScore: 800,
            CurrentBandCode: "B2",
            Questions: Array.Empty<InsightQuestionDetail>(),
            SectionAccuracies: sections,
            CategoryAccuracies: Array.Empty<InsightCategoryAccuracy>(),
            PreviousInsight: previousInsight);

        var result = await generator.GenerateInsightAsync(request);

        Assert.True(result.Success);
        Assert.Equal(AiInsightTrend.Improving, result.Trend);
        Assert.Contains("improving", result.Narrative);
        Assert.Contains("Compared to your previous review", result.Narrative);
    }

    [Fact]
    public async Task Dashboard_scope_detects_declining_trend_when_accuracy_drops()
    {
        var generator = new FakeAiInsightGenerator();

        var sections = new List<InsightSectionAccuracy>
        {
            new(1, "P1", "Photographs", 20, 10, 50),
            new(2, "P5", "Incomplete Sentences", 20, 8, 40)
        };

        var previousInsight = new PreviousInsightSummary(
            InsightId: 99,
            Narrative: "**Performance Summary**: Overall accuracy of 75% (30/40 questions answered across TOEIC).",
            Trend: AiInsightTrend.Improving,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-7));

        var request = new AiInsightGenerationRequest(
            UserId: 1,
            CertId: 1,
            CertName: "TOEIC",
            Scope: AiInsightScope.Dashboard,
            PracticeSessionId: null,
            AttemptId: null,
            ActivityTitle: "TOEIC Dashboard",
            TotalQuestions: 40,
            AnsweredCount: 40,
            CorrectCount: 18, // 45% vs previous 75%
            HintUsedCount: null,
            CorrectWithHintCount: null,
            TargetBandCode: "B2",
            TargetBandName: "Vantage",
            ScaledScore: 450,
            CurrentBandCode: "B1",
            Questions: Array.Empty<InsightQuestionDetail>(),
            SectionAccuracies: sections,
            CategoryAccuracies: Array.Empty<InsightCategoryAccuracy>(),
            PreviousInsight: previousInsight);

        var result = await generator.GenerateInsightAsync(request);

        Assert.True(result.Success);
        Assert.Equal(AiInsightTrend.Declining, result.Trend);
        Assert.Contains("declined", result.Narrative);
        Assert.Contains("Compared to your previous review", result.Narrative);
    }
}
