using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Contracts;

namespace WebMVC.Tests;

public sealed class DiagnosticClientTests
{
    [Fact]
    public async Task Practice_answer_preserves_payload_bearer_and_cancellation()
    {
        var payload = new SubmitPracticeAnswerRequest(null, "written answer", "[11,12]", [11, 12]);
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/learner/practice/sessions/42/questions/7/answer", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("learner-token", request.Headers.Authorization?.Parameter);
            var actual = await request.Content!.ReadFromJsonAsync<SubmitPracticeAnswerRequest>();
            Assert.Equal(payload.ResponseText, actual!.ResponseText);
            Assert.Equal(payload.SelectedOptionIdsJson, actual.SelectedOptionIdsJson);
            Assert.Equal(payload.SelectedOptionIds, actual.SelectedOptionIds);
            return JsonResponse("null");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") };
        await new CompanionApiClient(http).SubmitPracticeAnswerAsync(42, 7, payload, "learner-token", cancellation.Token);
        Assert.True(handler.Token.CanBeCanceled);
    }

    [Fact]
    public async Task Saved_question_filters_preserve_encoded_query_parameters()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("/api/learner/saved-questions?source=manual%26incorrect&sectionId=5&categoryId=9", request.RequestUri!.PathAndQuery);
            return Task.FromResult(JsonResponse("null"));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") };
        await new CompanionApiClient(http).GetSavedQuestionsAsync("manual&incorrect", 5, 9);
    }

    [Fact]
    public async Task Weakness_resolution_retains_structured_problem_details()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/learner/recommendations/9/resolve", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new { code = "recommendation_not_found", title = "Not found", detail = "Not owned by this learner." })
            });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") };
        var error = await Assert.ThrowsAsync<ApiException>(() => new CompanionApiClient(http).ResolveRecommendationAsync(9));
        Assert.Equal(404, error.Status);
        Assert.Equal("recommendation_not_found", error.Code);
        Assert.Equal("Not owned by this learner.", error.Detail);
    }

    [Fact]
    public async Task Insight_retry_retains_certificate_filter_and_post_method()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/learner/insights/dashboard/retry?certId=3", request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") };
        await new CompanionApiClient(http).RetryDashboardInsightAsync(3);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public CancellationToken Token { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return respond(request);
        }
    }
}
