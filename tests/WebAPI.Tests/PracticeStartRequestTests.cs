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
/// #187: the start endpoint accepts either a section or a cell, and refuses anything else with a 400
/// before the service is reached. Maps the real session endpoint - the actual route and its catch -
/// into a minimal app, so the assertion is about production behaviour. Only the service is replaced.
/// </summary>
public sealed class PracticeStartRequestTests
{
    [Fact]
    public async Task A_request_naming_both_a_section_and_a_cell_is_refused_before_the_service_runs()
    {
        var service = new RecordingService();
        var (client, app) = await PracticeAppAsync(service);
        using (app)
        {
            var response = await client.PostAsJsonAsync(
                "/api/learner/practice/sessions",
                new StartPracticeRequest(SectionId: 3, TopicId: 7, CefrLevelCode: "B1"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(service.StartedInput);
        }
    }

    [Fact]
    public async Task A_request_naming_neither_shape_is_refused_before_the_service_runs()
    {
        var service = new RecordingService();
        var (client, app) = await PracticeAppAsync(service);
        using (app)
        {
            var response = await client.PostAsJsonAsync(
                "/api/learner/practice/sessions",
                new StartPracticeRequest());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(service.StartedInput);
        }
    }

    [Fact]
    public async Task A_cell_request_reaches_the_service_as_a_cell_not_a_section()
    {
        var service = new RecordingService();
        var (client, app) = await PracticeAppAsync(service);
        using (app)
        {
            var response = await client.PostAsJsonAsync(
                "/api/learner/practice/sessions",
                new StartPracticeRequest(TopicId: 7, CefrLevelCode: "B1"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var input = Assert.IsType<StartPracticeSessionInput>(service.StartedInput);
            Assert.Null(input.SectionId);
            Assert.Equal(7, input.TopicId);
            Assert.Equal(CefrLevel.B1, input.CefrLevel);
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

        // CurrentUserId reads the claim the JWT middleware would have set.
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(IAccessTokenFactory.ClaimUserId, "9")], "Test"));
            await next();
        });

        var learner = app.MapGroup("/api/learner");
        var practice = learner.MapGroup("/practice");
        typeof(PracticeEndpoints)
            .GetMethod("MapSessions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [practice]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    /// <summary>Records the input it was asked to start, and answers with a pool-shaped session.</summary>
    private sealed class RecordingService : IPracticeService
    {
        public StartPracticeSessionInput? StartedInput { get; private set; }

        public Task<PracticeSessionInfo> StartSessionAsync(
            int userId, StartPracticeSessionInput input, CancellationToken cancellationToken = default)
        {
            StartedInput = input;
            return Task.FromResult(new PracticeSessionInfo(
                SessionId: 1,
                SectionId: null,
                SectionCode: "R-GRAM",
                SectionName: "Morphosyntactic structure and lexical precision",
                Modality: Modality.Reading,
                QuestionCount: 2,
                Status: PracticeStatus.InProgress,
                CorrectCount: 0,
                QuestionSource: QuestionSource.All,
                StartedAt: DateTimeOffset.UnixEpoch,
                FinishedAt: null,
                TopicId: 7,
                CefrLevel: CefrLevel.B1));
        }

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
