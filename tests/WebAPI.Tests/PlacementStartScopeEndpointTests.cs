using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services;
using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #289: HTTP surface for scope selection, idempotency key forwarding, and 402 insufficient credits on retake start.
/// </summary>
public sealed class PlacementStartScopeEndpointTests
{
    [Fact]
    public async Task Start_forwards_scope_and_idempotency_key_and_returns_402_on_insufficient_credits()
    {
        var stub = new StubPlacementService();
        var (client, app) = await PlacementAppAsync(stub);
        using (app)
        {
            var okResponse = await client.PostAsJsonAsync(
                "/api/learner/placement/start",
                new StartPlacementTestRequest(7, ReuseValidSkills: false, PlacementScopeDto.ListeningAndReading, "idem-key-1"));

            Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
            Assert.Equal(PlacementScope.ListeningAndReading, stub.CapturedScope);
            Assert.Equal("idem-key-1", stub.CapturedIdempotencyKey);

            var dto = await okResponse.Content.ReadFromJsonAsync<PlacementTestInfoDto>();
            Assert.NotNull(dto);
            Assert.Equal(PlacementScopeDto.ListeningAndReading, dto.Scope);
            Assert.Equal(3, dto.CreditsCharged);

            stub.ThrowInsufficientCredits = true;
            var failResponse = await client.PostAsJsonAsync(
                "/api/learner/placement/start",
                new StartPlacementTestRequest(7, ReuseValidSkills: false, PlacementScopeDto.AllSkills, "idem-key-2"));

            Assert.Equal(HttpStatusCode.PaymentRequired, failResponse.StatusCode);
            var body = await failResponse.Content.ReadAsStringAsync();
            Assert.Contains(ApiError.Codes.InsufficientCredits, body, StringComparison.Ordinal);
        }
    }

    private static async Task<(HttpClient Client, WebApplication App)> PlacementAppAsync(IPlacementService service)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddSingleton(service);
        builder.Services.AddProblemDetails();

        var app = builder.Build();
        app.UseExceptionHandler();

        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(IAccessTokenFactory.ClaimUserId, "12")], "Test"));
            await next();
        });

        var learner = app.MapGroup("/api/learner");
        typeof(PlacementEndpoints)
            .GetMethod("MapPlacementCatalogEndpoints", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [learner]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private sealed class StubPlacementService : IPlacementService
    {
        public PlacementScope? CapturedScope { get; private set; }
        public string? CapturedIdempotencyKey { get; private set; }
        public bool ThrowInsufficientCredits { get; set; }

        public Task<PlacementTestInfo> StartPlacementTestAsync(
            int userId,
            int examId,
            PlacementReuseChoice reuse,
            PlacementScope scope,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            if (ThrowInsufficientCredits)
            {
                return Task.FromException<PlacementTestInfo>(
                    new PlacementException(PlacementException.InsufficientCredits, "Need 5 credits."));
            }

            CapturedScope = scope;
            CapturedIdempotencyKey = idempotencyKey;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new PlacementTestInfo(
                101, 202, examId, PlacementStatus.InProgress, now, now.AddMinutes(30), 1800, false,
                Scope: scope,
                CreditsCharged: scope == PlacementScope.ListeningAndReading ? 3 : 5));
        }

        public Task<PlacementCatalogView> GetPlacementCatalogAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlacementTestInfo> StartPlacementTestAsync(int userId, int examId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlacementTestInfo> StartPlacementTestAsync(int userId, int examId, PlacementReuseChoice reuse, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlacementPreflightView> GetPreflightAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PreflightStatusView> GetPlacementPreflightStatusAsync(int userId, int placementId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlacementTakeView> GetPlacementTakeAsync(int userId, int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlacementResumeView> GetResumeAsync(int userId, int placementId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task AbandonAsync(int userId, int placementId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlacementRecordingDecision> RecordRecordingInterruptionAsync(int userId, int placementId, int questionId, string sessionToken, int capturedMs, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<SaveAttemptAnswerResult> SaveAnswerAsync(int userId, int placementId, int questionId, int? selectedOptionId, bool isFlagged, CancellationToken cancellationToken = default, string? responseText = null, string? selectedOptionIdsJson = null) =>
            throw new NotSupportedException();
        public Task<SaveAttemptDraftResult> SaveDraftAsync(int userId, int placementId, int questionId, string? responseText, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlacementGradingStatusView> GetGradingStatusAsync(int userId, int placementId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlacementRetryResult> RetryGradingAsync(int userId, int placementId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlacementResultView> SubmitPlacementTestAsync(int userId, int placementId, bool isAutoSubmit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlacementResultView> GetPlacementResultAsync(int userId, int placementId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<TargetBandView> GetTargetBandViewAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<SaveTargetBandResult> SetTargetBandAsync(int userId, string targetBandCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<SaveGoalResult> SetGoalAsync(int userId, string? currentBandCode, string targetBandCode, DateOnly? targetDate, decimal? weeklyStudyHours, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<GoalStatus> GetGoalStatusAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
