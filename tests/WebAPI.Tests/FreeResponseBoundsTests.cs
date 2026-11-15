using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services;
using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

public sealed class FreeResponseBoundsTests
{
    [Fact]
    public async Task Writing_draft_rejects_exceeding_5000_chars_with_400_and_response_field_error()
    {
        var tracker = new CallTracker();
        var (client, app) = await CreateAppAsync(tracker);
        await using (app)
        {
            var oversized = new string('a', 5001);
            var response = await client.PostAsJsonAsync(
                "/api/learner/writing/attempts/10/draft",
                new SaveWritingDraftRequest(1, oversized));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(tracker.WritingSaveDraftCalled, "RequestValidationFilter should short-circuit before calling WritingPracticeService.");
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("response", out var fieldErrors));
            Assert.True(fieldErrors.GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Writing_draft_rejects_exceeding_1000_words_with_400_and_response_field_error()
    {
        var tracker = new CallTracker();
        var (client, app) = await CreateAppAsync(tracker);
        await using (app)
        {
            var tooManyWords = string.Join(' ', Enumerable.Repeat("word", 1001));
            var response = await client.PostAsJsonAsync(
                "/api/learner/writing/attempts/10/draft",
                new SaveWritingDraftRequest(1, tooManyWords));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(tracker.WritingSaveDraftCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("response", out var fieldErrors));
            Assert.True(fieldErrors.GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Writing_submit_rejects_invalid_response_with_400_and_response_field_error()
    {
        var tracker = new CallTracker
        {
            WritingSubmitException = new WritingPracticeException(
                WritingPracticeException.InvalidResponse,
                "Response must be between 10 and 1,000 words.")
        };
        var (client, app) = await CreateAppAsync(tracker);
        await using (app)
        {
            var response = await client.PostAsync("/api/learner/writing/attempts/10/submit", content: null);
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Expected 400, got {(int)response.StatusCode}: {body}");
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("response", out var fieldErrors));
            Assert.True(fieldErrors.GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Exam_draft_and_answer_reject_exceeding_5000_chars_with_400_and_response_field_error()
    {
        var tracker = new CallTracker();
        var (client, app) = await CreateAppAsync(tracker);
        await using (app)
        {
            var oversized = new string('x', 5001);

            var mockDraftResp = await client.PostAsJsonAsync(
                "/api/learner/attempts/10/draft",
                new SaveAttemptDraftRequest(1, oversized));
            Assert.Equal(HttpStatusCode.BadRequest, mockDraftResp.StatusCode);
            Assert.False(tracker.MockSaveDraftCalled);

            var mockAnswerResp = await client.PostAsJsonAsync(
                "/api/learner/attempts/10/answers/1",
                new SaveAnswerRequest(null, false, oversized));
            Assert.Equal(HttpStatusCode.BadRequest, mockAnswerResp.StatusCode);
            Assert.False(tracker.MockSaveAnswerCalled);

            var placementDraftResp = await client.PostAsJsonAsync(
                "/api/learner/placement/take/10/draft",
                new SavePlacementDraftRequestDto(1, oversized));
            Assert.Equal(HttpStatusCode.BadRequest, placementDraftResp.StatusCode);
            Assert.False(tracker.PlacementSaveDraftCalled);

            var placementAnswerResp = await client.PostAsJsonAsync(
                "/api/learner/placement/take/10/answers/1",
                new SaveAnswerRequest(null, false, oversized));
            Assert.Equal(HttpStatusCode.BadRequest, placementAnswerResp.StatusCode);
            Assert.False(tracker.PlacementSaveAnswerCalled);
        }
    }

    [Fact]
    public async Task Speaking_upload_rejects_oversized_audio_over_10MB_with_400()
    {
        var tracker = new CallTracker();
        var (client, app) = await CreateAppAsync(tracker);
        await using (app)
        {
            using var content = new MultipartFormDataContent();
            var bytes = new byte[(10 * 1024 * 1024) + 1];
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/webm");
            content.Add(fileContent, "audioFile", "oversized.webm");
            content.Add(new StringContent("5000"), "durationMs");

            var response = await client.PostAsync("/api/learner/speaking/attempts/10/questions/1/audio", content);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(tracker.SpeakingSaveAudioCalled);
        }
    }

    [Fact]
    public async Task Speaking_upload_rejects_duration_over_180_seconds_with_400()
    {
        var tracker = new CallTracker();
        var (client, app) = await CreateAppAsync(tracker);
        await using (app)
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent([1, 2, 3, 4]);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/webm");
            content.Add(fileContent, "audioFile", "take.webm");
            content.Add(new StringContent("181000"), "durationMs");

            var response = await client.PostAsync("/api/learner/speaking/attempts/10/questions/1/audio", content);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(tracker.SpeakingSaveAudioCalled);
        }
    }

    [Theory]
    [InlineData("audio/webm", "take.webm")]
    [InlineData("audio/wav", "take.wav")]
    [InlineData("audio/mp4", "take.mp4")]
    [InlineData("audio/ogg", "take.ogg")]
    public async Task Speaking_upload_accepts_valid_audio_within_bounds(string mimeType, string fileName)
    {
        var tracker = new CallTracker();
        var (client, app) = await CreateAppAsync(tracker);
        await using (app)
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent([1, 2, 3, 4]);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
            content.Add(fileContent, "audioFile", fileName);
            content.Add(new StringContent("180000"), "durationMs");

            var response = await client.PostAsync("/api/learner/speaking/attempts/10/questions/1/audio", content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(tracker.SpeakingSaveAudioCalled);
        }
    }

    private static async Task<(HttpClient Client, WebApplication App)> CreateAppAsync(CallTracker tracker)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddSingleton(ServiceProxy<IWritingPracticeService>.Create(tracker));
        builder.Services.AddSingleton(ServiceProxy<ISpeakingPracticeService>.Create(tracker));
        builder.Services.AddSingleton(ServiceProxy<IMockExamService>.Create(tracker));
        builder.Services.AddSingleton(ServiceProxy<IPlacementService>.Create(tracker));
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options =>
            options.AddPolicy(Roles.Learner, policy => policy.RequireRole("learner")));

        var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapWritingEndpoints();
        app.MapSpeakingEndpoints();
        app.MapExamEndpoints();
        app.MapPlacementEndpoints();

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        return (client, app);
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(IAccessTokenFactory.ClaimUserId, "42"),
                    new Claim(ClaimTypes.NameIdentifier, "42"),
                    new Claim(ClaimTypes.Role, "learner"),
                ],
                "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class CallTracker
    {
        public bool WritingSaveDraftCalled { get; set; }
        public bool SpeakingSaveAudioCalled { get; set; }
        public bool MockSaveDraftCalled { get; set; }
        public bool MockSaveAnswerCalled { get; set; }
        public bool PlacementSaveDraftCalled { get; set; }
        public bool PlacementSaveAnswerCalled { get; set; }
        public Exception? WritingSubmitException { get; init; }
    }

    private class ServiceProxy<TService> : DispatchProxy where TService : class
    {
        private CallTracker _tracker = null!;

        public static TService Create(CallTracker tracker)
        {
            var proxy = Create<TService, ServiceProxy<TService>>();
            ((ServiceProxy<TService>)(object)proxy)._tracker = tracker;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var iface = typeof(TService).Name;
            var name = targetMethod?.Name;

            if (iface == nameof(IWritingPracticeService))
            {
                if (name == nameof(IWritingPracticeService.SaveDraftAsync))
                {
                    _tracker.WritingSaveDraftCalled = true;
                    return Task.FromResult(new SaveDraftResult(1, 1, DateTimeOffset.UtcNow, 10));
                }
                if (name == nameof(IWritingPracticeService.SubmitAttemptAsync))
                {
                    if (_tracker.WritingSubmitException is not null)
                    {
                        return Task.FromException<SubmitWritingResult>(_tracker.WritingSubmitException);
                    }
                    return Task.FromResult(new SubmitWritingResult(10, AttemptStatus.Submitted, DateTimeOffset.UtcNow));
                }
            }
            else if (iface == nameof(ISpeakingPracticeService) && name == nameof(ISpeakingPracticeService.SaveAudioAsync))
            {
                _tracker.SpeakingSaveAudioCalled = true;
                return Task.FromResult(new SaveSpeakingAudioResult(1, 1, "audio/1.webm", 1000, DateTimeOffset.UtcNow));
            }
            else if (iface == nameof(IMockExamService))
            {
                if (name == nameof(IMockExamService.SaveDraftAsync))
                {
                    _tracker.MockSaveDraftCalled = true;
                    return Task.FromResult(new SaveAttemptDraftResult(1, 1, DateTimeOffset.UtcNow, 10));
                }
                if (name == nameof(IMockExamService.SaveAnswerAsync))
                {
                    _tracker.MockSaveAnswerCalled = true;
                    return Task.FromResult(new SaveAttemptAnswerResult(1, null, false, 100, false));
                }
            }
            else if (iface == nameof(IPlacementService))
            {
                if (name == nameof(IPlacementService.SaveDraftAsync))
                {
                    _tracker.PlacementSaveDraftCalled = true;
                    return Task.FromResult(new SaveAttemptDraftResult(1, 1, DateTimeOffset.UtcNow, 10));
                }
                if (name == nameof(IPlacementService.SaveAnswerAsync))
                {
                    _tracker.PlacementSaveAnswerCalled = true;
                    return Task.FromResult(new SaveAttemptAnswerResult(1, null, false, 100, false));
                }
            }

            throw new NotImplementedException($"{iface}.{name}");
        }
    }
}
