using System.Reflection;
using Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WebMVC.Live;

namespace WebMVC.Tests;

/// <summary>
/// #286: <b>the stream's gate, and its behaviour over a real HTTP connection.</b>
/// <para>
/// <b>Why the gate is asserted here and not assumed from the controller attribute.</b> The attribute is on
/// the controller, and the fragment route already proves the policy is enforced on this class. The stream
/// is the one response that stays open and keeps producing data, so it gets its own assertion: an
/// unauthenticated caller must be refused before a single byte of event stream is written.
/// </para>
/// <para>
/// The positive case drives the real route with a stubbed revision source whose second reading differs, so
/// the endpoint is exercised end to end - the controller, the writer and the response - without a database
/// and without waiting for a tick.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class DashboardStreamEndpointTests
{
    /// <summary>
    /// An anonymous request is refused. If this ever passed, the response would be a permanent feed of
    /// revenue movements to anyone who could reach the URL.
    /// </summary>
    [Fact]
    public async Task The_stream_is_refused_to_an_unauthenticated_caller()
    {
        using var site = new Site();
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/admin/dashboard/stream");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("text/event-stream", response.Content.Headers.ContentType?.ToString() ?? string.Empty);
    }

    /// <summary>
    /// A signed-in admin receives event-stream framing. Read as a stream and abandoned after the first
    /// frames, exactly as a browser holds it: waiting for the response to end would wait forever, because a
    /// live stream does not end. The heartbeat cadence is not asserted here - the spec puts that out of test
    /// scope and leaves it to the browser walk.
    /// </summary>
    [Fact]
    public async Task An_admin_receives_event_stream_framing_from_the_real_route()
    {
        using var site = new Site();
        using var client = site.CreateClientForStaff();

        using var response = await client.GetAsync("/admin/dashboard/stream", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no", response.Headers.GetValues("X-Accel-Buffering").Single());

        await using var body = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(body);

        // One change event is enough to prove the route, the writer and the framing are all connected.
        var frame = new System.Text.StringBuilder();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !frame.ToString().Contains("event: revision"))
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            frame.AppendLine(line);
        }

        Assert.Contains("event: revision", frame.ToString());
    }

    /// <summary>The real host, with only the revision source replaced - everything else is production code.</summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ICompanionApiClient>(SilencedApi.Create());
                // Ten milliseconds, not thirty seconds: this exercises the route's framing, not its cadence.
                services.AddSingleton(new DashboardLiveOptions(TimeSpan.FromMilliseconds(10)));
                // Two readings, the second different: one baseline, then one change event, then the
                // enumerable ends and the response completes.
                services.AddScoped<IDashboardRevisionSource>(_ => new ScriptedSource(
                    DashboardRevision.Empty,
                    DashboardRevision.Empty with { PaidOrdersCount = 1, PaidOrdersAmount = 250000m }));
            });
        }

        public HttpClient CreateClientForStaff()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var options = Services.GetRequiredService<
                Microsoft.Extensions.Options.IOptionsMonitor<
                    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
                .Get(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                AuthClaims.For(1, "staff@test.local", Roles.Admin, "Staff", [Permissions.SystemAdmin]),
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                nameType: IAccessTokenFactory.ClaimEmail,
                roleType: IAccessTokenFactory.ClaimRole));

            var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(
                principal,
                new Microsoft.AspNetCore.Authentication.AuthenticationProperties(
                    new Dictionary<string, string?> { ["jwt"] = "token" })
                {
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
                },
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
            return client;
        }
    }

    /// <summary>
    /// Readings with no timer attached: the route under test exercises the writer and the framing, and a
    /// tick schedule here would only make the test wait for a clock. Two readings, the second different, so
    /// the first frame after the baseline is a change.
    /// </summary>
    private sealed class ScriptedSource(params DashboardRevision[] readings) : IDashboardRevisionSource
    {
        private int at;

        public Task<DashboardRevision> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(readings[Math.Min(at++, readings.Length - 1)]);
    }

    /// <summary>
    /// The cookie re-validation calls <c>/api/auth/me</c> on every request; only the two calls this route
    /// makes are answered, so an unexpected one fails loudly rather than silently rendering nothing.
    /// </summary>
    private class SilencedApi : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IAuthClient.GetMeAsync) => Task.FromResult(
                    new AuthUserDto(1, "staff@test.local", "Staff", Roles.Admin, true, [Permissions.SystemAdmin])),
                nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                    new ProfileDto("Staff", null, "Asia/Ho_Chi_Minh")),
                _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
            };

        public static ICompanionApiClient Create() =>
            DispatchProxy.Create<ICompanionApiClient, SilencedApi>();
    }
}
