using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
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

/// <summary>
/// #365: a learner plays their own spoken recording back inside the result page. The browser records
/// <c>audio/webm</c>, so the endpoint must answer with that media type and no <c>attachment</c>
/// disposition; otherwise <c>&lt;audio&gt;</c> renders nothing and the browser downloads the file.
/// Asserted over HTTP, because both are properties of the response the browser sees.
/// </summary>
public sealed class SpeakingAudioPlaybackTests
{
    [Fact]
    public async Task A_webm_recording_is_served_inline_as_audio_the_browser_plays()
    {
        var bytes = Encoding.UTF8.GetBytes("webm-opus-take");
        var (client, app) = await HostAsync(() => new FileStreamResultInfo(new MemoryStream(bytes), "audio/webm", "take.webm"));
        await using (app)
        {
            var response = await client.GetAsync("/api/learner/speaking/attempts/10/questions/3/audio");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("audio/webm", response.Content.Headers.ContentType?.MediaType);

            // Not "attachment": a download prompt means the learner never hears the recording.
            Assert.NotEqual("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task Another_learners_recording_answers_403_and_a_missing_one_answers_404()
    {
        var (client, app) = await HostAsync(
            () => throw new SpeakingPracticeException(SpeakingPracticeException.Forbidden, "not yours"));
        await using (app)
        {
            var response = await client.GetAsync("/api/learner/speaking/attempts/11/questions/3/audio");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        var (missingClient, missingApp) = await HostAsync(
            () => throw new SpeakingPracticeException(SpeakingPracticeException.AudioNotFound, "gone"));
        await using (missingApp)
        {
            var response = await missingClient.GetAsync("/api/learner/speaking/attempts/12/questions/3/audio");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static async Task<(HttpClient Client, WebApplication App)> HostAsync(Func<FileStreamResultInfo> audio)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddSingleton<ISpeakingPracticeService>(FakeSpeakingService.Create(audio));
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, LearnerAuth>("Test", null);
        builder.Services.AddAuthorization(options =>
            options.AddPolicy(Roles.Learner, policy => policy.RequireRole("learner")));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        var learner = app.MapGroup("/api/learner/speaking").RequireAuthorization(Roles.Learner);
        typeof(SpeakingEndpoints).GetMethod("MapAudioRoutes", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [learner]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private sealed class LearnerAuth(
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

    private class FakeSpeakingService : DispatchProxy
    {
        private Func<FileStreamResultInfo> _audio = null!;

        public static ISpeakingPracticeService Create(Func<FileStreamResultInfo> audio)
        {
            var proxy = Create<ISpeakingPracticeService, FakeSpeakingService>();
            ((FakeSpeakingService)(object)proxy)._audio = audio;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(ISpeakingPracticeService.GetAudioFileAsync)
                ? Task.FromResult<FileStreamResultInfo?>(_audio())
                : throw new NotImplementedException(targetMethod?.Name);
    }
}
