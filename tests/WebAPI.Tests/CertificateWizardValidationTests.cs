using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services;
using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #147: a certificate whose scoring configuration is invalid must be refused with a 400 that tells
/// the admin what to fix. The validator throws ScoreConversionException, which the wizard endpoint
/// did not catch, so the admin saw an opaque 500 instead of the message naming the gap.
/// <para>
/// Maps the real endpoint group, so it fails if the catch is removed.
/// </para>
/// </summary>
public sealed class CertificateWizardValidationTests
{
    [Fact]
    public async Task A_certificate_with_a_gap_between_bands_answers_400_naming_the_gap()
    {
        var (client, app) = await WizardAppAsync(new GappedBandCertificateService());
        await using (app)
        {
            var response = await client.PostAsJsonAsync("/api/admin/certificates/wizard", RequestBody());
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("SCORE_CONVERSION_INVALID", body, StringComparison.Ordinal);
            // The message has to name the missing range, or the admin cannot fix it.
            Assert.Contains("11", body, StringComparison.Ordinal);
        }
    }

    private static SaveCertificateWizardRequest RequestBody() => new(
        CertId: null, Code: "GAP", Name: "Gap", Description: null,
        ScoreAggregationRule: "sum", PresetCode: "custom", IsActive: true,
        Skills: [],
        MinScaledScore: 0, MaxScaledScore: 50, ScoreStep: 1,
        Bands:
        [
            new SaveScoreBandWizardDto(null, "FAIL", "Fail", 0, 10, 1),
            new SaveScoreBandWizardDto(null, "PASS", "Pass", 20, 50, 2),
        ],
        Criteria: []);

    private static async Task<(HttpClient Client, WebApplication App)> WizardAppAsync(ICertificateService service)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
                builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddSingleton(service);
        builder.Services.AddProblemDetails();
        // The real group requires the admin role; this test is about the response to an invalid
        // configuration, not about authorization.
        builder.Services.AddAuthentication("Test").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AllowAll>("Test", null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Roles.Admin, policy => policy.RequireRole(Roles.Admin));

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Roles.Admin);
        typeof(AdminEndpoints)
            .GetMethod("MapCertificates", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [admin]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    /// <summary>Authenticates every request and grants the admin role.</summary>
    private sealed class AllowAll : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>
    {
        public AllowAll(
            Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, Roles.Admin)],
                "Test");
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(
                new Microsoft.AspNetCore.Authentication.AuthenticationTicket(
                    new System.Security.Claims.ClaimsPrincipal(identity), "Test")));
        }
    }

    /// <summary>Runs the real validator so the message under test is the production one.</summary>
    private sealed class GappedBandCertificateService : ICertificateService
    {
        public Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(
            SaveCertificateWizardRequest request, CancellationToken cancellationToken = default)
        {
            CertificateConfigurationValidator.Validate(request);
            throw new InvalidOperationException("the validator should have refused this request");
        }

        public Task<IReadOnlyList<AdminCertificateSummaryDto>> GetSummariesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BusinessObjects.CertificateCatalog>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IReadOnlyList<CertificateBlueprintPresetDto> GetOfficialPresets() =>
            throw new NotSupportedException();

        public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(string code, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
