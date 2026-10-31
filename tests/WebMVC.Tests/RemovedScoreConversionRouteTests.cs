using System.Net;
using System.Reflection;
using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WebMVC.Tests;

/// <summary>
/// #147: score configuration belongs to the certificate. The wizard writes ScoreScale, ScoreBand and
/// GradingCriteria, so the separate admin screen has no reason to exist — and leaving it reachable
/// would let an admin edit the same rows from a second place, which is how the certificate and its
/// bands drift apart.
/// <para>
/// Drives the real host, so it fails if the controller comes back for any reason.
/// </para>
/// </summary>
public sealed class RemovedScoreConversionRouteTests : IClassFixture<RemovedScoreConversionRouteTests.Site>
{
    private readonly Site site;

    public RemovedScoreConversionRouteTests(Site site) => this.site = site;

    [Theory]
    [InlineData("/admin/score-conversion")]
    [InlineData("/admin/score-conversions")]
    public async Task The_score_conversion_screen_is_gone(string path)
    {
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", site.AdminCookie());

        var response = await client.GetAsync(path);

        // No route matches, so the request never reaches a controller. The app has no status-code
        // pages, so a dead admin URL surfaces through the exception handler as 500 — the point is
        // that it is not the score-conversion page any more, never a 200 with its form.
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Score conversion", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The real WebMVC host with the outbound API replaced, so nothing leaves the process.</summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ICompanionApiClient>(UpstreamStub.Create().Client));
        }

        /// <summary>A signed-in admin cookie, so a surviving route would answer 200 rather than 302.</summary>
        public string AdminCookie()
        {
            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                AuthClaims.For(1, "admin@test.local", Roles.Admin, "Admin"),
                CookieAuthenticationDefaults.AuthenticationScheme));
            var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" })
            {
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
            };
            var ticket = new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme);

            return $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
        }
    }

    /// <summary>The API is never reached by a 404; anything else calling it is a regression.</summary>
    public class UpstreamStub : DispatchProxy
    {
        public ICompanionApiClient Client { get; private set; } = null!;

        public static UpstreamStub Create()
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, UpstreamStub>();
            var stub = (UpstreamStub)(object)proxy;
            stub.Client = proxy;
            return stub;
        }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}");
    }
}
