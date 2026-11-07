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
using WebMVC.Models;

namespace WebMVC.Tests;

/// <summary>
/// #161: display hardcoding — every label that only works for one certificate.
/// <para>
/// The bottom line of the ticket, driven through the real host: a DEMO certificate the code has never
/// heard of, whose two skills are called Comprehension and Expression and which has four bands. Every
/// page the ticket names is walked, and each one is asked the same question — does anything on it come
/// from a certificate other than the one it was told to render?
/// </para>
/// <para>
/// A DEMO certificate rather than the seeded TOEIC/IELTS/VSTEP trio, because the seeded three happen to
/// match every literal in the templates. The defect disappears when the certificate is one the code was
/// not written against, so that is the shape the fixture takes.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class DemoCertificateDisplayTests : IClassFixture<DemoCertificateDisplayTests.Site>
{
    /// <summary>Every certificate name the app used to bake into a template.</summary>
    private static readonly string[] OtherCertificateNames = ["TOEIC", "IELTS", "VSTEP"];

    private readonly Site site;

    public DemoCertificateDisplayTests(Site site) => this.site = site;

    [Theory]
    [InlineData("/")]
    [InlineData("/certificates")]
    [InlineData("/weakness-analysis")]
    [InlineData("/practice")]
    [InlineData("/placement/result/1")]
    [InlineData("/exams/history")]
    public async Task No_page_names_a_certificate_it_was_not_given(string path)
    {
        using var client = site.CreateClientWithLearner();
        var response = await client.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} -> {path}");

        var body = await response.Content.ReadAsStringAsync();

        foreach (var name in OtherCertificateNames)
        {
            Assert.False(body.Contains(name, StringComparison.Ordinal),
                $"{path} renders \"{name}\", a certificate this learner is not working on.");
        }
    }

    [Fact]
    public async Task A_section_is_called_whatever_the_certificate_calls_it()
    {
        // The weakness page labelled sections "Part" when the certificate's code was TOEIC and
        // "Section" otherwise. The word is now a property of the certificate, so a certificate that
        // says "Unit" is labelled "Unit" without the code knowing it exists.
        using var client = site.CreateClientWithLearner();
        var body = await client.GetStringAsync("/weakness-analysis");

        Assert.Contains("Unit C1", body);
        Assert.DoesNotContain("Part C1", body);
        Assert.DoesNotContain("Section C1", body);
    }

    [Fact]
    public async Task The_certificates_strip_draws_exactly_the_bands_the_certificate_has()
    {
        using var client = site.CreateClientWithLearner();
        var body = await client.GetStringAsync("/certificates");

        // Four bands of the DEMO certificate, no fifth column invented for the CEFR ladder.
        var strip = Regex.Match(body, @"<div class=""cefr__track""[^>]*>(?<body>[\s\S]*?)</div>").Groups["body"].Value;
        Assert.NotEmpty(strip);

        var bands = Regex.Matches(strip, @"class=""cefr__band[^""]*"">(?<code>[^<]+)<").Select(m => m.Groups["code"].Value.Trim()).ToList();
        Assert.Equal(4, bands.Count);
        Assert.Equal(["D1", "D2", "D3", "D4"], bands);
    }

    [Fact]
    public async Task A_band_is_as_wide_as_the_score_range_it_covers()
    {
        using var client = site.CreateClientWithLearner();
        var body = await client.GetStringAsync("/certificates");

        // DEMO bands: D1 0-99, D2 100-199, D3 200-399, D4 400-500. Equal columns would be 25% each,
        // which would put the D4 edge in the wrong place on every page that draws this scale.
        var track = Regex.Match(body, @"<div class=""cefr__track""(?<attrs>[^>]*)>").Groups["attrs"].Value;
        Assert.Contains("--band-columns: 99fr 99fr 199fr 100fr", track);
    }

    [Fact]
    public async Task A_goal_screen_with_no_target_band_says_so_instead_of_inventing_one()
    {
        // #161 / D7: the view model used to fall back to the literal "B2" when the learner had never
        // chosen a band. A DEMO certificate has no B2, so the old code preselected a band that does not
        // exist in the list it was rendering.
        using var client = site.CreateClientWithLearner();
        var body = await client.GetStringAsync("/placement/target-band");

        Assert.Contains("no target band yet", body, StringComparison.OrdinalIgnoreCase);

        // The claim is about the band picker, not the whole document. Reading the whole body made a
        // random anti-forgery token containing "B2" fail the test about one run in twenty (#252), and
        // a test that also fails for an unrelated reason cannot be trusted when it does fail.
        // Every band the page offers is a `value="..."` on a TargetBandCode radio; none of them may
        // be B2, because a DEMO certificate has D1-D4 and no B2.
        var targetBands = Regex.Matches(
                body,
                @"name=""TargetBandCode""\s*value=""(?<code>[^""]+)""")
            .Select(m => m.Groups["code"].Value)
            .ToList();

        Assert.NotEmpty(targetBands);
        Assert.DoesNotContain("B2", targetBands);
    }

    [Fact]
    public void A_landing_page_with_no_catalogue_invents_no_certificate()
    {
        // #161: the hero's preview, the chips and the cards all came from literals. With nothing to
        // read, the page must not fall back to the certificate it used to be written for.
        var empty = new HomeViewModel(false, false, []);
        Assert.Null(empty.Preview);
        Assert.Empty(empty.Certificates);
        Assert.DoesNotContain("TOEIC", empty.Description);
        Assert.DoesNotContain("IELTS", empty.Description);
        Assert.DoesNotContain("VSTEP", empty.Description);
    }

    [Fact]
    public void A_landing_card_reads_the_certificates_own_skills_and_range()
    {
        // The DEMO certificate of this ticket: two skills nobody wrote a literal for, and a 0-500 range.
        var card = HomeCertificateCard.From(new CertificateDto("DEMO", "Demo Certificate", null,
            [new SkillDto("COMP", "Comprehension", ModalityDto.Listening, 45, []),
             new SkillDto("EXPR", "Expression", ModalityDto.Writing, 60, [])],
            [new BandDto("D1", "Starting", 0, 99), new BandDto("D4", "Mastery", 400, 500)]));

        Assert.Equal("Comprehension · Expression", card.SkillsSummary);
        Assert.Equal("0–500", card.RangeSummary);
    }

    /// <summary>The real WebMVC host with every upstream the six pages read swapped for a DEMO
    /// certificate. Only the pages' own template can supply a certificate name after this.</summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        internal static readonly CertificateOptionDto Demo = new("DEMO", "Demo Certificate", "Unit");

        internal static readonly BandDto[] DemoBands =
        [
            new("D1", "Starting", 0, 99),
            new("D2", "Developing", 100, 199),
            new("D3", "Strong", 200, 399),
            new("D4", "Mastery", 400, 500),
        ];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services => services.AddSingleton<ICompanionApiClient>(DemoUpstream.Create().Client));
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

    /// <summary>
    /// Answers every upstream call the six walked pages make, and answers the two catalogue calls with
    /// the DEMO certificate. A method the pages do not use is a defect in this fixture, not something to
    /// answer plausibly: the auth and time-zone calls are named explicitly so an unexpected one fails
    /// loudly instead of silently rendering an empty page.
    /// </summary>
    public class DemoUpstream : DispatchProxy
    {
        private static readonly CertificateOptionDto Demo = Site.Demo;

        public ICompanionApiClient Client { get; private set; } = null!;

        public static DemoUpstream Create()
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, DemoUpstream>();
            var stub = (DemoUpstream)(object)proxy;
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
                new LearnerContextDto(Demo, [Demo], 8)),

            nameof(ICertificatesClient.GetCertificatesAsync) => Task.FromResult<IReadOnlyList<CertificateDto>>(
            [
                new CertificateDto("DEMO", "Demo Certificate", "Two skills named by the author.",
                    [
                        new SkillDto("COMP", "Comprehension", ModalityDto.Listening, 45,
                            [new SectionDto("C1", "First passage", 10, 1)]),
                        new SkillDto("EXPR", "Expression", ModalityDto.Writing, 60,
                            [new SectionDto("E1", "First task", 1, 2)]),
                    ],
                    [.. Site.DemoBands.Select(b => new BandDto(b.Code, b.Name, b.MinTotal, b.MaxTotal))]),
            ]),

            nameof(IWeaknessAnalysisClient.GetWeaknessAnalysisAsync) => Task.FromResult(new WeaknessAnalysisDto(
                Demo, HasData: true, GradedExamCount: 1, CompletedPracticeCount: 2, TotalAnswered: 20,
                TotalCorrect: 10, OverallAccuracyPercentage: 50,
                Sections: [new SectionAccuracyDto(1, "C1", "First passage", 20, 10, 50, IsWeakest: true, IsStrongest: false)],
                FrequentlyMissedCategories: [], Recommendations: [],
                PriorityPracticeSection: new SectionAccuracyDto(1, "C1", "First passage", 20, 10, 50, true, false))),
            nameof(IWeaknessAnalysisClient.GetDashboardInsightAsync) =>
                Task.FromException<AiInsightDto>(new InvalidOperationException("No insight")),
            // The placement result prefetches an insight and degrades without one, so this fixture
            // proves the page renders with none rather than stubbing a narrative nothing reads.
            nameof(IAiInsightsClient.GetExamInsightAsync) =>
                Task.FromException<AiInsightDto>(new InvalidOperationException("No insight")),

            nameof(IPracticeClient.GetPracticeCatalogAsync) => Task.FromResult(new PracticeCatalogDto(
                Demo,
                ListeningSections: [Section(1, "COMP", "Comprehension", ModalityDto.Listening, "C1", "First passage")],
                ReadingSections: [],
                OtherSections: [Section(2, "EXPR", "Expression", ModalityDto.Writing, "E1", "First task")],
                WeakestSection: null)),

            nameof(IPlacementClient.GetPlacementResultAsync) => Task.FromResult(PlacementResult()),
            nameof(IPlacementClient.GetTargetBandViewAsync) => Task.FromResult(TargetBand()),
            nameof(IMockExamsClient.GetExamHistoryAsync) => Task.FromResult(ExamHistory()),

            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };

        private static PracticeSectionDto Section(int id, string skillCode, string skillName, ModalityDto modality,
            string code, string name) =>
            new(id, skillCode, skillName, modality, code, name, 4, 10, 10, null, 0, 1);

        private static PlacementResultViewDto PlacementResult() => new(
            PlacementId: 1, AttemptId: 1, CertId: 100, CertCode: "DEMO", ExamName: "Demo placement",
            TakenAt: DateTimeOffset.UtcNow, DurationMinutes: 30, TotalQuestions: 20, TotalScore: 150,
            ResultBandCode: "D2", ResultBandName: "Developing",
            CefrBands: [.. Site.DemoBands.Select((b, i) => new CefrBandInfoDto(b.Code, b.Name, b.MinTotal, b.MaxTotal,
                IsReached: i <= 1, IsCurrent: b.Code == "D2"))],
            SkillScores:
            [
                new PlacementSkillScoreInfoDto(1, "COMP", "Comprehension", 10, 10, 75, "D2"),
                new PlacementSkillScoreInfoDto(2, "EXPR", "Expression", 10, 10, 75, "D2"),
            ],
            WeakerSkillName: null, BandNote: null);

        private static TargetBandViewDto TargetBand() => new(
            Demo, CurrentBandCode: "D2", CurrentTotalScore: 150, CurrentBandName: "Developing",
            TargetBandCode: null,
            CefrBands: [.. Site.DemoBands.Select((b, i) => new CefrBandInfoDto(b.Code, b.Name, b.MinTotal, b.MaxTotal,
                IsReached: i <= 1, IsCurrent: b.Code == "D2"))],
            AvailableBands: [.. Site.DemoBands.Select(b => new TargetBandOptionDto(
                b.Code, b.Name, b.MinTotal, b.MaxTotal, null, IsCurrent: b.Code == "D2", IsTarget: false))],
            Goal: null);

        private static ExamHistoryDto ExamHistory() => new(
            Demo, TotalAttempts: 1, BestScore: 150, AverageScore: 150,
            BestScoreBandCode: "D2", BestScoreBandName: "Developing",
            CefrBands: [.. Site.DemoBands.Select(b => new ScoreBandItemDto(b.Code, b.Name, b.MinTotal, b.MaxTotal, 0))],
            Attempts:
            [
                new ExamHistoryItemDto(1, 1, "Demo mock exam", 100, "DEMO",
                    DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1),
                    TotalScore: 150, BandCode: "D2", BandName: "Developing",
                    SkillScores: [new AttemptSkillScoreDto(1, "COMP", "Comprehension", ModalityDto.Listening, 10, 75, 10)],
                    Status: AttemptStatusDto.Graded),
            ]);
    }
}
