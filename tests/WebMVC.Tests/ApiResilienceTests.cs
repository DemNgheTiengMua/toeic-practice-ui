using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebMVC.Infrastructure;

namespace WebMVC.Tests;

/// <summary>
/// #140: a degraded page must render in seconds, not the ~12 s three failed calls used to cost,
/// and later pages during the same outage must fail at once. These drive the real host — real
/// middleware, real cookie scheme, real typed API client and its resilience pipeline — with only
/// the socket under the client replaced, so they assert what a user gets rather than handler state.
/// </summary>
public sealed class ApiResilienceTests : IClassFixture<ApiResilienceTests.Site>
{
    private readonly Site site;

    public ApiResilienceTests(Site site)
    {
        this.site = site;
        this.site.Transport.Reset();
    }

    [Fact]
    public async Task A_degraded_page_renders_quickly_and_costs_one_failed_connection()
    {
        site.Transport.Mode = TransportMode.Refuse;

        var stopwatch = Stopwatch.StartNew();
        var response = await site.CreateClient(new() { AllowAutoRedirect = false })
            .SendAsync(site.SignedInGet("/profile"));
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "couldn't load your profile",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        // Fail-fast is what turns three 4 s failures into one: the other two calls never reach
        // the transport.
        Assert.Equal(1, site.Transport.Attempts);
        // Generous against machine speed; the point is that it is one connect budget, not three.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(6),
            $"degraded page took {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task Pages_stop_attempting_the_api_once_the_breaker_opens()
    {
        site.Transport.Mode = TransportMode.Refuse;
        using var client = site.CreateClient(new() { AllowAutoRedirect = false });

        // One failed connection per page; the breaker opens on the third consecutive outage page.
        for (var page = 0; page < 3; page++)
        {
            await client.SendAsync(site.SignedInGet("/profile"));
        }

        site.Transport.Reset();
        var response = await client.SendAsync(site.SignedInGet("/profile"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, site.Transport.Attempts);
    }

    [Fact]
    public async Task The_site_recovers_on_its_own_once_the_api_answers_again()
    {
        site.Transport.Mode = TransportMode.Refuse;
        using var client = site.CreateClient(new() { AllowAutoRedirect = false });
        for (var page = 0; page < 3; page++)
        {
            await client.SendAsync(site.SignedInGet("/profile"));
        }

        // Past the break window the next call probes; an answering API closes the breaker.
        site.Clock.Advance(site.Options.BreakDuration + TimeSpan.FromSeconds(1));
        site.Transport.Mode = TransportMode.Answer;
        var response = await client.SendAsync(site.SignedInGet("/profile"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(
            "couldn't load your profile",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_api_that_accepts_connections_but_never_answers_is_cut_off()
    {
        site.Transport.Mode = TransportMode.Hang;

        var stopwatch = Stopwatch.StartNew();
        var response = await site.CreateClient(new() { AllowAutoRedirect = false })
            .SendAsync(site.SignedInGet("/profile"));
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            stopwatch.Elapsed < site.Options.HeaderTimeout + TimeSpan.FromSeconds(5),
            $"a hanging API held the page for {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task An_error_answer_from_a_live_api_is_handled_as_before_and_is_not_an_outage()
    {
        site.Transport.Mode = TransportMode.ErrorAnswer;

        using var client = site.CreateClient(new() { AllowAutoRedirect = false });
        for (var page = 0; page < 4; page++)
        {
            var response = await client.SendAsync(site.SignedInGet("/profile"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Four pages, three upstream calls each (re-validation, time-zone lookup, the page
        // itself): every one reached the API. An error status means the API is up, so the
        // breaker never opened and no page was served the fail-fast path.
        Assert.Equal(12, site.Transport.Attempts);
    }

    [Fact]
    public async Task A_dead_session_still_signs_the_user_out()
    {
        site.Transport.Mode = TransportMode.Locked;

        var response = await site.CreateClient(new() { AllowAutoRedirect = false })
            .SendAsync(site.SignedInGet("/profile"));

        // The refusal must still end the session rather than be swallowed by the fail-fast path.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/auth/sign-in", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The real MVC host with only the socket under the API client replaced.</summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        public FakeTransport Transport { get; } = new();

        public ManualTime Clock { get; } = new();

        public ApiResilienceOptions Options => new()
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(300),
            HeaderTimeout = TimeSpan.FromMilliseconds(700),
            FailureThreshold = 3,
            BreakDuration = TimeSpan.FromSeconds(5),
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.UseSetting("ApiResilience:ConnectTimeoutMs", "300");
            builder.UseSetting("ApiResilience:HeaderTimeoutMs", "700");
            builder.UseSetting("ApiResilience:FailureThreshold", "3");
            builder.UseSetting("ApiResilience:BreakDurationMs", "5000");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(Clock);
                // Replacing the primary handler keeps the real resilience pipeline, typed client
                // and CookieBearerHandler in play: the fake sits exactly where the socket was.
                services.AddHttpClient<ICompanionApiClient, CompanionApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => Transport);
            });
        }

        /// <summary>A request that carries the auth cookie exactly as sign-in issues it.</summary>
        public HttpRequestMessage SignedInGet(string path)
        {
            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                AuthClaims.For(7, "learner@test.local", Roles.Learner, "Learner"),
                CookieAuthenticationDefaults.AuthenticationScheme));
            var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" })
            {
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
            };
            var ticket = new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme);

            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add(
                "Cookie",
                $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
            return request;
        }
    }

    public enum TransportMode
    {
        Answer,
        Refuse,
        Hang,
        ErrorAnswer,
        Locked,
    }

    /// <summary>Stands in for the network: counts attempts and fails, hangs or answers on demand.</summary>
    public sealed class FakeTransport : HttpMessageHandler
    {
        private int attempts;

        public TransportMode Mode { get; set; } = TransportMode.Answer;

        public int Attempts => Volatile.Read(ref attempts);

        public void Reset() => Interlocked.Exchange(ref attempts, 0);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attempts);

            // Cookie re-validation (/auth/me) must keep succeeding in every mode but Locked, or
            // the principal is rejected before the page under test ever runs.
            var isRevalidation = request.RequestUri!.AbsolutePath.EndsWith("/auth/me", StringComparison.Ordinal);

            switch (Mode)
            {
                case TransportMode.Refuse:
                    throw new HttpRequestException("Connection refused", new SocketExceptionStub());
                case TransportMode.Hang:
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    throw new UnreachableException();
                case TransportMode.Locked:
                    return Problem(HttpStatusCode.Forbidden, ApiError.Codes.Locked);
                case TransportMode.ErrorAnswer when !isRevalidation:
                    return Problem(HttpStatusCode.InternalServerError, ApiError.Codes.Unexpected);
                default:
                    return Answered(request.RequestUri!);
            }
        }

        private static HttpResponseMessage Answered(Uri uri)
        {
            // Enough shape for the two calls a signed-in page makes: /api/auth/me and
            // /api/profile. Everything else gets an empty object.
            var body = uri.AbsolutePath switch
            {
                var p when p.EndsWith("/auth/me", StringComparison.Ordinal) =>
                    $$"""{"id":7,"email":"learner@test.local","fullName":"Learner","role":"{{Roles.Learner}}"}""",
                var p when p.EndsWith("/profile", StringComparison.Ordinal) =>
                    """{"fullName":"Learner","phone":null,"timeZoneId":"Asia/Ho_Chi_Minh"}""",
                _ => "{}",
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }

        private static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
            new(status)
            {
                Content = new StringContent(
                    $$"""{"code":"{{code}}","title":"failure"}""",
                    System.Text.Encoding.UTF8,
                    "application/problem+json"),
            };
    }

    /// <summary>Deterministic clock: the breaker's break window is the only clock it reads.</summary>
    public sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new NotSupportedException("The resilience pipeline only reads the wall clock.");
    }

    private sealed class SocketExceptionStub : Exception
    {
        public SocketExceptionStub() : base("A connection attempt failed") { }
    }
}
