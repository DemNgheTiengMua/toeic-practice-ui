using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
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
/// #208 / S-08: the rubric and grader calibration studio's screen.
/// <para>
/// <b>The 28-cell assertion is on the COORDINATE, not the count.</b> A page with 28 <c>&lt;input&gt;</c>
/// elements is not a 4x7 grid: it could be seven criteria by four bands, or 28 cells with two of them
/// disabled, or 28 cells in one row. Every test below therefore pins a criterion id and a band number and
/// asks for that cell, then asserts the cell is editable.
/// </para>
/// <para>
/// Rendered through the real host, because the defect this catches is a render one - a cell that exists
/// but is not editable, or a ladder panel that paints "not plotted" as a pass.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class RubricStudioViewTests : IClassFixture<RubricStudioViewTests.Site>
{
    private readonly Site site;

    public RubricStudioViewTests(Site site) => this.site = site;

    // -----------------------------------------------------------------------------------------
    // AC5: the grid renders twenty-eight editable cells, by coordinate
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// AC1 and AC5 in one assertion. <b>Which guard could this pass while being wrong:</b> counting
    /// <c>&lt;input&gt;</c> elements. Twenty-eight inputs pass a count and still be the wrong grid - the
    /// shape could be seven criteria by four bands, or two of the 28 could be disabled. So every one of
    /// the 28 coordinates is requested by name below, and each must carry a writable field.
    /// </summary>
    [Fact]
    public async Task The_grid_renders_twenty_eight_editable_cells_one_per_criterion_and_band()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/rubric?certId=1&skillId=1");

        // Four criteria, each with all seven bands: 28 coordinates, each asked for by name.
        var coordinates = new List<string>();
        foreach (var criterion in new[] { 11, 12, 13, 14 })
        {
            foreach (var band in new byte[] { 0, 1, 2, 3, 4, 5, 6 })
            {
                var cell = Cell(body, criterion, band);
                coordinates.Add($"{criterion}x{band}");

                // Editable means a textarea for the descriptor and one for the gates, neither disabled
                // and neither readonly. A disabled field is a cell an author cannot fill, which is the
                // "28 cells but two are disabled" mutation.
                var descriptor = Field(cell, criterion, band, "descriptor");
                var gates = Field(cell, criterion, band, "gates");
                Assert.DoesNotContain("disabled", descriptor, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("readonly", descriptor, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("disabled", gates, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("readonly", gates, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Equal(28, coordinates.Distinct().Count());
    }

    /// <summary>
    /// The mutation the coordinate assertion is for: <b>which guard could this pass while being wrong:</b>
    /// a grid of seven criteria by four bands. Every cell above would be found if the page had 28 cells in
    /// any shape; this test asserts the shape itself - four rows and seven cells in each.
    /// </summary>
    [Fact]
    public async Task The_grid_is_four_rows_and_seven_cells_never_seven_rows_and_four()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/rubric?certId=1&skillId=1");

        var rows = Regex.Matches(body, @"<tr class=""rubric-grid__row""[^>]*data-criterion=""(?<criterion>\d+)""");
        Assert.Equal(4, rows.Count);
        Assert.Equal(
            new[] { "11", "12", "13", "14" },
            rows.Select(m => m.Groups["criterion"].Value).ToArray());

        // Each row carries bands 0..6 in order, read off the cells' own data-band attributes.
        var cells = Regex.Matches(body, @"class=""rubric-grid__cell""[^>]*data-band=""(?<band>\d+)""");
        Assert.Equal(28, cells.Count);
        for (var row = 0; row < 4; row++)
        {
            var slice = cells.Skip(row * 7).Take(7).Select(m => m.Groups["band"].Value).ToList();
            Assert.Equal(["0", "1", "2", "3", "4", "5", "6"], slice);
        }
    }

    /// <summary>
    /// AC2 at the screen. <b>Which guard could this pass while being wrong:</b> three anchor fields
    /// anywhere on the page. The anchors have to be per criterion and at bands 2, 3 and 4 - a criterion
    /// with two anchors, or a fourth at band 5, is the defect the schema's CK_GCA_Band refuses and the
    /// screen has to make unreachable.
    /// </summary>
    [Fact]
    public async Task Anchors_are_offered_for_every_criterion_at_bands_two_three_and_four()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/rubric?certId=1&skillId=1");

        foreach (var criterion in new[] { 11, 12, 13, 14 })
        {
            foreach (var band in new byte[] { 2, 3, 4 })
            {
                Assert.Matches(
                    $@"data-field=""anchor""[^>]*data-criterion=""{criterion}""[^>]*data-band=""{band}""",
                    Field(body, criterion, band, "anchor"));
            }

            // And no anchor slot at a band outside 2..4.
            foreach (var band in new byte[] { 0, 1, 5, 6 })
            {
                Assert.DoesNotMatch(
                    $@"data-field=""anchor""[^>]*data-criterion=""{criterion}""[^>]*data-band=""{band}""",
                    body);
            }
        }
    }

    /// <summary>
    /// AC3 and the ticket's trap. <b>Which guard could this pass while being wrong:</b> a panel that says
    /// "not run" in a way that reads like a pass, or one with no verdict element at all. The element must
    /// exist, carry the <c>not_plotted</c> outcome so the stylesheet paints it gold, and say in words that
    /// the ladder has not run and that the lock is not available.
    /// </summary>
    [Fact]
    public async Task An_unrun_ladder_is_painted_as_not_plotted_and_never_as_a_pass()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/rubric?certId=1&skillId=1");

        var verdict = Element(body, "rubric-ladder-verdict");
        Assert.Contains("data-outcome=\"not_plotted\"", verdict);
        Assert.Contains("has not run", body, StringComparison.OrdinalIgnoreCase);

        // The panel's own state attribute is what the script toggles, and it starts false.
        Assert.Contains("data-has-run=\"false\"", Element(body, "rubric-ladder"));
    }

    /// <summary>
    /// AC3: the plot. <b>Which guard could this pass while being wrong:</b> a panel with a verdict and no
    /// bars - "the ladder ran" with nothing plotted is exactly the <c>LadderNotPlotted</c> state under
    /// another name. The seven bars are asserted by band, with their composites.
    /// </summary>
    [Fact]
    public async Task A_run_ladder_plots_one_bar_per_band_and_the_trap()
    {
        using var client = site.CreateClientForStaff();

        // The stub returns an accepted ladder with seven points; a different upstream would answer the
        // same call with a refusal, which the next test drives.
        var body = await client.GetStringAsync("/admin/studio/rubric?certId=1&skillId=2");

        var bars = Regex.Matches(
            body, @"class=""rubric-ladder__bar[^""]*""\s+data-band=""(?<band>\d+)""\s+data-composite=""(?<composite>[0-9.]+)""");
        Assert.Equal(7, bars.Count);
        Assert.Equal(
            ["1", "2", "3", "4", "5", "6", "3"],
            bars.Select(m => m.Groups["band"].Value).ToList());
    }

    /// <summary>
    /// AC4 at the screen: a refused ladder names its failure. <b>Which guard could this pass while being
    /// wrong:</b> a single generic "the rubric was refused" - the three failures are three different
    /// rewrites, and this test drives the separation one and asserts the criterion the API named.
    /// </summary>
    [Fact]
    public async Task A_refused_ladder_names_the_criterion_the_api_named()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/rubric?certId=1&skillId=3");

        var verdict = Element(body, "rubric-ladder-verdict");
        Assert.Contains("data-outcome=\"separation\"", verdict);
        Assert.Contains("minimum separation", body, StringComparison.OrdinalIgnoreCase);

        // The criterion the API named must be legible on the page, or the refusal sends the author to
        // four rows with no way to tell which one stalled.
        var responsible = Regex.Match(
            body,
            @"<p class=""rubric-ladder__responsible"" id=""rubric-ladder-responsible"">(?<text>.*?)</p>",
            RegexOptions.Singleline);
        Assert.True(responsible.Success, "the page renders no responsible-criterion element");
        Assert.Contains("W_GR", responsible.Groups["text"].Value);
    }

    /// <summary>
    /// The page ships the ladder's own numbers rather than a second copy, and tells the reader which band
    /// is which. <b>Which guard could this pass while being wrong:</b> a hardcoded 0.65 in the script -
    /// it would agree today and silently disagree the day the ladder's constant moves.
    /// </summary>
    [Fact]
    public async Task The_page_ships_the_band_labels_and_the_ladder_bounds()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/rubric?certId=1&skillId=1");

        Assert.Contains("Pre-A1", body);
        Assert.Contains("C2", body);

        var json = WebUtility.HtmlDecode(
            Regex.Match(body, @"<script type=""application/json"" id=""rubric-config"">(?<json>.*?)</script>", RegexOptions.Singleline)
                .Groups["json"].Value);
        Assert.Contains("\"anchorBands\":[2,3,4]", json);
        Assert.Contains("\"separation\":0.65", json);
        Assert.Contains("\"trapCeiling\":3.25", json);
    }

    /// <summary>
    /// §5: no emoji and no raw glyph standing in for an icon. <b>Which guard could this pass while being
    /// wrong:</b> checking one glyph. The scan is the whole body over the private-use and dingbat ranges.
    /// </summary>
    [Fact]
    public async Task The_page_carries_no_typed_glyphs()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/rubric?certId=1&skillId=1");

        Assert.DoesNotMatch(@"[\u2190-\u21FF\u2300-\u23FF\u2600-\u27BF\uD83C-\uDBFF]", body);
    }

    /// <summary>
    /// §5 and the design system: no radius, no shadow, no gradient. <b>Which guard could this pass while
    /// being wrong:</b> a page that relies on the global reset for most elements and sets a radius on one
    /// panel - so the scan is over the whole stylesheet.
    /// </summary>
    [Fact]
    public void The_stylesheet_sets_no_radius_no_shadow_and_no_gradient()
    {
        var css = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "css", "admin", "rubric-studio.css"));

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

    // -------------------------------------------------------------------------------------------
    // AC2 and the round trip: the forwards are MVC, carry the token, and reach the API's shape
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// ADR-0002 and §1.2. <b>Which guard could this pass while being wrong:</b> asserting only that the
    /// route renders. An action with the policy and no <c>[ValidateAntiForgeryToken]</c> accepts a
    /// cross-site POST; an action with the token and no policy accepts any signed-in learner.
    /// </summary>
    [Fact]
    public void Every_forward_carries_the_anti_forgery_token_and_the_blueprint_capability()
    {
        var controller = typeof(WebMVC.Controllers.AdminRubricStudioController);

        var policy = controller.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(true)
            .Select(attribute => attribute.Policy)
            .ToList();
        Assert.Contains(Permissions.BlueprintDesign, policy);

        foreach (var name in new[] { nameof(WebMVC.Controllers.AdminRubricStudioController.Save),
                                     nameof(WebMVC.Controllers.AdminRubricStudioController.Ladder),
                                     nameof(WebMVC.Controllers.AdminRubricStudioController.Lock) })
        {
            var action = controller.GetMethod(name);
            Assert.NotNull(action);
            Assert.NotEmpty(action!.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>(true));
        }
    }

    /// <summary>
    /// The page's script posts to the MVC forwards and never to the API.
    /// <b>Which guard could this pass while being wrong:</b> a script that posts to <c>/api/...</c> works
    /// in dev and puts the cookie's token in a request the browser made itself.
    /// </summary>
    [Fact]
    public void The_script_posts_to_the_mvc_forwards_and_carries_the_token()
    {
        var script = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "js", "admin", "rubric-studio.js"));

        Assert.Contains("/admin/studio/rubric/save", script);
        Assert.Contains("/admin/studio/rubric/ladder", script);
        Assert.Contains("/admin/studio/rubric/lock", script);
        Assert.Contains("RequestVerificationToken", script);
        Assert.DoesNotContain("/api/admin/studio", script);
    }

    /// <summary>
    /// AC2's round trip: an anchor posted through the real MVC forward reaches the API at its band.
    /// <b>Which guard could this pass while being wrong:</b> a page that echoed what the author typed -
    /// the assertion is on the request the API received, not on the response body.
    /// </summary>
    [Fact]
    public async Task A_grid_save_round_trips_its_coordinates_and_anchors_to_the_api()
    {
        using var client = site.CreateClientForStaff();
        await client.GetStringAsync("/admin/studio/rubric?certId=1&skillId=1");

        var request = new SaveRubricRequest(1, 1, [
            Criterion(11, "W_TR"),
            Criterion(12, "W_CC"),
            Criterion(13, "W_LR"),
            Criterion(14, "W_GR"),
        ]);

        var response = await PostJsonAsync(client, "/admin/studio/rubric/save", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sent = site.Upstream.LastSave;
        Assert.NotNull(sent);
        Assert.Equal(4, sent!.Criteria.Count);
        Assert.All(sent.Criteria, criterion =>
        {
            // The coordinate survived: seven cells, each on its own band.
            Assert.Equal([0, 1, 2, 3, 4, 5, 6], criterion.Cells.Select(c => (int)c.Band));
            // And the anchors, at exactly 2, 3 and 4.
            Assert.Equal([2, 3, 4], criterion.Anchors.Select(a => (int)a.Band));
            // Descriptor and gates are distinct facts, not one text in two columns.
            Assert.All(criterion.Cells, cell => Assert.NotEqual(cell.Descriptor, cell.BinaryGates));
        });
    }

    // -------------------------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------------------------

    private static RubricCriterionDto Criterion(int id, string code) =>
        new(id, code, code, 6m, 1m, id - 10,
            RubricBands.All.Select(b => new RubricCellDto(b, $"{code} band {b}", $"{code} gate {b}")).ToList(),
            RubricBands.AnchorBands.Select(b => new RubricAnchorDto(b, $"{code} anchor {b}")).ToList());

    /// <summary>
    /// The cell at (criterion, band) INCLUDING its contents, so a field inside it can be asserted. The
    /// coordinate is the assertion: a page that renders cells in any other shape cannot produce this
    /// opening tag, and a cell with no fields inside it fails the next assertion.
    /// </summary>
    private static string Cell(string body, int criterion, byte band)
    {
        var match = Regex.Match(
            body,
            $@"<td class=""rubric-grid__cell""[^>]*data-criterion=""{criterion}""[^>]*data-band=""{band}""[^>]*>(?<inner>.*?)</td>",
            RegexOptions.Singleline);
        Assert.True(match.Success, $"no cell at criterion {criterion}, band {band}");
        return match.Value;
    }

    /// <summary>The field tag for (criterion, band, field) within the given scope.</summary>
    private static string Field(string scope, int criterion, byte band, string field)
    {
        var match = Regex.Match(
            scope,
            $@"<textarea[^>]*data-field=""{field}""[^>]*data-criterion=""{criterion}""[^>]*data-band=""{band}""[^>]*>");
        Assert.True(match.Success, $"no {field} field at criterion {criterion}, band {band}");
        return match.Value;
    }

    private static string Element(string body, string id)
    {
        var match = Regex.Match(
            body,
            $@"<(?<tag>[a-z0-9]+)(?<attrs>[^>]*\bid=""{Regex.Escape(id)}""[^>]*)>");
        Assert.True(match.Success, $"the page renders no element with id '{id}'");
        return match.Value;
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        var page = await client.GetStringAsync("/admin/studio/rubric?certId=1&skillId=1");
        var token = Regex.Match(page, @"name=""__RequestVerificationToken""[^>]*value=""(?<token>[^""]+)""")
            .Groups["token"].Value;

        var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        message.Headers.Add("RequestVerificationToken", token);

        return await client.SendAsync(message);
    }

    /// <summary>
    /// The real WebMVC host: routing, the cookie scheme, the authorization policy, the anti-forgery
    /// middleware and the Razor view are production code. Only the outbound API is answered, and only for
    /// the calls this page makes.
    /// </summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        public Upstream Upstream { get; } = Upstream.Create();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ICompanionApiClient>(_ => Upstream.Client));
        }

        public HttpClient CreateClientForStaff()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                AuthClaims.For(1, "staff@test.local", Roles.Admin, "Staff", [Permissions.BlueprintDesign]),
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

    /// <summary>
    /// The upstream calls the page and the shell make, and nothing else - an unexpected call fails loudly
    /// rather than rendering an empty page that would pass. Three skills are answered differently so the
    /// panel's three states are each reachable.
    /// </summary>
    public class Upstream : DispatchProxy
    {
        public ICompanionApiClient Client { get; private set; } = null!;

        public SaveRubricRequest? LastSave { get; private set; }

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
                new AuthUserDto(1, "staff@test.local", "Staff", Roles.Admin, true, [Permissions.BlueprintDesign])),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Staff", null, "Asia/Ho_Chi_Minh")),

            nameof(IRubricStudioClient.GetRubricStudioAsync) => Task.FromResult(
                Studio((int)args![0]!, (int)args[1]!)),

            nameof(IRubricStudioClient.SaveRubricAsync) => Save(args!),

            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };

        private Task<RubricStudioDto> Save(object?[] args)
        {
            LastSave = (SaveRubricRequest)args[0]!;
            return Task.FromResult(Studio(LastSave.CertId, LastSave.SkillId, LastSave));
        }

        /// <summary>
        /// Three skills, three ladder states: unlocked and never run (skill 1), locked and accepted
        /// (skill 2), and unlocked with a separation refusal naming W_GR (skill 3).
        /// </summary>
        private static RubricStudioDto Studio(int certId, int skillId, SaveRubricRequest? saved = null)
        {
            var criteria = saved?.Criteria ?? [
                new RubricCriterionDto(11, "W_TR", "Task fulfilment", 6m, 1m, 1, [], []),
                new RubricCriterionDto(12, "W_CC", "Coherence and cohesion", 6m, 1m, 2, [], []),
                new RubricCriterionDto(13, "W_LR", "Lexical resource", 6m, 1m, 3, [], []),
                new RubricCriterionDto(14, "W_GR", "Grammatical range", 6m, 1m, 4, [], []),
            ];

            var ladder = skillId switch
            {
                2 => new RubricLadderDto(
                    RubricLadderOutcomes.Accepted, true, "Every adjacent band separated by at least 0.65.",
                    [],
                    [
                        new RubricLadderPointDto(1, 1.00m, false),
                        new RubricLadderPointDto(2, 2.00m, false),
                        new RubricLadderPointDto(3, 3.00m, false),
                        new RubricLadderPointDto(4, 4.00m, false),
                        new RubricLadderPointDto(5, 5.00m, false),
                        new RubricLadderPointDto(6, 6.00m, false),
                        new RubricLadderPointDto(3, 3.10m, true),
                    ],
                    DateTimeOffset.UnixEpoch),
                3 => new RubricLadderDto(
                    RubricLadderOutcomes.Separation, false,
                    "Bands 3 and 4 are only 0.40 apart and the rubric needs at least 0.65. Criterion W_GR moved the least across that step.",
                    ["W_GR"],
                    [
                        new RubricLadderPointDto(1, 1.00m, false),
                        new RubricLadderPointDto(2, 1.70m, false),
                        new RubricLadderPointDto(3, 2.40m, false),
                        new RubricLadderPointDto(4, 2.80m, false),
                        new RubricLadderPointDto(5, 3.60m, false),
                        new RubricLadderPointDto(6, 4.40m, false),
                        new RubricLadderPointDto(3, 2.20m, true),
                    ],
                    DateTimeOffset.UnixEpoch),
                _ => null,
            };

            return new RubricStudioDto(
                certId, "DEMO", skillId, "WRIT", "Writing", "writing",
                IsLocked: skillId == 2,
                LockedAt: skillId == 2 ? DateTimeOffset.UnixEpoch : null,
                criteria,
                ladder);
        }
    }
}
