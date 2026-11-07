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
/// #205 / S-02: the ingestion and staging workbench, rendered through the real host.
/// <para>
/// <b>The criterion this file exists for:</b> the verdict list renders <em>one row per record</em>.
/// The trap is that a list built from refusals satisfies "shows the failing rule and the line" and
/// passes any test that counts rows on a bad file - so the fixture here has accepted records beside
/// refused ones, one refused record carries two rules, and the assertions are the record count plus
/// the presence of an accepted row. A page that rendered one row per error fails the first.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class IngestionWorkbenchViewTests : IClassFixture<IngestionWorkbenchViewTests.Site>
{
    private readonly Site site;

    public IngestionWorkbenchViewTests(Site site) => this.site = site;

    /// <summary>
    /// Criterion 5, precisely. Four records: two accepted in the first group, two refused in the
    /// second, and the first refused one breaks <b>two</b> rules - so a list keyed on errors would
    /// have five rows where the file had four.
    /// </summary>
    [Fact]
    public async Task The_verdict_list_renders_exactly_one_row_per_record_with_the_accepted_ones_present()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/ingestion/7");

        // One row per record: the fixture has 4 records and one of them breaks two rules.
        var rows = Regex.Matches(body, @"<tr[^>]*class=""ingestion-verdict[^""]*""[^>]*>");
        Assert.Equal(4, rows.Count);

        // And the two accepted records are present and distinguishable, not omitted.
        var accepted = Regex.Matches(body, @"data-accepted=""true""");
        var refused = Regex.Matches(body, @"data-accepted=""false""");
        Assert.Equal(2, accepted.Count);
        Assert.Equal(2, refused.Count);

        // The line number and the failing rule are both in the row the author has to act on:
        // line 141 was refused by gate one, and line 160 broke gate one and gate four.
        Assert.Contains("data-line=\"141\"", body, StringComparison.Ordinal);
        Assert.Contains("data-line=\"160\"", body, StringComparison.Ordinal);
        Assert.Contains("gate1.feedback.explanation", body, StringComparison.Ordinal);
        Assert.Contains("gate4.span.overlap", body, StringComparison.Ordinal);

        // The accepted rows carry no rule at all - a row that named a rule would not be accepted.
        var acceptedBlock = body[body.IndexOf("data-accepted=\"true\"", StringComparison.Ordinal)..];
        Assert.DoesNotContain("gate1.", acceptedBlock[..acceptedBlock.IndexOf("</tr>", StringComparison.Ordinal)],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterion 2 at the screen: the staged items are labelled as staged, not as live, and the page
    /// says in words that selection excludes them. <b>Which guard could this pass while being wrong:</b>
    /// a heading that reads "Live" over the same rows. The assertion is the label and the sentence.
    /// </summary>
    [Fact]
    public async Task Staged_items_are_labelled_as_staged_and_the_page_says_they_are_not_live()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/ingestion/7");

        Assert.Contains("Staged, awaiting review", body, StringComparison.Ordinal);
        Assert.DoesNotContain(">Live<", body, StringComparison.Ordinal);
        Assert.Contains("not live", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-lifecycle=\"validated\"", body, StringComparison.Ordinal);

        // The cleared item is in service and says so, so the two states are distinguishable in words.
        Assert.Contains("In service", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterion 4 at the screen: the batch names its uploader and its time. <b>Which guard could this
    /// pass while being wrong:</b> a page that shows the file name only. The assertion is the account
    /// and a formatted timestamp, both of which come from the batch row rather than the file.
    /// </summary>
    [Fact]
    public async Task The_batch_names_who_submitted_it_and_when()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/ingestion/7");

        Assert.Contains("author@test.local", body, StringComparison.Ordinal);
        Assert.Contains("12 Oct 2026 09:30", body, StringComparison.Ordinal);
        Assert.Contains("reading-pool.json", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterion 3 at the screen: a refused record carries its own text in an editable box, so it can
    /// be corrected without re-uploading the file. <b>Which guard could this pass while being wrong:</b>
    /// a form with an empty textarea - it would look like a correction path and be a rewrite from
    /// memory. The assertion is that the box holds the record's JSON.
    /// </summary>
    [Fact]
    public async Task A_refused_record_offers_its_own_text_for_correction()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/ingestion/7");

        var form = Regex.Match(
            body,
            @"<form[^>]*action=""/admin/ingestion/7/records/141""[^>]*>.*?</form>",
            RegexOptions.Singleline);
        Assert.True(form.Success, "the refused record at line 141 has no correction form");

        Assert.Contains("item_demo_depotitem00000011", form.Value, StringComparison.Ordinal);
        Assert.Contains("Verbatim quote", form.Value, StringComparison.Ordinal);
        Assert.Contains("Judge this record again", form.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR-0002 and §1.2: the browser posts to the MVC routes, never to the API. <b>Which guard could
    /// this pass while being wrong:</b> a page that carried the API path in a form action would work in
    /// development and put the call in a request the browser made itself.
    /// </summary>
    [Fact]
    public async Task Every_form_posts_to_the_mvc_routes_and_carries_the_antiforgery_token()
    {
        // Review capability, because the clearance form only exists for a reviewer - the author-only
        // client would be a page with one form, and the assertion below names two.
        using var client = site.CreateClientForStaff(Permissions.ContentAuthor, Permissions.ContentReview);

        var body = await client.GetStringAsync("/admin/ingestion/7");

        Assert.DoesNotContain("/api/admin/ingestion", body, StringComparison.Ordinal);
        Assert.Contains("action=\"/admin/ingestion/7/records/141\"", body, StringComparison.Ordinal);
        Assert.Contains("action=\"/admin/ingestion/7/items/11/clear\"", body, StringComparison.Ordinal);

        // Every form on the page carries a token: a post without one is refused, so a form that
        // forgot it would be a button that does nothing. Counted against the forms that POST.
        var forms = Regex.Matches(body, @"<form\b[^>]*method=""post""[^>]*>", RegexOptions.IgnoreCase);
        var tokens = Regex.Matches(body, @"name=""__RequestVerificationToken""");
        Assert.True(forms.Count > 0, "the page renders no posting form");
        Assert.True(
            tokens.Count >= forms.Count,
            $"{forms.Count} posting form(s) but only {tokens.Count} anti-forgery token(s)");
    }

    /// <summary>
    /// §5: the stylesheet is the painted-sign system - no radius, no shadow, no gradient, no blur.
    /// <b>Which guard could this pass while being wrong:</b> relying on the global reset while one
    /// panel sets a radius is still the defect, so the scan is over the whole sheet.
    /// </summary>
    [Fact]
    public void The_stylesheet_sets_no_radius_no_shadow_and_no_gradient()
    {
        var css = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "css", "admin", "ingestion.css"));

        foreach (Match match in Regex.Matches(css, @"box-shadow:\s*(?<value>[^;]+);"))
        {
            Assert.Equal("none", match.Groups["value"].Value.Trim());
        }

        foreach (Match match in Regex.Matches(css, @"border-radius:\s*(?<value>[^;]+);"))
        {
            Assert.Equal("0", match.Groups["value"].Value.Trim());
        }

        foreach (var rule in new[] { "linear-gradient", "radial-gradient", "conic-gradient", "backdrop-filter", "text-shadow" })
        {
            Assert.DoesNotContain(rule, css, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// §5: no emoji or bare glyph standing in for an icon. <b>Which guard could this pass while being
    /// wrong:</b> an assertion over the words only. A tick glyph beside "Staged" would carry meaning
    /// the stylesheet's colour does not, and would vanish for a screen reader.
    /// </summary>
    [Fact]
    public async Task The_page_carries_no_typed_glyphs()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/ingestion/7");

        Assert.DoesNotMatch(@"[\u2190-\u21FF\u2300-\u23FF\u2600-\u27BF\uD83C-\uDBFF]", body);
    }

    /// <summary>
    /// The upload list, which is the first screen. A batch whose every record was refused must not
    /// read as "done": <b>which guard could this pass while being wrong:</b> rendering the raw
    /// `done` code and calling it a success.
    /// </summary>
    [Fact]
    public async Task The_batch_list_shows_who_submitted_each_batch_and_never_calls_a_refusal_done()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/ingestion");

        Assert.Contains("author@test.local", body, StringComparison.Ordinal);
        Assert.Contains("reviewer@test.local", body, StringComparison.Ordinal);
        Assert.Contains("Nothing accepted", body, StringComparison.Ordinal);
        Assert.DoesNotContain(">done<", body, StringComparison.Ordinal);
    }

    /// <summary>An author without the review namespace does not see the clearance control.</summary>
    [Fact]
    public async Task An_account_without_review_gets_no_clearance_control()
    {
        using var client = site.CreateClientForStaff(Permissions.ContentAuthor);

        var body = await client.GetStringAsync("/admin/ingestion/7");

        Assert.DoesNotContain("Clear for service", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/clear", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same page for a reviewer: the control is present, which proves the absence above is a
    /// capability check rather than the control never being rendered.
    /// </summary>
    [Fact]
    public async Task An_account_with_review_gets_the_clearance_control()
    {
        using var client = site.CreateClientForStaff(Permissions.ContentAuthor, Permissions.ContentReview);

        var body = await client.GetStringAsync("/admin/ingestion/7");

        Assert.Contains("Clear for service", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The real WebMVC host. Only the outbound API is answered, and only for the calls these pages
    /// make - an unexpected call fails loudly rather than rendering an empty page that would pass.
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

        /// <summary>
        /// A staff client holding the given capabilities. The default is authoring only, which is what
        /// the upload screen needs; the clearance assertions ask for review explicitly.
        /// </summary>
        public HttpClient CreateClientForStaff(params string[] permissions)
        {
            var held = permissions.Length == 0 ? [Permissions.ContentAuthor] : permissions;

            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                AuthClaims.For(1, "author@test.local", Roles.Admin, "Staff", held),
                CookieAuthenticationDefaults.AuthenticationScheme,
                nameType: IAccessTokenFactory.ClaimEmail,
                roleType: IAccessTokenFactory.ClaimRole));
            var ticket = new AuthenticationTicket(
                principal,
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    // The cookie is re-validated against the API on every request, and a capability
                    // set that disagrees with GetMe signs the account out. Encoding the held set in
                    // the token is how the stub answers with the same set the cookie carries.
                    ["jwt"] = string.Join(',', held),
                })
                {
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
                },
                CookieAuthenticationDefaults.AuthenticationScheme);

            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
            return client;
        }
    }

    /// <summary>
    /// The upstream calls these pages make. The fixture is the one criterion 5 describes: two accepted
    /// records, two refused, and the first refused record breaks two rules.
    /// </summary>
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
                // The held set is the caller's own, so the cookie's capabilities and the API's agree -
                // the stub is answering for whoever the test signed in as.
                new AuthUserDto(1, "author@test.local", "Staff", Roles.Admin, true,
                    (args![0] as string ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .ToList())),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Staff", null, "Asia/Ho_Chi_Minh")),
            nameof(IAdminIngestionClient.GetWorkbenchBatchesAsync) => Task.FromResult(List()),
            nameof(IAdminIngestionClient.GetWorkbenchBatchAsync) => Task.FromResult(Detail()),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };

        private static IngestionBatchListDto List() => new(
        [
            new IngestionBatchSummaryDto(
                7, "reading-pool.json", "done", 1, "author@test.local",
                new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 12, 9, 31, 0, TimeSpan.Zero), 4, 2),
            // Every record refused: the screen must not call this "done".
            new IngestionBatchSummaryDto(
                8, "broken.json", "failed", 2, "reviewer@test.local",
                new DateTimeOffset(2026, 10, 13, 11, 0, 0, TimeSpan.Zero), null, 3, 0),
        ]);

        private static IngestionBatchDetailDto Detail() => new(
            new IngestionBatchSummaryDto(
                7, "reading-pool.json", "done", 1, "author@test.local",
                new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 12, 9, 31, 0, TimeSpan.Zero), 4, 2),
            [
                new IngestionRecordDto(26, 1, true, 11, [],
                    """{ "item_id": "item_demo_depotitem00000000" }"""),
                new IngestionRecordDto(57, 1, true, 12, [],
                    """{ "item_id": "item_demo_depotitem00000001" }"""),
                new IngestionRecordDto(141, 1, false, null,
                    [new IngestionRefusalDto("gate1.feedback.explanation",
                        "the explanation is empty; gate one refuses an item whose key cannot be justified.")],
                    """{ "item_id": "item_demo_depotitem00000011", "verbatim_quote": "Verbatim quote" }"""),
                // Two rules on one record: a row-per-error list renders five rows for four records.
                new IngestionRecordDto(160, 1, false, null,
                    [
                        new IngestionRefusalDto("gate1.feedback.explanation", "the explanation is empty."),
                        new IngestionRefusalDto("gate4.span.overlap", "its evidence span overlaps its sibling's."),
                    ],
                    """{ "item_id": "item_demo_depotitem00000021" }"""),
            ],
            [
                new IngestionStagedItemDto(11, "R-DET", "B1", "reading", "validated", 1),
                new IngestionStagedItemDto(12, "R-INF", "B1", "reading", "active", 1),
            ]);
    }
}
