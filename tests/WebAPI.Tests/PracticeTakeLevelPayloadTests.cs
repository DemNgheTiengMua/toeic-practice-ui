using System.Net;
using System.Security.Claims;
using System.Text.Json;
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
/// #238 / U18: the take endpoint's payload.
/// <para>
/// The level must be <b>absent</b> from the JSON an unanswered question travels in, and present after
/// the answer locks. This asserts the serialized body the browser receives — not a class name and not a
/// hidden element — so a leak cannot pass by being styled away.
/// </para>
/// </summary>
public sealed class PracticeTakeLevelPayloadTests
{
    private static PracticeQuestionView Question(bool locked) => new(
        SessionId: 1,
        SectionName: "Detail",
        QuestionId: null,
        DisplayOrder: 1,
        TotalQuestions: 2,
        Stem: "Stem",
        CategoryName: null,
        Hint: null,
        HintUsed: false,
        Options: [new PracticeQuestionOption(11, "A", "First"), new PracticeQuestionOption(12, "B", "Second")],
        SelectedOptionId: locked ? 11 : null,
        IsCorrect: locked ? true : null,
        CorrectOptionId: locked ? 12 : null,
        Explanation: locked ? "Because." : null,
        Transcript: null,
        Passage: null,
        AudioPath: null,
        ImagePath: null,
        ImageAlt: null);

    private static PracticeSessionInfo Session() => new(
        SessionId: 1, SectionId: null, SectionCode: "R-DET", SectionName: "Detail",
        Modality: Modality.Reading, QuestionCount: 2, Status: PracticeStatus.InProgress,
        CorrectCount: 0, QuestionSource: QuestionSource.All, StartedAt: DateTimeOffset.UnixEpoch,
        FinishedAt: null, TopicId: 5, CefrLevel: CefrLevel.B2);

    /// <summary>
    /// The guard: an unanswered question must not carry a level in the take body, even though the session
    /// it belongs to names the cell's level. The wrong input that passes a weaker test is a DTO that
    /// carries the level and hides it only in markup.
    /// </summary>
    [Fact]
    public async Task The_take_body_omits_the_level_for_an_unanswered_question()
    {
        var service = new StubService { Take = new PracticeSessionTake( Session(), Question(locked: false), [Question(locked: false)], []) };
        var (client, app) = await PracticeAppAsync(service);
        using (app)
        {
            var response = await client.GetAsync("/api/learner/practice/sessions/1/take?q=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var question = document.RootElement.GetProperty("question");
            Assert.False(question.TryGetProperty("cefrLevelCode", out var level)
                         && level.ValueKind == JsonValueKind.String,
                "an unanswered question must not name its level in the take body");
        }
    }

    /// <summary>
    /// The other half: a locked question carries the level, so the page can reveal it, and the session
    /// carries the cell too. The wrong input that would pass a weaker test is a body where the session
    /// has the level but the question never does — the reveal would have nothing to render from.
    /// </summary>
    [Fact]
    public async Task The_take_body_carries_the_cell_level_for_a_locked_question()
    {
        var service = new StubService { Take = new PracticeSessionTake(Session(), Question(locked: true), [Question(locked: true)], []) };
        var (client, app) = await PracticeAppAsync(service);
        using (app)
        {
            var response = await client.GetAsync("/api/learner/practice/sessions/1/take?q=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var session = document.RootElement.GetProperty("session");
            Assert.Equal("B2", session.GetProperty("cefrLevelCode").GetString());
            Assert.Equal(5, session.GetProperty("topicId").GetInt32());

            var question = document.RootElement.GetProperty("question");
            Assert.Equal("B2", question.GetProperty("cefrLevelCode").GetString());
        }
    }

    /// <summary>
    /// A section drill names no cell, so there is no level to reveal even after a lock — and the body
    /// must not invent one. The wrong input is a body that fills the level from the certificate or the
    /// section name.
    /// </summary>
    [Fact]
    public async Task A_section_drill_carries_no_level_even_when_locked()
    {
        var sectionSession = Session() with { SectionId = 5, TopicId = null, CefrLevel = null };
        var service = new StubService
        {
            Take = new PracticeSessionTake(sectionSession, Question(locked: true), [Question(locked: true)], []),
        };
        var (client, app) = await PracticeAppAsync(service);
        using (app)
        {
            var response = await client.GetAsync("/api/learner/practice/sessions/1/take?q=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var question = document.RootElement.GetProperty("question");
            Assert.False(question.TryGetProperty("cefrLevelCode", out var level)
                         && level.ValueKind == JsonValueKind.String);
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
            .GetMethod("MapQuestions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [practice]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private sealed class StubService : IPracticeService
    {
        public PracticeSessionTake? Take { get; set; }

        public Task<PracticeSessionTake> GetSessionTakeAsync(int userId, int sessionId, int? displayOrder = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Take!);

        public Task<PracticeSectionsView> GetPracticeSectionsAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PracticeSessionInfo> StartSessionAsync(int userId, StartPracticeSessionInput input, CancellationToken cancellationToken = default) =>
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
    }
}
