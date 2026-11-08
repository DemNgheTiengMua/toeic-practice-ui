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
/// #241: the dashboard's three blocks, asserted on the HTML a learner receives.
/// <para>
/// <b>Why the real Razor pipeline and not a source grep.</b> Every guard in this file is about what is
/// RENDERED. A source grep for "no progressbar" passes on a page that renders one from a partial, and
/// a grep for "two columns" passes on a four-track template with two cells empty. So each test boots
/// the real host, renders <c>/dashboard</c>, and asserts on the markup.
/// </para>
/// <para>
/// <b>The input that would pass each guard while being wrong is stated above each test</b>, because a
/// guard whose wrong input nobody wrote down is a guard nobody has checked.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class DashboardRenderedBlocksTests
{
    // ------------------------------------------------------------------------------------------
    // fixtures: a four-skill certificate and a two-skill one, built from the DTOs the API sends
    // ------------------------------------------------------------------------------------------

    private static SkillCardDto Measured(int id, string code, string name, string level, int position) =>
        new(id, code, name, ModalityDto.Reading, null, null, [], 1,
            LevelCode: level, PositionInBand: position,
            MeasuredAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    private static SkillCardDto Unmeasured(int id, string code, string name) =>
        new(id, code, name, ModalityDto.Reading, null, null, [], 1);

    private static GoalStandingDto Goal(
        string? target = "B2",
        string? status = "stretch",
        int? requiredHours = 120,
        decimal? weeklyHours = 6m,
        DateOnly? targetDate = null,
        bool noMeasurement = false) =>
        new(
            CurrentBandCode: noMeasurement ? null : "B1",
            CurrentBandName: noMeasurement ? null : "Intermediate",
            CurrentScore: noMeasurement ? null : 750,
            CurrentSource: noMeasurement ? null : "placement",
            TargetBandCode: target,
            TargetBandName: target is null ? null : "Vantage",
            TargetMinScore: target is null ? null : 785,
            TargetDate: targetDate,
            WeeklyStudyHours: weeklyHours,
            Status: status,
            CompletedPathSteps: 2,
            TotalPathSteps: 5,
            CurrentWeekNumber: 2,
            WeekPlannedMinutes: 120,
            WeekCompletedMinutes: 45,
            ScoreDisplayFactor: 1,
            RequiredTotalHours: requiredHours,
            OverallLevelCode: noMeasurement ? null : "B1",
            LimitingSkillCode: noMeasurement ? null : "LIST",
            LimitingSkillName: noMeasurement ? null : "Listening",
            HasNoMeasurement: noMeasurement);

    private static LearnerDashboardDto Dashboard(
        IReadOnlyList<SkillCardDto> skills, GoalStandingDto? goal = null) =>
        new(
            new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
            new NextActionDto(
                "WEAKEST TOPIC", "Drill Incomplete Sentences",
                "58% accuracy over 12 answers in Reading.", "Start practice", "/practice",
                "warning", "free_drill", "READ · 58%"),
            goal ?? Goal(),
            skills,
            AttemptCount: 3,
            PracticeCount: 5);

    private static string Render(LearnerDashboardDto dashboard)
    {
        using var site = new Site(dashboard);
        using var client = site.CreateClientWithLearner();

        var response = client.GetAsync("/dashboard").GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static IReadOnlyList<string> Blocks(string html)
    {
        // The three block sections, by the class only that block carries. Counted on the rendered
        // HTML, not the source, so a partial inserted from anywhere is caught.
        var markers = new[] { "dashboard-hero", "dashboard-goal", "dashboard-skills" };
        return markers.Where(marker => Regex.IsMatch(html, $@"class=""[^""]*\b{marker}\b")).ToList();
    }

    // ------------------------------------------------------------------------------------------
    // exactly three blocks
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that passes this guard while being wrong:</b> the three required blocks plus a
    /// "recent activity" block that someone added back. Three markers are present, every other test in
    /// this file passes, and the page has four blocks - exactly the regression LRN-12 calls out. This
    /// counts the block SECTIONS and the declared block count, so a fourth fails here.
    /// </summary>
    [Fact]
    public void A_placed_learner_gets_exactly_three_blocks_and_a_fourth_fails_the_count()
    {
        var html = Render(Dashboard([
            Measured(1, "LIST", "Listening", "B2", 60),
            Measured(2, "READ", "Reading", "B1", 40),
            Measured(3, "SPEA", "Speaking", "B1", 30),
            Measured(4, "WRIT", "Writing", "A2", 70),
        ]));

        var blocks = Blocks(html);

        Assert.Equal(3, blocks.Count);
        Assert.Equal(
            ["dashboard-hero", "dashboard-goal", "dashboard-skills"],
            blocks);
        // The page states its own count, and it is the count of block sections above - a fourth
        // partial that forgot to add itself to Blocks is caught by the first assertion, and a Blocks
        // member with no rendering is caught by this one.
        Assert.Contains("data-block-count=\"3\"", html, StringComparison.Ordinal);

        // The named regressions from #159 stay gone: no recent-activity block, no insight panel.
        Assert.DoesNotContain("Recent exams", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Recent practice", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AiInsightPanel", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// LRN-13: with nothing measured the page is ONE block, and it says what placement is. The wrong
    /// implementation this catches is the one the product shipped before #159: an unplaced learner
    /// sees three blocks of empty figures, which reads as broken at the moment they have least reason
    /// to trust it.
    /// </summary>
    [Fact]
    public void An_unplaced_learner_gets_one_block_and_a_sentence_not_a_grid_of_empty_figures()
    {
        var html = Render(Dashboard(
            [Unmeasured(1, "LIST", "Listening"), Unmeasured(2, "READ", "Reading")],
            Goal(target: null, status: null, requiredHours: null, weeklyHours: null, noMeasurement: true)));

        var blocks = Blocks(html);

        Assert.Equal(["dashboard-hero"], blocks);
        Assert.Contains("data-block-count=\"1\"", html, StringComparison.Ordinal);
        // The sentence LRN-13 requires: what placement is.
        Assert.Contains("id=\"placement-notice\"", html, StringComparison.Ordinal);
        Assert.Contains("free sitting", html, StringComparison.OrdinalIgnoreCase);
        // And no ghost figures: no goal block, no per-skill table, no fabricated A1.
        Assert.DoesNotContain("dashboard-goal__cells", html, StringComparison.Ordinal);
        Assert.DoesNotContain("skill-standing", html, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    // block 2: four tabular cells, NO progress bar
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that passes this guard while being wrong:</b> the progress bar restyled so it is
    /// "not technically a bar" - a <c>&lt;div&gt;</c> with an inline width and no <c>progress</c> class.
    /// It has no <c>role="progressbar"</c>, so an assertion on ARIA misses it entirely, and it still
    /// draws a bar. This asserts the BAR MECHANISM - the inline width, the role, the progress classes -
    /// and then asserts the four cells exist, because deleting the block would also remove the bar.
    /// </summary>
    [Fact]
    public void Goal_standing_renders_four_tabular_cells_and_no_progress_bar_mechanism()
    {
        var html = Render(Dashboard([
            Measured(1, "LIST", "Listening", "B2", 60),
            Measured(2, "READ", "Reading", "B1", 40),
        ]));

        // The bar mechanism, in every shape it has taken here: ARIA role, the progress classes, and a
        // div carrying a width percentage.
        Assert.DoesNotContain("role=\"progressbar\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("progress__bar", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"progress", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"style=""[^""]*width:\s*\d+%", html);
        Assert.DoesNotMatch(@"<div[^>]+width:\s*\d+%", html);

        // Four cells, in D21a's order, so removing the bar did not remove the block.
        var cells = Regex.Matches(html, @"<th scope=""row"">(?<label>[^<]+)</th>")
            .Select(m => m.Groups["label"].Value.Trim()).ToList();
        Assert.Equal(
            ["Level against target", "Remaining guided hours", "Pacing", "Verdict"],
            cells);
    }

    /// <summary>
    /// <b>The input that passes a weaker test while being wrong:</b> a goal block that renders the
    /// four labels with no values in them - headings over empty cells. Every count assertion passes and
    /// the learner learns nothing, so the figures are asserted by value.
    /// </summary>
    [Fact]
    public void The_four_cells_carry_the_stored_figures()
    {
        var html = Render(Dashboard([
            Measured(1, "LIST", "Listening", "B2", 60),
            Measured(2, "READ", "Reading", "B1", 40),
        ]));

        Assert.Contains("B1 to B2", html, StringComparison.Ordinal);  // level against target
        Assert.Contains("120 h", html, StringComparison.Ordinal);     // stored ΔH
        Assert.Contains("6 h/week", html, StringComparison.Ordinal);  // declared pace
        Assert.Contains("Achievable with more hours a week", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The input that passes this guard while being wrong:</b> a page that prints a confident verdict
    /// from a goal that has no target, or from bands that carry no study hours. #234 and #244 both
    /// established the rule: a fact that cannot be judged reads "cannot judge", never a verdict. A
    /// <c>switch</c> with a default arm of "unrealistic" passes every other assertion in this file.
    /// </summary>
    [Fact]
    public void A_goal_that_cannot_be_judged_reads_cannot_judge_and_never_a_verdict()
    {
        var html = Render(Dashboard(
            [Measured(1, "LIST", "Listening", "B2", 60)],
            Goal(target: null, status: null, requiredHours: null, weeklyHours: null)));

        Assert.Contains("cannot judge", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("On pace", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Not achievable", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Achievable with more hours", html, StringComparison.Ordinal);
        // And the missing ΔH is its own "cannot judge", not a fabricated 0 h.
        Assert.DoesNotContain(">0 h<", html, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    // block 3: one column per examined skill
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that passes this guard while being wrong:</b> a four-column grid on a two-skill
    /// certificate, with two cells empty. It has <c>display:grid</c>, four header cells (two blank), and
    /// looks complete in a screenshot. D21a names it explicitly. This asserts the rendered COLUMN COUNT
    /// against the certificate's skill count, and that no header cell is empty.
    /// </summary>
    [Fact]
    public void A_two_skill_certificate_renders_two_columns_and_no_empty_third_or_fourth()
    {
        var html = Render(Dashboard([
            Measured(1, "LIST", "Listening", "B2", 60),
            Measured(2, "READ", "Reading", "B1", 40),
        ]));

        var headers = HeaderCells(html);

        Assert.Equal(2, headers.Count);
        Assert.Equal(["LIST", "READ"], headers.Select(h => h.Code));
        // No empty header - the symptom of a four-track template with a two-skill list.
        Assert.All(headers, h => Assert.False(string.IsNullOrWhiteSpace(h.Name), "an empty skill column"));
        // The count class follows the certificate, and CSS keys on it.
        Assert.Contains("dashboard-skills__grid--count-2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard-skills__grid--count-4", html, StringComparison.Ordinal);
        // The rendered cells agree with the headers: two columns, two cells.
        Assert.Equal(2, CountDataSkillCells(html));
    }

    /// <summary>
    /// The other half of the same criterion: a four-skill certificate renders four, so the two-column
    /// result above is not "the template caps at two".
    /// </summary>
    [Fact]
    public void A_four_skill_certificate_renders_four_columns()
    {
        var html = Render(Dashboard([
            Measured(1, "LIST", "Listening", "B2", 60),
            Measured(2, "READ", "Reading", "B1", 40),
            Measured(3, "SPEA", "Speaking", "B1", 30),
            Measured(4, "WRIT", "Writing", "A2", 70),
        ]));

        var headers = HeaderCells(html);

        Assert.Equal(4, headers.Count);
        Assert.Equal(["LIST", "READ", "SPEA", "WRIT"], headers.Select(h => h.Code));
        Assert.Contains("dashboard-skills__grid--count-4", html, StringComparison.Ordinal);
        Assert.Equal(4, CountDataSkillCells(html));
    }

    /// <summary>
    /// <b>The input that passes this guard while being wrong:</b> an unmeasured skill rendered as A1,
    /// or as a grey dash. Both look like an answer, and both are fabricated: #211 omits an unmeasured
    /// skill from the stored vector precisely so no screen can present it at A1. This asserts the
    /// words and the absence of a level where there is none.
    /// </summary>
    [Fact]
    public void An_unmeasured_skill_says_so_and_is_never_A1_or_a_grey_dash()
    {
        var html = Render(Dashboard([
            Measured(1, "LIST", "Listening", "B2", 60),
            Unmeasured(2, "READ", "Reading"),
        ]));

        Assert.Contains("Not measured yet", html, StringComparison.Ordinal);
        // The unmeasured cell carries its attention marker, not a muted/empty one.
        Assert.Contains("skill-standing--unmeasured", html, StringComparison.Ordinal);

        // A1 is the fabricated value this guard exists to catch. No cell may print it for a skill with
        // no stored state.
        var readingCell = Regex.Match(
            html, @"data-skill-id=""2""(?<body>[\s\S]*?)</td>").Groups["body"].Value;
        Assert.NotEmpty(readingCell);
        Assert.DoesNotContain("A1", readingCell, StringComparison.Ordinal);
        // A dash is the other fabrication: it reads as "zero" rather than "unknown".
        Assert.DoesNotContain(">—<", readingCell, StringComparison.Ordinal);
    }

    /// <summary>
    /// LRN-19: the 0-100 position is shown as a POSITION, never a percentage. A bare "12%" after
    /// "78%" reads as collapse, because crossing a band legitimately resets the number downward.
    /// <b>The input that passes a weaker test:</b> the right number followed by "%".
    /// </summary>
    [Fact]
    public void A_position_is_printed_as_position_over_one_hundred_not_a_percentage()
    {
        var html = Render(Dashboard([
            Measured(1, "LIST", "Listening", "B2", 78),
            Measured(2, "READ", "Reading", "B1", 12),
        ]));

        Assert.Contains("78/100", html, StringComparison.Ordinal);
        Assert.Contains("12/100", html, StringComparison.Ordinal);
        Assert.DoesNotContain("78%", html, StringComparison.Ordinal);
        Assert.DoesNotContain("12%", html, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    // LRN-15: the exam date, as a date normally and a countdown inside 14 days
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that passes this guard while being wrong:</b> a perpetual countdown - "365 days to
    /// exam" on a twelve-month goal. U22 calls that noise with no use; the countdown is for the window
    /// where the pressure is actionable. So this asserts BOTH halves: a far date renders as a date, and
    /// a near one renders as a countdown.
    /// </summary>
    [Fact]
    public void A_distant_exam_date_renders_as_a_date_and_a_near_one_as_a_countdown()
    {
        var far = Render(Dashboard(
            [Measured(1, "LIST", "Listening", "B2", 60)],
            Goal(targetDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200))));
        Assert.DoesNotContain("to exam", far, StringComparison.Ordinal);
        Assert.Matches(@"\d{1,2} \w{3} \d{4}", far);

        var near = Render(Dashboard(
            [Measured(1, "LIST", "Listening", "B2", 60)],
            Goal(targetDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(9))));
        Assert.Contains("9 days to exam", near, StringComparison.Ordinal);
        // The attention state is carried by colour AND the words, never colour alone.
        Assert.Contains("dashboard-goal__date--countdown", near, StringComparison.Ordinal);
    }

    /// <summary>The 14-day boundary itself, which is where an off-by-one lives.</summary>
    [Theory]
    [InlineData(14, true)]
    [InlineData(15, false)]
    [InlineData(0, true)]
    public void The_countdown_window_ends_at_fourteen_days(int days, bool expected)
    {
        var presentation = new DashboardPresentation(
            Dashboard(
                [Measured(1, "LIST", "Listening", "B2", 60)],
                Goal(targetDate: new DateOnly(2026, 12, 20))),
            Formatter(),
            today: new DateOnly(2026, 12, 20).AddDays(-days));

        Assert.Equal(expected, presentation.IsCountdown);
    }

    // ------------------------------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------------------------------

    private static WebMVC.DateTimeFormatter Formatter() =>
        new(new Microsoft.AspNetCore.Http.HttpContextAccessor
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
        });

    private sealed record Header(string Code, string Name);

    private static List<Header> HeaderCells(string html) =>
        Regex.Matches(html, @"<th scope=""col""[^>]*class=""skill-standing__head"">(?<body>[\s\S]*?)</th>")
            .Select(m =>
            {
                var code = Regex.Match(m.Groups["body"].Value, @"skill-standing__code[^>]*>(?<v>[^<]*)<").Groups["v"].Value.Trim();
                var name = Regex.Match(m.Groups["body"].Value, @"skill-standing__name[^>]*>(?<v>[^<]*)<").Groups["v"].Value.Trim();
                return new Header(code, name);
            })
            .ToList();

    private static int CountDataSkillCells(string html) =>
        Regex.Matches(html, @"data-skill-id=""\d+""").Count;

    /// <summary>
    /// The real WebMVC host with the upstream API replaced. Everything else - middleware order, the
    /// cookie scheme, Razor compilation - is production code, which is the point: a hand-rendered
    /// partial would prove nothing about the page.
    /// </summary>
    private sealed class Site(LearnerDashboardDto dashboard) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
            {
                var upstream = Upstream.Create();
                upstream.Dashboard = dashboard;
                services.AddSingleton<ICompanionApiClient>(upstream.Client);
            });
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

    private class Upstream : DispatchProxy
    {
        public LearnerDashboardDto Dashboard { get; set; } = null!;

        public ICompanionApiClient Client { get; private set; } = null!;

        public static Upstream Create()
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, Upstream>();
            var stub = (Upstream)(object)proxy;
            stub.Client = proxy;
            return stub;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IAuthClient.GetMeAsync) => Task.FromResult(
                new AuthUserDto(2, "learner@test.local", "Learner", Roles.Learner, true)),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Learner", null, "Asia/Ho_Chi_Minh")),
            nameof(ILearnerContextClient.GetLearnerContextAsync) => Task.FromResult(
                new LearnerContextDto(new CertificateOptionDto("TOEIC", "TOEIC"), [], 8)),
            nameof(ILearnerContextClient.GetDashboardAsync) => Task.FromResult(Dashboard),
            // The dashboard shell's certificate switcher reads this; the page under test never calls
            // anything else, and an unexpected call fails loudly rather than rendering an empty page.
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };
    }
}
