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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services;
using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #166: a second submission of an answered question is refused by the service, and the learner has to
/// see a sentence rather than a generic failure. Maps the real answer endpoint - the actual route and
/// its catch - into a minimal app, so the assertion is about production behaviour. Only the service is
/// replaced.
/// </summary>
public sealed class PracticeAnswerRefusalTests
{
    [Fact]
    public async Task A_second_submission_answers_409_with_the_already_answered_code()
    {
        var (client, app) = await PracticeAppAsync();
        using (app)
        {
            var response = await client.PostAsJsonAsync(
                "/api/learner/practice/sessions/42/questions/7/answer",
                new SubmitPracticeAnswerRequest(11, null, null, null));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(ApiError.Codes.PracticeAlreadyAnswered, body, StringComparison.Ordinal);
            // The detail is the sentence the learner reads, so it must say what happened and what to do.
            Assert.Contains("already been answered", body, StringComparison.Ordinal);
            Assert.Contains("next question", body, StringComparison.Ordinal);
        }
    }

    private static async Task<(HttpClient Client, WebApplication App)> PracticeAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddSingleton<IPracticeService>(new AnsweredQuestionService());
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
            .GetMethod("MapAnswers", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [practice]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    /// <summary>Refuses every submission the way an already-graded answer does.</summary>
    private sealed class AnsweredQuestionService : IPracticeService
    {
        public Task<GradedPracticeAnswerResult> SubmitAnswerAsync(
            int userId, int sessionId, int questionId, SubmitPracticeAnswerInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GradedPracticeAnswerResult>(new PracticeException(
                PracticeException.AlreadyAnswered,
                $"Question {questionId} has already been answered in this session. Continue with the next question."));

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

        public Task<PracticeSummary> GetSummaryAsync(int userId, int sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PracticeQuestionView>> GetSessionQuestionsAsync(int userId, int sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PracticeSessionTake> GetSessionTakeAsync(int userId, int sessionId, int? displayOrder = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
