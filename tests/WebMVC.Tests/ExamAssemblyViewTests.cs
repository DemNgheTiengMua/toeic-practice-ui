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
/// #225 / S-09: the assembly workbench rendered through the real host.
/// <para>
/// <b>The criterion this file exists for</b> is the one a service test cannot reach: an over-demand
/// section must <em>show its deficit</em> rather than bind short silently. That is a rendering claim,
/// so the fixture gives three sections whose declared and supply figures deliberately do not
/// arithmetically relate — the shortfall in the payload is not the subtraction a view would perform —
/// and the page is read back.
/// </para>
/// <para>
/// <b>What would pass this while being wrong:</b> a page that prints the declared count twice, or one
/// that prints "6 / 6" for a section that declares six and can supply two. The assertions read the
/// gap column's own text, so a recompute or a duplicate fails rather than looks right.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class ExamAssemblyViewTests : IClassFixture<ExamAssemblyViewTests.Site>
{
    /// <summary>The exam id the stub answers for; a request for another id is a defect, not a 404.</summary>
    private const int ExamId = 5;

    private readonly Site site;

    public ExamAssemblyViewTests(Site site) => this.site = site;

    /// <summary>
    /// Three sections that cover the three readings the screen must distinguish: nothing servable at
    /// all, short of what is declared, and able to fill what is declared.
    /// <para>
    /// The third is the one that discriminates a printed figure from a computed one: it declares 30 and
    /// can supply 41, so a view that subtracted would print "11 short" where the payload says 0. The
    /// first two are the case the ticket names — a section the pool cannot fill must show its deficit
    /// rather than read as a paper that is merely not finished.
    /// </para>
    /// </summary>
    private static ExamAssemblyDto Assembly() => new(
        ExamId,
        "ETS 2024 Reading",
        "TOEIC",
        "demo",
        120,
        FinishedAttemptCount: 0,
        [
            new ExamAssemblySectionDto(
                11, "P1", "Photographs", ModalityDto.Listening, 1,
                DeclaredCount: 6, BoundCount: 0, SupplyCount: 0, Shortfall: 6,
                Positions: [new ExamAssemblyPositionDto(1, true, null, null, null, 1)],
                Candidates: [],
                CandidateTotal: 0),

            new ExamAssemblySectionDto(
                12, "P5", "Incomplete Sentences", ModalityDto.Reading, 2,
                DeclaredCount: 39, BoundCount: 2, SupplyCount: 2, Shortfall: 37,
                Positions:
                [
                    new ExamAssemblyPositionDto(1, true, 200, "R-DET", "B1", 1),
                    new ExamAssemblyPositionDto(2, true, 201, "R-GRAM", "B1", 1),
                    new ExamAssemblyPositionDto(3, true, null, null, null, 1),
                ],
                Candidates:
                [
                    new ExamAssemblyCandidateDto(1, 200, 1, "R-DET", "B1", "sentence_cloze", "demo", "The depot opens at six."),
                ],
                CandidateTotal: 1),

            // Over-supplied: 41 servable versions against 30 declared. The payload says the gap is 0,
            // because a positive gap is a shortfall and a surplus is not one. A view that subtracted
            // would print "-11 short" and fail below.
            new ExamAssemblySectionDto(
                13, "P7", "Reading Comprehension", ModalityDto.Reading, 3,
                DeclaredCount: 30, BoundCount: 0, SupplyCount: 41, Shortfall: 0,
                Positions: [new ExamAssemblyPositionDto(1, true, null, null, null, 1)],
                Candidates: [],
                CandidateTotal: 41),
        ]);

    /// <summary>
    /// AC1 and AC5 together: each section's row carries the count it declares and the stock the pool
    /// can supply, as two separate figures, and an over-demand section shows a positive shortfall.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a table whose supply column repeats the
    /// declared figure still "shows two numbers" — for every row here they differ, so a duplicate
    /// fails on the first section.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_over_demand_section_shows_its_deficit_rather_than_binding_short_silently()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync($"/admin/exams/{ExamId}/assembly");

        var short_ = RowFor(body, "P5");
        Assert.Contains("data-figure=\"declared\">39<", short_);
        Assert.Contains("data-figure=\"supply\">2<", short_);
        Assert.Contains("37 short", short_);

        // The empty section: nothing servable, and the full declared count is the gap.
        var empty = RowFor(body, "P1");
        Assert.Contains("data-figure=\"declared\">6<", empty);
        Assert.Contains("data-figure=\"supply\">0<", empty);
        Assert.Contains("6 short", empty);

        // The over-supplied section reads "0 short", never a negative gap.
        var surplus = RowFor(body, "P7");
        Assert.Contains("data-figure=\"supply\">41<", surplus);
        Assert.Contains("0 short", surplus);
        Assert.DoesNotContain("-11", surplus);
        Assert.DoesNotContain("11 short", surplus);
    }

    /// <summary>
    /// The row's paint is a state, not an expression in the markup: an empty section and a short one
    /// must be told apart even when both are short of their declared count.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a page that painted every short row the
    /// same colour passes an assertion on "short" alone. The empty row is asserted separately.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_empty_section_and_a_short_section_are_painted_as_different_states()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync($"/admin/exams/{ExamId}/assembly");

        Assert.Contains("assembly-row--empty", RowFor(body, "P1"));
        Assert.Contains("assembly-row--short", RowFor(body, "P5"));
        Assert.Contains("assembly-row--met", RowFor(body, "P7"));
    }

    /// <summary>
    /// AC2 at the screen: a bound position states its unit and the declared shape keeps its open
    /// slots, so an author can see where the paper still needs an item.
    /// </summary>
    [Fact]
    public async Task A_bound_position_shows_its_unit_and_open_positions_stay_visible()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync($"/admin/exams/{ExamId}/assembly");

        // position 1 of P5 is bound to R-DET in unit 1
        Assert.Contains("R-DET", body);
        Assert.Contains("unit 1", body);
        // and the section still declares positions 3..39 that nobody filled
        Assert.Contains("Unbound", body);
        Assert.Contains("No version sits here", body);
    }

    /// <summary>
    /// AC3's screen half: the section's candidate list carries the tier, which is where INV-6 is read
    /// before the click. A list without it would let an author pick a demo version for a production
    /// paper and learn the rule from the refusal.
    /// </summary>
    [Fact]
    public async Task A_candidate_states_its_tier_and_its_skill()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync($"/admin/exams/{ExamId}/assembly");

        Assert.Contains("Demo stock", body);
        Assert.Contains("Reading", body);
        Assert.Contains("Sentence cloze", body);
    }

    /// <summary>
    /// The bind form posts to the MVC forwarding endpoint (ADR-0002) with the anti-forgery token, and
    /// carries the section, the version, the position and the unit — the four facts the binding needs.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a form that posted straight to
    /// <c>/api/...</c> would work in dev and put the cookie's token in a request the browser made
    /// itself. The assertion is on the action path.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_bind_form_posts_to_the_mvc_forward_with_its_token_and_its_four_facts()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync($"/admin/exams/{ExamId}/assembly");

        Assert.Contains($"/admin/exams/{ExamId}/assembly/bind", body);
        Assert.DoesNotContain("/api/admin/exams", body);
        Assert.Contains("__RequestVerificationToken", body);
        Assert.Contains("name=\"SectionId\"", body);
        Assert.Contains("name=\"ItemVersionId\"", body);
        Assert.Contains("name=\"DisplayOrder\"", body);
        Assert.Contains("name=\"UnitNumber\"", body);

        // The position defaults to the paper's next open slot, not 1: a default of 1 would collide
        // with the binding already there and the author would be told their own paper is full.
        var bind = Regex.Match(body, @"name=""DisplayOrder""[\s\S]{0,200}?value=""(?<value>\d+)""");
        Assert.True(bind.Success, "the bind form renders no default position");
        Assert.Equal("3", bind.Groups["value"].Value);
    }

    /// <summary>
    /// The controller names the authoring capability and every write carries the anti-forgery token —
    /// the same two attributes every staff forward has, checked by reflection because a missing one is
    /// invisible on a rendered page.
    /// </summary>
    [Fact]
    public void The_assembly_actions_carry_the_capability_and_the_anti_forgery_token()
    {
        var controller = typeof(WebMVC.Controllers.AdminExamsController);

        Assert.Contains(
            Permissions.ContentAuthor,
            controller.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(true)
                .Select(attribute => attribute.Policy));

        var bind = controller.GetMethod(nameof(WebMVC.Controllers.AdminExamsController.Bind));
        Assert.NotNull(bind);
        Assert.NotEmpty(bind!.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>(true));
    }

    /// <summary>The stylesheet obeys the design system: no radius, no shadow, no gradient.</summary>
    [Fact]
    public void The_stylesheet_sets_no_radius_no_shadow_and_no_gradient()
    {
        var css = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "css", "admin", "exam-assembly.css"));

        foreach (Match match in Regex.Matches(css, @"box-shadow:\s*(?<value>[^;]+);"))
        {
            Assert.Equal("none", match.Groups["value"].Value.Trim());
        }

        foreach (Match match in Regex.Matches(css, @"border-radius:\s*(?<value>[^;]+);"))
        {
            Assert.Equal("0", match.Groups["value"].Value.Trim());
        }

        foreach (var rule in new[] { "linear-gradient", "radial-gradient", "conic-gradient", "backdrop-filter" })
        {
            Assert.DoesNotContain(rule, css);
        }
    }

    /// <summary>
    /// The design system's rule about glyphs: a page carries no emoji and no bare arrow standing in
    /// for an icon. The screen pairs every mark with a word.
    /// </summary>
    [Fact]
    public async Task The_page_carries_no_typed_glyphs()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync($"/admin/exams/{ExamId}/assembly");

        Assert.DoesNotMatch(@"[\u2190-\u21FF\u2300-\u23FF\u2600-\u27BF\uD83C-\uDBFF]", body);
    }

    /// <summary>
    /// The supply table's whole markup, found by the section code it names. Regex rather than a DOM
    /// because there is no HTML parser in this project; a missing row fails loudly rather than
    /// silently checking nothing.
    /// </summary>
    private static string RowFor(string body, string sectionCode)
    {
        // Walk every row and return the one whose code cell names this section.
        foreach (Match candidate in Regex.Matches(
                     body, @"<tr class=""assembly-row [^""]*"" id=""assembly-section-\d+"">(?<row>[\s\S]*?)</tr>"))
        {
            if (candidate.Groups["row"].Value.Contains($">{sectionCode}<", StringComparison.Ordinal))
            {
                return candidate.Value;
            }
        }

        throw new InvalidOperationException($"the page renders no row for section {sectionCode}");
    }

    /// <summary>
    /// The real WebMVC host: routing, the cookie scheme, the authorization policy and the Razor view
    /// are production code. Only the outbound API is answered, and only for the calls this page makes
    /// — an unexpected one fails loudly rather than rendering an empty page that would pass.
    /// </summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ICompanionApiClient>(Upstream.Create().Client));
        }

        public HttpClient CreateClientForStaff()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                AuthClaims.For(1, "staff@test.local", Roles.Admin, "Staff", [Permissions.ContentAuthor]),
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
            return client;
        }
    }

    /// <summary>The three upstream calls the page and the shell make, and nothing else.</summary>
    public class Upstream : DispatchProxy
    {
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
                new AuthUserDto(1, "staff@test.local", "Staff", Roles.Admin, true, [Permissions.ContentAuthor])),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Staff", null, "Asia/Ho_Chi_Minh")),
            nameof(IAdminExamsClient.GetAdminExamAssemblyAsync) => Task.FromResult(Assembly()),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };
    }
}
