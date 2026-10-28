using BusinessObjects;
using Services;

namespace Services.Tests;

public sealed class ClaudeAiInsightGeneratorTests
{
    private static AiInsightGenerationRequest Request() => new(
        7, 1, "TOEIC", AiInsightScope.PracticeSession, 42, null, "Reading practice session", 10, 5, 3,
        1, 1, "B2", "Vantage", null, null, [],
        [new(5, "P5", "Reading", 5, 3, 60)],
        [new(9, "TENSE", "Verb tense", 5, 3, 60)]);

    [Theory]
    [InlineData("{\"narrative\":\"Review verb tense.\",\"trend\":\"Improving\",\"recommendations\":[{\"text\":\"Drill verb tense\",\"severity\":\"High\"}]}")]
    [InlineData("```json\n{\"narrative\":\"Review verb tense.\",\"trend\":\"Improving\",\"recommendations\":[{\"text\":\"Drill verb tense\",\"severity\":\"High\"}]}\n```")]
    public async Task Generation_preserves_evidence_prompt_and_parses_plain_or_fenced_json(string response)
    {
        var client = new RecordingClient(response);
        using var cancellation = new CancellationTokenSource();
        var result = await new ClaudeAiInsightGenerator(client).GenerateInsightAsync(Request(), cancellation.Token);

        Assert.True(result.Success);
        Assert.Equal("Review verb tense.", result.Narrative);
        Assert.Equal(AiInsightTrend.Improving, result.Trend);
        var recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(RecommendationSeverity.Major, recommendation.Severity);
        Assert.Equal("B2", recommendation.TargetBandCode);
        Assert.Contains("3 correct out of 5 answered (60%) across 10 questions", client.Prompt);
        Assert.Contains("Verb tense (TENSE): 3/5 (60%)", client.Prompt);
        Assert.Contains("Reading (P5): 3/5 (60%)", client.Prompt);
        Assert.Contains("Target Band / Goal: B2 (Vantage)", client.Prompt);
        Assert.Equal(4000, client.MaxTokens);
        Assert.Equal(cancellation.Token, client.CancellationToken);
    }

    [Theory]
    [InlineData("invalid JSON")]
    [InlineData("{}")]
    public async Task Invalid_provider_responses_return_failed_results(string response)
    {
        var result = await new ClaudeAiInsightGenerator(new RecordingClient(response)).GenerateInsightAsync(Request());
        Assert.False(result.Success);
        Assert.NotEmpty(result.FailureReason!);
        Assert.Empty(result.Recommendations);
    }

    [Fact]
    public async Task Simulated_failure_never_calls_provider()
    {
        var client = new RecordingClient("{}");
        var result = await new ClaudeAiInsightGenerator(client).GenerateInsightAsync(Request() with { SimulateFailure = true });
        Assert.False(result.Success);
        Assert.Null(client.Prompt);
    }

    private sealed class RecordingClient(string response) : IClaudeClient
    {
        public string? Prompt { get; private set; }
        public int MaxTokens { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<string> CreateMessageAsync(string userPrompt, string? systemPrompt = null, int maxTokens = 1500,
            CancellationToken cancellationToken = default)
        {
            Prompt = userPrompt;
            MaxTokens = maxTokens;
            CancellationToken = cancellationToken;
            return Task.FromResult(response);
        }
    }
}
