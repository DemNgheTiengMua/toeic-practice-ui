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
/// #200 criterion 3 through the real pipeline, not only the authorization service: a staff route
/// admits an account holding the capability it names and refuses one holding a different capability.
/// The defect this catches is a gate that looks right in metadata but is decided from the role name
/// at request time — the shape the whole ticket exists to remove.
/// </summary>
public sealed class StaffRouteCapabilityEndToEndTests : IClassFixture<StaffRouteCapabilityEndToEndTests.Site>
{
    private readonly Site site;

    public StaffRouteCapabilityEndToEndTests(Site site) => this.site = site;

    [Fact]
    public async Task An_account_holding_the_capability_reaches_the_route()
    {
        using var client = site.ClientFor([Permissions.ContentAuthor]);

        var response = await client.GetAsync("/admin/exams");

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("/auth/denied", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task An_account_holding_a_different_capability_is_refused()
    {
        using var client = site.ClientFor([Permissions.ContentReview]);

        var response = await client.GetAsync("/admin/exams");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/auth/denied", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task An_account_holding_no_capability_is_refused()
    {
        using var client = site.ClientFor([]);

        var response = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/auth/denied", response.Headers.Location?.ToString() ?? "");
    }

    public sealed class Site : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services => services.AddSingleton(Upstream.Create().Client));
        }

        /// <summary>A cookie exactly as sign-in issues it: the capability claims plus the JWT in the
        /// ticket properties, protected by the scheme's own format.</summary>
        public HttpClient ClientFor(IReadOnlyList<string> permissions)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                AuthClaims.For(1, "staff@test.local", Roles.Admin, "Staff", permissions),
                CookieAuthenticationDefaults.AuthenticationScheme,
                nameType: IAccessTokenFactory.ClaimEmail,
                roleType: IAccessTokenFactory.ClaimRole));
            var ticket = new AuthenticationTicket(
                principal,
                new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" })
                {
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
                },
                CookieAuthenticationDefaults.AuthenticationScheme);

            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
            Upstream.Permissions = permissions;
            return client;
        }
    }

    /// <summary>Cookie re-validation calls /api/auth/me; the response must agree with the cookie or
    /// the principal is rejected before the gate is ever consulted.</summary>
    public class Upstream : DispatchProxy
    {
        public static IReadOnlyList<string> Permissions { get; set; } = [];

        public ICompanionApiClient Client { get; private set; } = null!;

        public static Upstream Create()
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, Upstream>();
            var stub = (Upstream)(object)proxy;
            stub.Client = proxy;
            return stub;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IAuthClient.GetMeAsync) => Task.FromResult(
                    new AuthUserDto(1, "staff@test.local", "Staff", Roles.Admin, true, Permissions)),
                nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                    new ProfileDto("Staff", null, "Asia/Ho_Chi_Minh")),
                nameof(IAdminExamsClient.GetAdminExamsAsync) => Task.FromResult(
                    new AdminExamListSummaryDto([], 0, 1, 20, [])),
                nameof(IAdminUsersClient.GetAdminUsersAsync) => Task.FromResult(
                    new AdminUsersSummaryDto([], 0, 1, 20, 0, 0, 0)),
                _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
            };
    }
}
