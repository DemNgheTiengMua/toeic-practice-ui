using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #353: the totals export is a second file, not a second row shape inside the first. Asserted over HTTP so
/// the content type, the attachment filename and the forwarded period are the route's answer rather than a
/// formatter's.
/// </summary>
public sealed class MentorPayoutTotalsExportRouteTests
{
    [Fact]
    public async Task The_totals_export_is_a_csv_attachment_naming_its_period()
    {
        var recorder = new PayoutWindowRecorder();
        var (client, app) = await HostAsync(recorder);
        await using (app)
        {
            var response = await client.GetAsync("/api/admin/mentors/payouts/totals/export?from=2026-11-01&to=2026-11-14");

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

            var disposition = response.Content.Headers.ContentDisposition;
            Assert.Equal("attachment", disposition?.DispositionType);
            Assert.Equal("mentor-payout-totals-2026-11-01-to-2026-11-14.csv", disposition?.FileName?.Trim('"'));

            // The period the caller asked for, so both files can be made for one month.
            Assert.Equal(new DateOnly(2026, 11, 1), recorder.From);
            Assert.Equal(new DateOnly(2026, 11, 14), recorder.To);

            // The byte-order mark survives the wire, not only the formatter.
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        }
    }

    [Fact]
    public async Task The_totals_export_defaults_to_the_current_month_so_an_unbounded_file_is_impossible()
    {
        var recorder = new PayoutWindowRecorder();
        var (client, app) = await HostAsync(recorder);
        await using (app)
        {
            var response = await client.GetAsync("/api/admin/mentors/payouts/totals/export");

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            // Clock is 2026-11-14 12:00 UTC, which is 19:00 on the 14th at UTC+7.
            Assert.Equal(new DateOnly(2026, 11, 1), recorder.From);
            Assert.Equal(new DateOnly(2026, 11, 14), recorder.To);
        }
    }

    [Fact]
    public async Task The_per_session_export_keeps_its_own_name_and_shape()
    {
        var recorder = new PayoutWindowRecorder();
        var (client, app) = await HostAsync(recorder);
        await using (app)
        {
            var response = await client.GetAsync("/api/admin/mentors/payouts/export?from=2026-11-01&to=2026-11-14");

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(
                "mentor-payouts-2026-11-01-to-2026-11-14.csv",
                response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        }
    }

    private static async Task<(HttpClient Client, WebApplication App)> HostAsync(PayoutWindowRecorder recorder)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");

        builder.Services.AddSingleton(CreateMentorService(recorder));
        builder.Services.AddSingleton<TimeProvider>(
            new FixedClock(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero)));
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, AllowStaff>("Test", null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Permissions.SupportTriage, p => p.RequireAuthenticatedUser());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        var admin = app.MapGroup("/api/admin/mentors").RequireAuthorization(Permissions.SupportTriage);
        typeof(MentorEndpoints).GetMethod("MapSlots", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [admin]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private static IMentorService CreateMentorService(PayoutWindowRecorder recorder)
    {
        var proxy = DispatchProxy.Create<IMentorService, RecordingMentorService>();
        ((RecordingMentorService)(object)proxy).Recorder = recorder;
        return proxy;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Records the window the route bound, so the assertion is about the route, not a stub that
    /// restates it.</summary>
    public sealed class PayoutWindowRecorder
    {
        public DateOnly? From { get; set; }

        public DateOnly? To { get; set; }
    }

    /// <summary>
    /// Answers the two payout calls and nothing else: only the totals call returns a mentor, so the file has
    /// a real body. Any other call means the route reached for something it should not, and fails loudly
    /// rather than passing on a default.
    /// </summary>
    public class RecordingMentorService : DispatchProxy
    {
        internal PayoutWindowRecorder? Recorder { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IMentorService.GetMentorPayoutTotalsAsync):
                    Recorder!.From = (DateOnly?)args![0];
                    Recorder.To = (DateOnly?)args[1];
                    return Task.FromResult<IReadOnlyList<MentorPayoutTotalsRow>>(
                    [
                        new MentorPayoutTotalsRow(7, "Dr. Trần Minh Khoa", "TOEIC", 2, 4, 80_000m),
                    ]);

                case nameof(IMentorService.GetMentorPayoutExportRowsAsync):
                    Recorder!.From = (DateOnly?)args![0];
                    Recorder.To = (DateOnly?)args[1];
                    return Task.FromResult<IReadOnlyList<MentorPayoutExportRow>>([]);

                default:
                    throw new NotSupportedException(
                        $"{targetMethod?.Name} is not part of the payout export routes.");
            }
        }
    }

    private sealed class AllowStaff : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public AllowStaff(
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
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
