using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Xunit;

namespace WebMVC.Tests;

public sealed class LessonClientTests
{
    private sealed class MockHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private static (CompanionApiClient client, Func<HttpRequestMessage?> getLastRequest) CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        HttpRequestMessage? lastRequest = null;
        var mockHandler = new MockHandler(req =>
        {
            lastRequest = req;
            return handler(req);
        });
        var httpClient = new HttpClient(mockHandler)
        {
            BaseAddress = new Uri("http://localhost:5001")
        };
        return (new CompanionApiClient(httpClient), () => lastRequest);
    }

    [Fact]
    public async Task GetLessonAsync_SendsCorrectRequest_AndDeserializesResponse()
    {
        var expectedLesson = new LessonDetailDto(
            LessonId: 42,
            Title: "Present Continuous",
            Summary: "Master present continuous tense",
            SkillId: 1,
            CategoryId: 10,
            EstimatedMinutes: 5,
            Cards:
            [
                new LessonCardDto(
                    CardId: 1,
                    CardType: "rule",
                    Title: "The Rule",
                    ContentMarkdown: "# Rule\nSubject + be + V-ing",
                    QuestionPrompt: null,
                    OptionA: null,
                    OptionB: null,
                    OptionC: null,
                    OptionD: null,
                    CorrectOption: null,
                    Explanation: null,
                    DisplayOrder: 0
                )
            ]
        );

        var (client, getLastRequest) = CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("/api/learner/lessons/42", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(expectedLesson)
            };
        });

        var result = await client.GetLessonAsync(42, "test-token");

        Assert.NotNull(result);
        Assert.Equal(42, result.LessonId);
        Assert.Equal("Present Continuous", result.Title);
        Assert.Single(result.Cards);
        var req = getLastRequest();
        Assert.Equal("Bearer", req!.Headers.Authorization!.Scheme);
        Assert.Equal("test-token", req.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GetLessonProgressAsync_SendsCorrectRequest_AndDeserializesResponse()
    {
        var expectedProgress = new LessonProgressDto(
            CardsCompleted: 3,
            TotalCards: 4,
            IsCompleted: false,
            LastViewedCardId: 102,
            FirstOpenedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt: null,
            UpdatedAt: DateTimeOffset.UtcNow
        );

        var (client, _) = CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("/api/learner/lessons/42/progress", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(expectedProgress)
            };
        });

        var result = await client.GetLessonProgressAsync(42);

        Assert.NotNull(result);
        Assert.Equal(3, result.CardsCompleted);
        Assert.Equal(4, result.TotalCards);
        Assert.False(result.IsCompleted);
        Assert.Equal(102, result.LastViewedCardId);
    }

    [Fact]
    public async Task RecordCardViewAsync_SendsPostRequest()
    {
        var (client, getLastRequest) = CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/api/learner/lessons/42/cards/103/view", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true })
            };
        });

        await client.RecordCardViewAsync(42, 103, "my-token");

        var req = getLastRequest();
        Assert.NotNull(req);
        Assert.Equal("my-token", req.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetLessonByCategoryAsync_SendsCorrectRequest()
    {
        var expectedLesson = new LessonDetailDto(
            LessonId: 99,
            Title: "Conditionals",
            Summary: "Zero, First, Second conditionals",
            SkillId: 2,
            CategoryId: 15,
            EstimatedMinutes: 8,
            Cards: []
        );

        var (client, _) = CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("/api/learner/lessons/by-category/15", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(expectedLesson)
            };
        });

        var result = await client.GetLessonByCategoryAsync(15);

        Assert.NotNull(result);
        Assert.Equal(99, result.LessonId);
        Assert.Equal(15, result.CategoryId);
    }

    [Fact]
    public async Task UnlockPathAsync_SendsPostToUnlock()
    {
        var (client, getLastRequest) = CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/api/learner/learning-path/7/unlock", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true })
            };
        });

        await client.UnlockPathAsync(7, "tok-123");

        var req = getLastRequest();
        Assert.NotNull(req);
        Assert.Equal("tok-123", req.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetLessonAsync_WhenNotFound_ThrowsApiExceptionWithCorrectCode()
    {
        var problem = new
        {
            title = "Lesson not found",
            status = 404,
            code = ApiError.Codes.LessonNotFound,
            detail = "Lesson 999 not found"
        };

        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(problem)
        });

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetLessonAsync(999));

        Assert.Equal(404, ex.Status);
        Assert.Equal(ApiError.Codes.LessonNotFound, ex.Code);
        Assert.Equal("Lesson 999 not found", ex.Detail);
    }

    [Fact]
    public async Task UnlockPathAsync_WhenInsufficientCredits_ThrowsApiException()
    {
        var problem = new
        {
            title = "Insufficient credits",
            status = 400,
            code = ApiError.Codes.InsufficientCredits,
            detail = "Insufficient credits to unlock path."
        };

        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(problem)
        });

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.UnlockPathAsync(7));

        Assert.Equal(400, ex.Status);
        Assert.Equal(ApiError.Codes.InsufficientCredits, ex.Code);
    }
}
