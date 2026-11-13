using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
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
/// #290: Placement screen states, confirmation screen (scope choice, pricing, short-balance refusal,
/// idempotence), and two-skill vs four-skill result reporting, driven through the real Razor host.
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class PlacementRetakeScreensTests
{
    private static readonly DateTimeOffset Taken = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Never_sat_shows_start_action_free_and_no_retake_price()
    {
        var catalog = Catalog(isRetake: false, activePlacementId: null, allCredits: 7, lrCredits: 4, balance: null);
        await using var site = new Site(catalog: () => catalog);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement");

        Assert.Contains("Start placement test", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/placement/start\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Retake placement test", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/placement/preflight", html, StringComparison.Ordinal);
        Assert.DoesNotContain("7 credits", html, StringComparison.Ordinal);
        Assert.DoesNotContain("4 credits", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sat_before_shows_retake_action_opening_confirmation_screen()
    {
        var catalog = Catalog(isRetake: true, activePlacementId: null, currentBand: "B1", currentScore: 600);
        await using var site = new Site(catalog: () => catalog);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement");

        Assert.Contains("Retake placement test", html, StringComparison.Ordinal);
        Assert.Contains("/placement/preflight?examId=1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Start placement test", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Free, no credit deducted", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_progress_shows_resume_action_even_after_prior_sitting()
    {
        var catalog = Catalog(isRetake: true, activePlacementId: 55, currentBand: "B1", currentScore: 600);
        await using var site = new Site(catalog: () => catalog);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement");

        Assert.Contains("Resume in-progress test", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/placement/take/55\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Retake placement test", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Start placement test", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Free, no credit deducted", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirmation_screen_presents_both_scopes_unselected_with_configured_prices_and_excluded_skills()
    {
        var gate = FourSkillGate(isRetake: true, allCredits: 7, lrCredits: 4, balance: 10);
        await using var site = new Site(gate: () => gate);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/preflight?examId=10");

        // Both scope choices are rendered with API-configured prices (non-default 7 and 4 to prove no hardcoding)
        Assert.Contains("All four skills", html, StringComparison.Ordinal);
        Assert.Contains("Listening and Reading only", html, StringComparison.Ordinal);
        Assert.Contains("7 credits", html, StringComparison.Ordinal);
        Assert.Contains("4 credits", html, StringComparison.Ordinal);

        // Neither radio is pre-selected as a default
        var scopeRadios = Regex.Matches(html, @"<input\b[^>]*name=""scope""[^>]*>");
        Assert.Equal(2, scopeRadios.Count);
        foreach (Match radio in scopeRadios)
        {
            Assert.DoesNotContain("checked", radio.Value, StringComparison.OrdinalIgnoreCase);
        }

        // Excluded skills (Speaking, Writing) are named, along with idempotency key and external JS
        Assert.Contains("Speaking", html, StringComparison.Ordinal);
        Assert.Contains("Writing", html, StringComparison.Ordinal);
        Assert.Contains("name=\"idempotencyKey\"", html, StringComparison.Ordinal);
        Assert.Contains("preflight.js", html, StringComparison.Ordinal);
        // Audio equipment panel still renders when required by offers
        Assert.Contains("data-audio-equipment", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirmation_screen_refuses_when_balance_is_short_for_all_scopes_and_offers_pricing()
    {
        var gate = FourSkillGate(isRetake: true, allCredits: 7, lrCredits: 4, balance: 2);
        await using var site = new Site(gate: () => gate);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/preflight");

        Assert.Contains("href=\"/pricing\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("action=\"/placement/start\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirmation_screen_disables_only_unaffordable_scope_when_partial_balance_covers_two_skills()
    {
        var gate = FourSkillGate(isRetake: true, allCredits: 7, lrCredits: 4, balance: 4);
        await using var site = new Site(gate: () => gate);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/preflight");

        Assert.Contains("href=\"/pricing\"", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/placement/start\"", html, StringComparison.Ordinal);

        var allSkillsInput = Regex.Match(html, @"<input\b[^>]*value=""all_skills""[^>]*>").Value;
        var lrInput = Regex.Match(html, @"<input\b[^>]*value=""listening_and_reading""[^>]*>").Value;
        Assert.Contains("disabled", allSkillsInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("disabled", lrInput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_skill_result_shows_only_two_rows_states_it_covered_two_skills_and_shows_no_zeros()
    {
        var dto = ScopedResult(PlacementScopeDto.ListeningAndReading, includeProductive: false);
        await using var site = new Site(result: () => dto);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        var rows = Regex.Matches(html, @"data-skill-state=""[^""]+""");
        Assert.Equal(2, rows.Count);
        Assert.Contains("covered 2 skills", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 skills covered", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A2 (10/100)", html, StringComparison.Ordinal);

        var table = Regex.Match(html, @"<table class=""signboard__horizon"">(?<t>[\s\S]*?)</table>").Groups["t"].Value;
        Assert.Contains("Listening", table, StringComparison.Ordinal);
        Assert.Contains("Reading", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Speaking", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Writing", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Not measured", table, StringComparison.Ordinal);
        Assert.DoesNotContain("(0/100)", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_does_not_filter_in_scope_skills_by_english_name()
    {
        var baseDto = ScopedResult(PlacementScopeDto.ListeningAndReading, includeProductive: false);
        var localized = baseDto with
        {
            SkillScores =
            [
                baseDto.SkillScores[0] with { SkillName = "Nghe hiểu" },
                baseDto.SkillScores[1] with { SkillName = "Đọc hiểu" },
            ],
            SkillResults =
            [
                baseDto.SkillResults![0] with { SkillName = "Nghe hiểu" },
                baseDto.SkillResults[1] with { SkillName = "Đọc hiểu" },
            ],
        };

        await using var site = new Site(result: () => localized);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        var rows = Regex.Matches(html, @"data-skill-state=""[^""]+""");
        Assert.Equal(2, rows.Count);
        var decoded = System.Net.WebUtility.HtmlDecode(html);
        Assert.Contains("Nghe hiểu", decoded, StringComparison.Ordinal);
        Assert.Contains("Đọc hiểu", decoded, StringComparison.Ordinal);

    }

    [Fact]
    public async Task Four_skill_result_renders_all_four_rows_without_two_skill_banner()
    {
        var dto = ScopedResult(PlacementScopeDto.AllSkills, includeProductive: true);
        await using var site = new Site(result: () => dto);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        var rows = Regex.Matches(html, @"data-skill-state=""[^""]+""");
        Assert.Equal(4, rows.Count);
        Assert.DoesNotContain("covered 2 skills", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2 skills covered", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A2 (10/100)", html, StringComparison.Ordinal);
    }

    private static PlacementCatalogViewDto Catalog(
        bool isRetake,
        int? activePlacementId,
        string? currentBand = null,
        int? currentScore = null,
        int allCredits = 5,
        int lrCredits = 3,
        int? balance = 10) =>
        new(
            new CertificateOptionDto("TOEIC", "TOEIC"),
            currentBand,
            currentBand is null ? null : "Intermediate",
            currentScore,
            [
                new PlacementCatalogItemDto(1, 1, "TOEIC", "TOEIC Placement", "Diagnostic", 45, 40, true, activePlacementId)
            ],
            activePlacementId,
            activePlacementId.HasValue ? 99 : null,
            IsRetake: isRetake,
            AllSkillsRetakeCredits: allCredits,
            ListeningAndReadingRetakeCredits: lrCredits,
            CreditBalance: balance);

    private static PlacementPreflightViewDto FourSkillGate(
        bool isRetake,
        int allCredits,
        int lrCredits,
        int? balance,
        bool startable = true) =>
        new(
            CertId: 1,
            CertCode: "TOEIC",
            ExamId: 10,
            Offers:
            [
                new PlacementSkillOfferDto(1, "LIST", "Listening", "listening", false, true, null, null, null, null, false, false),
                new PlacementSkillOfferDto(2, "READ", "Reading", "reading", false, false, null, null, null, null, false, false),
                new PlacementSkillOfferDto(3, "SPEA", "Speaking", "speaking", true, false, null, null, null, null, false, false),
                new PlacementSkillOfferDto(4, "WRIT", "Writing", "writing", false, false, null, null, null, null, false, false),
            ],
            CooldownUntil: null,
            CooldownWaived: false,
            HasReusableSkills: false,
            IsStartable: startable,
            RequiresMicrophone: true,
            RequiresAudio: true,
            IsRetake: isRetake,
            AllSkillsRetakeCredits: allCredits,
            ListeningAndReadingRetakeCredits: lrCredits,
            CreditBalance: balance);

    private static PlacementResultViewDto ScopedResult(PlacementScopeDto scope, bool includeProductive)
    {
        List<PlacementSkillScoreInfoDto> scores =
        [
            new(1, "LIST", "Listening", 16, 20, 420, "B2"),
            new(2, "READ", "Reading", 8, 20, 210, "A2"),
        ];
        List<PlacementSkillResultInfoDto> skillResults =
        [
            new(1, "LIST", "Listening", "Measured", "B2", 42, Taken, false, null),
            new(2, "READ", "Reading", "Measured", "A2", 10, Taken, false, null),
        ];

        if (includeProductive)
        {
            scores.Add(new(3, "SPEA", "Speaking", 12, 20, 140, "B1"));
            scores.Add(new(4, "WRIT", "Writing", 13, 20, 150, "B1"));
            skillResults.Add(new(3, "SPEA", "Speaking", "Measured", "B1", 55, Taken, false, null));
            skillResults.Add(new(4, "WRIT", "Writing", "Measured", "B1", 60, Taken, false, null));
        }

        return new PlacementResultViewDto(
            PlacementId: 1,
            AttemptId: 77,
            CertId: 1,
            CertCode: "TOEIC",
            ExamName: "TOEIC Placement",
            TakenAt: Taken,
            DurationMinutes: 30,
            TotalQuestions: includeProductive ? 44 : 40,
            TotalScore: 630,
            ResultBandCode: "A2",
            ResultBandName: "Elementary",
            CefrBands:
            [
                new CefrBandInfoDto("below_A1", "Below A1", 0, 99, true, false),
                new CefrBandInfoDto("A1", "Beginner", 100, 220, true, false),
                new CefrBandInfoDto("A2", "Elementary", 221, 450, true, true),
                new CefrBandInfoDto("B1", "Intermediate", 451, 700, false, false),
                new CefrBandInfoDto("B2", "Upper intermediate", 701, 900, false, false),
                new CefrBandInfoDto("C1", "Advanced", 901, 990, false, false),
            ],
            SkillScores: scores,
            WeakerSkillName: "Reading",
            BandNote: null,
            ScoreDisplayFactor: 1,
            IsGraded: true,
            SkillResults: skillResults,
            Scope: scope);
    }

    public sealed class Site(
        Func<PlacementCatalogViewDto>? catalog = null,
        Func<PlacementPreflightViewDto>? gate = null,
        Func<PlacementResultViewDto>? result = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ICompanionApiClient>(Upstream.Create(catalog, gate, result).Client));
        }

        public HttpClient CreateClientWithLearner()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var identity = new ClaimsIdentity(
                AuthClaims.For(2, "learner@test.local", Roles.Learner, "Learner"),
                CookieAuthenticationDefaults.AuthenticationScheme,
                nameType: IAccessTokenFactory.ClaimEmail,
                roleType: IAccessTokenFactory.ClaimRole);
            var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" })
            {
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
            };
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), properties,
                CookieAuthenticationDefaults.AuthenticationScheme);
            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
            return client;
        }
    }

    public class Upstream : DispatchProxy
    {
        public ICompanionApiClient Client { get; private set; } = null!;
        private Func<PlacementCatalogViewDto>? _catalog;
        private Func<PlacementPreflightViewDto>? _gate;
        private Func<PlacementResultViewDto>? _result;

        public static Upstream Create(
            Func<PlacementCatalogViewDto>? catalog,
            Func<PlacementPreflightViewDto>? gate,
            Func<PlacementResultViewDto>? result)
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, Upstream>();
            var stub = (Upstream)(object)proxy;
            stub._catalog = catalog;
            stub._gate = gate;
            stub._result = result;
            stub.Client = proxy;
            return stub;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IAuthClient.GetMeAsync) => Task.FromResult(
                new AuthUserDto(2, "learner@test.local", "Learner", Roles.Learner, false)),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Learner", null, "Asia/Ho_Chi_Minh")),
            nameof(ILearnerContextClient.GetLearnerContextAsync) => Task.FromResult(
                new LearnerContextDto(new CertificateOptionDto("TOEIC", "TOEIC", "Part"),
                    [new CertificateOptionDto("TOEIC", "TOEIC", "Part")], 10)),
            nameof(IPlacementClient.GetPlacementCatalogAsync) => Task.FromResult(_catalog!()),
            nameof(IPlacementClient.GetPlacementGateAsync) => Task.FromResult(_gate!()),
            nameof(IPlacementClient.GetPlacementResultAsync) => Task.FromResult(_result!()),
            nameof(IAiInsightsClient.GetExamInsightAsync) => Task.FromResult<AiInsightDto?>(null),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };
    }
}
