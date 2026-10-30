using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WebMVC;

namespace WebMVC.Tests;

/// <summary>
/// F3: a static asset must never reach cookie authentication. UseAuthentication re-validates the
/// cookie principal on every request it sees (OnValidatePrincipal -> GET /api/auth/me), and
/// TimeZoneMiddleware follows with GET /api/profile, so each asset that falls through the
/// pipeline costs two upstream API calls. Program.cs pairs MapStaticAssets() with ShortCircuit()
/// so routing dispatches the asset inside UseRouting instead; these tests prove the bytes still
/// come back and that the upstream client is never touched.
/// </summary>
public sealed class StaticAssetPipelineTests : IClassFixture<StaticAssetPipelineTests.Site>
{
    private readonly Site site;

    public StaticAssetPipelineTests(Site site) => this.site = site;

    [Theory]
    [InlineData("/css/base.css")]
    [InlineData("/js/icons.js")]
    [InlineData("/img/favicon.svg")]
    public async Task A_static_asset_is_served_without_touching_the_upstream_api(string path)
    {
        site.Upstream.Reset();

        var response = await site.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        Assert.Empty(site.Upstream.Calls);
        Assert.Empty(site.Auth.AuthenticatedSchemes);
    }

    /// <summary>
    /// The crawl's actual shape: a signed-in learner's layout pulls css and js. UseAuthentication
    /// is what runs OnValidatePrincipal (hence GET /api/auth/me) and what populates HttpContext.User
    /// for TimeZoneMiddleware (hence GET /api/profile). Recording zero AuthenticateAsync calls
    /// therefore proves neither upstream call can be made — which is the 34,000-line flood.
    /// </summary>
    [Theory]
    [InlineData("/css/base.css")]
    [InlineData("/js/icons.js")]
    public async Task A_signed_in_learner_loading_an_asset_never_invokes_authentication(string path)
    {
        site.Auth.SignedIn = true;
        site.Upstream.Reset();
        site.Auth.Reset();

        var response = await site.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        Assert.Empty(site.Auth.AuthenticatedSchemes);
        Assert.Empty(site.Upstream.Calls);
    }

    [Fact]
    public async Task A_page_still_runs_authentication_so_the_short_circuit_is_asset_only()
    {
        site.Auth.SignedIn = false;
        site.Upstream.Reset();
        site.Auth.Reset();

        var response = await site.CreateClient().GetAsync("/");

        // The home page is anonymous, but authentication still ran for it: the short-circuit is
        // scoped to asset endpoints, not to the site.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(site.Auth.AuthenticatedSchemes);
    }

    /// <summary>
    /// The real WebMVC pipeline with only the outbound API swapped for a recorder. Everything
    /// else — middleware order, routing, MapStaticAssets, the cookie scheme — is production code
    /// under test, which is the point: a hand-built pipeline would prove nothing about Program.cs.
    /// </summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        public RecordingUpstream Upstream { get; } = RecordingUpstream.Create();

        public RecordingAuthenticationService Auth { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAuthenticationService>(Auth);
                // Every segregated client in Program.cs resolves through ICompanionApiClient, so
                // replacing that one registration swaps the whole upstream surface. Last
                // registration wins over the typed HttpClient there.
                services.AddSingleton<ICompanionApiClient>(Upstream.Client);
            });
        }
    }

    /// <summary>Records the upstream calls the pipeline made, keyed by client method name.</summary>
    public class RecordingUpstream : DispatchProxy
    {
        private readonly ConcurrentDictionary<string, int> calls = new();

        public IReadOnlyCollection<string> Calls => calls.Keys.ToList();

        public ICompanionApiClient Client { get; private set; } = null!;

        public static RecordingUpstream Create()
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, RecordingUpstream>();
            var recorder = (RecordingUpstream)(object)proxy;
            recorder.Client = proxy;
            return recorder;
        }

        public void Reset() => calls.Clear();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            calls.AddOrUpdate(targetMethod?.Name ?? "?", 1, (_, n) => n + 1);
            // Nothing these tests fetch reads data: an upstream call here is the defect, not
            // something to answer plausibly.
            throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}");
        }
    }

    public sealed class RecordingAuthenticationService : IAuthenticationService
    {
        private readonly ConcurrentBag<string?> authenticated = new();

        /// <summary>When true, authentication reports a signed-in principal — the state in which
        /// OnValidatePrincipal would go on to call GetMeAsync.</summary>
        public bool SignedIn { get; set; }

        public IReadOnlyCollection<string?> AuthenticatedSchemes => authenticated.ToList();

        public void Reset() => authenticated.Clear();

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            authenticated.Add(scheme);
            return Task.FromResult(SignedIn
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(IAccessTokenFactory.ClaimRole, Roles.Learner)], "Cookies")),
                    new AuthenticationProperties(), scheme ?? "Cookies"))
                : AuthenticateResult.NoResult());
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? props) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
    }
}
