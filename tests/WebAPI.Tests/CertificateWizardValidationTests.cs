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

    /// <summary>
    /// #356: the wizard now derives its default bands from the rule and the skill count, so a
    /// two-skill <c>sum</c> certificate is tiled 0-200 rather than 0-100. Each case posts the four
    /// bands <c>deriveDefaultBands</c> produces and expects the real validator to accept them.
    /// </summary>
    [Theory]
    [InlineData("sum", 2)]
    [InlineData("sum", 4)]
    [InlineData("avg_half_step", 2)]
    [InlineData("avg_half_step", 4)]
    [InlineData("avg_whole", 4)]
    public async Task The_wizards_derived_bands_are_accepted_for_every_rule_it_offers(
        string rule, int skillCount)
    {
        var (client, app) = await WizardAppAsync(new AcceptingCertificateService());
        await using (app)
        {
            var skills = Enumerable.Range(1, skillCount)
                .Select(i => new SaveSkillWizardDto(
                    null, $"SKILL{i}", $"Skill {i}", ModalityDto.Listening, i, 30,
                    [new SaveSectionWizardDto("PART1", "Part 1", 10)]))
                .ToList();

            var request = new SaveCertificateWizardRequest(
                CertId: null, Code: "OK", Name: "Accepted", Description: null,
                ScoreAggregationRule: rule, PresetCode: "custom", IsActive: true,
                Skills: skills,
                MinScaledScore: 0, MaxScaledScore: 100, ScoreStep: 1,
                Bands: DerivedBands(rule, skillCount),
                Criteria: []);

            var response = await client.PostAsJsonAsync("/api/admin/certificates/wizard", request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"code\":\"OK\"", body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Mirrors <c>deriveDefaultBands</c> in CertificatesStudioPage.tsx: the same total-range rule the
    /// validator applies, split into four contiguous bands. If the two disagree the theory above
    /// fails, which is the point.
    /// </summary>
    private static List<SaveScoreBandWizardDto> DerivedBands(string rule, int skillCount)
    {
        var (low, high) = rule == "sum" ? (0, 100 * skillCount) : (0, 100);
        var span = high - low + 1;
        var count = Math.Min(4, span);
        var size = (int)Math.Ceiling(span / (double)count);
        var labels = new[] { ("A2", "Elementary"), ("B1", "Intermediate"), ("B2", "Upper"), ("C1", "Advanced") };

        return Enumerable.Range(0, count)
            .Select(i => new SaveScoreBandWizardDto(
                null, labels[i].Item1, labels[i].Item2,
                low + i * size,
                i == count - 1 ? high : Math.Min(low + (i + 1) * size - 1, high),
                i + 1))
            .ToList();
    }

    /// <summary>
    /// The defect #356 reports: the wizard's old hardcoded 0-40/41-65/66-85/86-100 bands were never
    /// rescaled for a <c>sum</c> over two skills, so the save was refused with a gap at the top.
    /// </summary>
    [Fact]
    public async Task The_old_hardcoded_bands_still_leave_a_gap_at_the_top_for_a_two_skill_sum()
    {
        var (client, app) = await WizardAppAsync(new GappedBandCertificateService());
        await using (app)
        {
            var request = new SaveCertificateWizardRequest(
                CertId: null, Code: "GAPTOP", Name: "Gap top", Description: null,
                ScoreAggregationRule: "sum", PresetCode: "two_skill", IsActive: true,
                Skills:
                [
                    new SaveSkillWizardDto(null, "LISTENING", "Listening", ModalityDto.Listening, 1, 45,
                        [new SaveSectionWizardDto("PART1", "Part 1", 25)]),
                    new SaveSkillWizardDto(null, "READING", "Reading", ModalityDto.Reading, 2, 60,
                        [new SaveSectionWizardDto("PART5", "Part 1", 25)]),
                ],
                MinScaledScore: 0, MaxScaledScore: 100, ScoreStep: 1,
                Bands:
                [
                    new SaveScoreBandWizardDto(null, "A2", "Elementary", 0, 40, 1),
                    new SaveScoreBandWizardDto(null, "B1", "Intermediate", 41, 65, 2),
                    new SaveScoreBandWizardDto(null, "B2", "Upper", 66, 85, 3),
                    new SaveScoreBandWizardDto(null, "C1", "Advanced", 86, 100, 4),
                ],
                Criteria: []);

            var response = await client.PostAsJsonAsync("/api/admin/certificates/wizard", request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("gap at the top", body, StringComparison.Ordinal);
        }
    }

    /// <summary>Runs the real validator, then answers success — the wizard's happy path.</summary>
    private sealed class AcceptingCertificateService : ICertificateService
    {
        public Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(
            SaveCertificateWizardRequest request, CancellationToken cancellationToken = default)
        {
            CertificateConfigurationValidator.Validate(request);
            return Task.FromResult(new SaveCertificateWizardResultDto(
                1, request.Code, request.Name, request.Skills.Count, 1));
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
