using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BusinessObjects;
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
using WebAPI.Extensions;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #351: the dashboard route carries a chosen month through to the service, and its absence stays
/// "whatever the server's clock says". The route is the seam the screen actually crosses; asserting the
/// service directly would pass even if the parameter were dropped on the way in.
/// </summary>
public sealed class AdminDashboardRouteTests
{
    [Fact]
    public async Task A_month_on_the_query_reaches_the_service_intact()
    {
        var recorded = new RecordingDashboard();
        var (client, app) = await DashboardAppAsync(recorded);
        await using (app)
        {
            var response = await client.GetAsync("/api/admin/dashboard?month=2026-09-01");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new DateOnly(2026, 9, 1), recorded.LastMonth);
        }
    }

    [Fact]
    public async Task No_month_on_the_query_asks_the_service_for_the_current_period()
    {
        var recorded = new RecordingDashboard();
        var (client, app) = await DashboardAppAsync(recorded);
        await using (app)
        {
            var response = await client.GetAsync("/api/admin/dashboard");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(recorded.LastMonth);
        }
    }

    private sealed class RecordingDashboard : IAdminDashboardService
    {
        public DateOnly? LastMonth { get; private set; }

        public Task<AdminDashboardDto> GetDashboardMetricsAsync(
            DateOnly? month = null,
            CancellationToken cancellationToken = default)
        {
            LastMonth = month;
            return Task.FromResult(AdminDashboardDto.Empty with { PeriodLabel = "September 2026" });
        }
    }

    private sealed class StubRevision : IDashboardRevisionService
    {
        public Task<DashboardRevision> GetRevisionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardRevision(0, 0m, 0, 0, 0, 0, 0, 0, 0, 0, 0m));
    }

    private static async Task<(HttpClient Client, WebApplication App)> DashboardAppAsync(IAdminDashboardService dashboard)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");

        builder.Services.AddSingleton(dashboard);
        // MapDashboard also maps the revision route beside it, and that sibling needs its own service.
        builder.Services.AddSingleton<IDashboardRevisionService, StubRevision>();

        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, AllowAdmin>("Test", null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Permissions.SystemAdmin, p => p.RequireAuthenticatedUser());

        var app = builder.Build();
        app.UseApiProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();

        var adminSystem = app.MapGroup("/api/admin").RequireAuthorization(Permissions.SystemAdmin);
        typeof(AdminEndpoints).GetMethod("MapDashboard", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [adminSystem]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private sealed class AllowAdmin : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public AllowAdmin(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(IAccessTokenFactory.ClaimUserId, "1")],
                "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
