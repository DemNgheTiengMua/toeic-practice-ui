using System.Net;
using System.Net.Http.Json;
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
/// #235 / PLC-62: the free-drill endpoint serves the lock <b>and its sentence</b>. The input that would
/// pass while being wrong is an endpoint returning a bare boolean: the screen would then have to invent
/// the reason, which is the "lock with no explanation" the brief forbids.
/// </summary>
public sealed class PracticeFreeDrillEndpointTests
{
    [Fact]
    public async Task The_locked_state_reaches_the_wire_with_its_steps_and_minutes()
    {
        var service = new RecordingService
        {
            State = new FreeDrillState(
                IsUnlocked: false,
                IncompleteSteps: [new IncompleteStep(41, 2, "Weakness in R-DET", "R-DET", 12)],
                RemainingMinutes: 12,
                Priorities:
                [
                    new PrioritisedCoordinate(3, "READ", "Reading", Modality.Reading, 5, "R-DET", "Local detail",
                        CefrLevel.B1, "Open on your path in week 2"),
                ],
                CellUsedToday: false,
                DailyCeilingReached: false,
                AnsweredToday: 0,
                DailyItemCeiling: 60,
                LockNotice: "Free drilling opens once this week's steps are done. 1 step(s) remain, about 12 minute(s): "
                    + "Weakness in R-DET (12 min)"),
        };

        var (client, app) = await PracticeAppAsync(service);
        using (app)
        {
            var dto = await client.GetFromJsonAsync<FreeDrillStateDto>("/api/learner/practice/free-drill");

            Assert.NotNull(dto);
            Assert.False(dto!.IsUnlocked);
            Assert.Equal(12, dto.RemainingMinutes);
            var step = Assert.Single(dto.IncompleteSteps);
            Assert.Equal(41, step.StepId);
            Assert.Equal(12, step.EstimatedMinutes);
            Assert.Contains("12 minute", dto.LockNotice!, StringComparison.Ordinal);

            var priority = Assert.Single(dto.Priorities);
            Assert.Equal("R-DET", priority.TopicCode);
            Assert.Equal("B1", priority.LevelCode);
        }
    }

    [Fact]
    public async Task The_cell_and_level_travel_to_the_service()
    {
        var service = new RecordingService();
        var (client, app) = await PracticeAppAsync(service);
        using (app)
        {
            await client.GetAsync("/api/learner/practice/free-drill?topicId=7&cefrLevelCode=B1");

            Assert.Equal(7, service.LastTopicId);
            Assert.Equal(CefrLevel.B1, service.LastLevel);
        }
    }

    /// <summary>A level code that is not an authored level is not passed through as a fabricated
    /// CefrLevel; the screen simply asks about no cell.</summary>
    [Fact]
    public async Task An_unauthorable_level_code_is_read_as_no_cell()
    {
        var service = new RecordingService();
        var (client, app) = await PracticeAppAsync(service);
        using (app)
        {
            var response = await client.GetAsync("/api/learner/practice/free-drill?topicId=7&cefrLevelCode=pre_a1");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(7, service.LastTopicId);
            Assert.Null(service.LastLevel);
        }
    }

    private static async Task<(HttpClient Client, WebApplication App)> PracticeAppAsync(IPracticeService service)
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
                [new Claim(IAccessTokenFactory.ClaimUserId, "9")], "Test"));
            await next();
        });

        var learner = app.MapGroup("/api/learner");
        var practice = learner.MapGroup("/practice");
        typeof(PracticeEndpoints)
            .GetMethod("MapFreeDrill", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [practice]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private sealed class RecordingService : IPracticeService
    {
        public FreeDrillState State { get; init; } = FreeDrillState.Unrestricted([], false, 0, false);

        public int? LastTopicId { get; private set; }

        public CefrLevel? LastLevel { get; private set; }

        public Task<FreeDrillState> GetFreeDrillStateAsync(
            int userId, int? topicId, CefrLevel? level, CancellationToken cancellationToken = default)
        {
            LastTopicId = topicId;
            LastLevel = level;
            return Task.FromResult(State);
        }

        public Task<PracticeSessionInfo> StartSessionAsync(int userId, StartPracticeSessionInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PracticeSectionsView> GetPracticeSectionsAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LessonMicroCheckAnswer> SubmitLessonMicroCheckAsync(int userId, int lessonId, int cardId, string selectedOption, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PracticeSessionInfo> GetSessionInfoAsync(int userId, int sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PracticeQuestionView> GetQuestionAsync(int userId, int sessionId, int displayOrder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> RevealHintAsync(int userId, int sessionId, int questionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GradedPracticeAnswerResult> SubmitAnswerAsync(int userId, int sessionId, int questionId, SubmitPracticeAnswerInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PracticeSummary> GetSummaryAsync(int userId, int sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PracticeQuestionView>> GetSessionQuestionsAsync(int userId, int sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PracticeSessionTake> GetSessionTakeAsync(int userId, int sessionId, int? displayOrder = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
