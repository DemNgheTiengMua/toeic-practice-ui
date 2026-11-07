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
/// #206 / S-03: the stratified audit queue, rendered through the real host.
/// <para>
/// <b>The criterion this file exists for:</b> the screen says in words that approving a stratum promotes
/// the reviewed sample <em>and</em> the unsampled remainder. D17 calls that sentence out because an
/// implementer who promotes only what was reviewed satisfies the button and stalls the pool at 10% of
/// target volume. A test on the returned DTO would pass a page that never said it.
/// </para>
/// <para>
/// <b>And the second:</b> blind solve is an order, not a label. The queue must carry no authored key at
/// all - so the assertion is the absence of the key value from the page body, not the presence of a
/// "hidden" attribute.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class AuditQueueViewTests : IClassFixture<AuditQueueViewTests.Site>
{
    private readonly Site site;

    public AuditQueueViewTests(Site site) => this.site = site;

    /// <summary>
    /// Criterion 4 at the screen. <b>Which guard could this pass while being wrong:</b> a sentence that
    /// says "approving the stratum promotes it" and leaves the reviewer to assume it means the whole
    /// cell. The assertion is both numbers and the words "AND" and "unsampled".
    /// </summary>
    [Fact]
    public async Task The_screen_says_an_approval_promotes_the_reviewed_and_the_unsampled_together()
    {
        using var client = site.CreateClientForReviewer();

        var body = await client.GetStringAsync("/admin/audit/7");

        var sentence = Regex.Match(
            body, @"<p[^>]*id=""audit-promotion-sentence""[^>]*>(?<text>.*?)</p>", RegexOptions.Singleline);
        Assert.True(sentence.Success, "the page carries no promotion-scope sentence");

        var text = sentence.Groups["text"].Value;
        Assert.Contains("reviewed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unsampled", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AND", text, StringComparison.Ordinal);
        // The two numbers, so the sentence cannot drift from the table above it: 3 reviewed, 47 unreviewed.
        Assert.Contains("3", text, StringComparison.Ordinal);
        Assert.Contains("47", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The approve button names both halves. <b>Which guard could this pass while being wrong:</b> a
    /// button labelled "Approve sample", which is the exact defect D17 warns about - the reviewer
    /// believes they are publishing 3 items and the system publishes 50.
    /// </summary>
    [Fact]
    public async Task The_approve_button_names_both_halves_of_what_it_publishes()
    {
        using var client = site.CreateClientForReviewer();

        var body = await client.GetStringAsync("/admin/audit/7");

        var button = Regex.Match(body, @"<button[^>]*class=""btn btn--primary audit-decide""[^>]*>(?<text>.*?)</button>",
            RegexOptions.Singleline);
        Assert.True(button.Success, "the page renders no decision button");

        var text = button.Groups["text"].Value;
        Assert.Contains("reviewed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unreviewed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Approve sample", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Criterion 2 at the screen, from the other side: <b>the queue carries no authored key.</b>
    /// <b>Which guard could this pass while being wrong:</b> a key rendered in the page and hidden with
    /// CSS - a reviewer who opens devtools has seen it, and the solve was never blind. The assertion is
    /// that the value is absent from the body.
    /// </summary>
    [Fact]
    public async Task The_queue_page_does_not_carry_the_authored_key_anywhere()
    {
        using var client = site.CreateClientForReviewer();

        var body = await client.GetStringAsync("/admin/audit/7");

        // The stub's authored key for sample 501 is the marker string, so "it is not here" is checkable
        // rather than assumed. The reviewer's own solve IS here, which proves the page is the queue.
        Assert.DoesNotContain("KEY-IS-B", body, StringComparison.Ordinal);
        Assert.Contains("data-key-revealed=\"false\"", body, StringComparison.Ordinal);
        Assert.Contains("Solve blind before reading the key", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The order is the interface's: an unsolved item offers a solve and no reveal; a solved one offers
    /// the reveal and no second solve. <b>Which guard could this pass while being wrong:</b> rendering
    /// both forms for every row and relying on the API to refuse - which the API does, but the screen
    /// would invite a click that always fails.
    /// </summary>
    [Fact]
    public async Task An_unsolved_item_offers_the_solve_and_a_solved_one_offers_the_key()
    {
        using var client = site.CreateClientForReviewer();

        var body = await client.GetStringAsync("/admin/audit/7");

        // Sample 501 is unsolved: a solve form, and no reveal form for it.
        Assert.Contains("action=\"/admin/audit/7/samples/501/solve\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("strata/900/samples/501/reveal", body, StringComparison.Ordinal);

        // Sample 502 is solved and not yet revealed: the reveal form, and no solve form.
        Assert.Contains("strata/900/samples/502/reveal", body, StringComparison.Ordinal);
        Assert.DoesNotContain("action=\"/admin/audit/7/samples/502/solve\"", body, StringComparison.Ordinal);

        // Sample 503 has been solved and judged: neither form, because nothing is left to do.
        Assert.DoesNotContain("action=\"/admin/audit/7/samples/503/solve\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("strata/900/samples/503/reveal", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The quarantine state is in words, and it says which rule it broke. <b>Which guard could this pass
    /// while being wrong:</b> colouring the panel red and printing the raw code <c>key_error</c>, which
    /// makes the author look up a vocabulary to learn what happened.
    /// </summary>
    [Fact]
    public async Task A_quarantined_stratum_names_the_rule_it_broke_in_words()
    {
        using var client = site.CreateClientForReviewer();

        var body = await client.GetStringAsync("/admin/audit/7");

        Assert.Contains("data-status=\"quarantined\"", body, StringComparison.Ordinal);
        Assert.Contains("A confirmed key error. One key error quarantines the whole stratum.", body,
            StringComparison.Ordinal);
        Assert.Contains("Quarantined", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR-0002 and §1.2: the browser posts to the MVC routes, never to the API, and every posting form
    /// carries the token. <b>Which guard could this pass while being wrong:</b> a form action naming
    /// <c>/api/admin/audit</c>, which works in development and puts the call in the browser's own request.
    /// </summary>
    [Fact]
    public async Task Every_form_posts_to_the_mvc_routes_and_carries_the_antiforgery_token()
    {
        using var client = site.CreateClientForReviewer();

        var body = await client.GetStringAsync("/admin/audit/7");

        Assert.DoesNotContain("/api/admin/audit", body, StringComparison.Ordinal);
        Assert.Contains("action=\"/admin/audit/7/samples/501/solve\"", body, StringComparison.Ordinal);
        Assert.Contains("strata/900/samples/502/reveal", body, StringComparison.Ordinal);
        Assert.Contains("action=\"/admin/audit/7/strata/900/decide\"", body, StringComparison.Ordinal);

        var forms = Regex.Matches(body, @"<form\b[^>]*method=""post""[^>]*>", RegexOptions.IgnoreCase);
        var tokens = Regex.Matches(body, @"name=""__RequestVerificationToken""");
        Assert.True(forms.Count > 0, "the page renders no posting form");
        Assert.True(tokens.Count >= forms.Count,
            $"{forms.Count} posting form(s) but only {tokens.Count} anti-forgery token(s)");
    }

    /// <summary>
    /// §5: the stylesheet is the painted-sign system - no radius, no shadow, no gradient, no blur.
    /// <b>Which guard could this pass while being wrong:</b> relying on the global reset while one panel
    /// sets a radius is still the defect, so the scan is over the whole sheet.
    /// </summary>
    [Fact]
    public void The_stylesheet_sets_no_radius_no_shadow_and_no_gradient()
    {
        var css = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "css", "admin", "audit.css"));

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
    /// wrong:</b> a tick or a cross beside a verdict, which carries meaning the stylesheet's colour does
    /// not and vanishes for a screen reader.
    /// </summary>
    [Fact]
    public async Task The_page_carries_no_typed_glyphs()
    {
        using var client = site.CreateClientForReviewer();

        var body = await client.GetStringAsync("/admin/audit/7");

        Assert.DoesNotMatch(@"[\u2190-\u21FF\u2300-\u23FF\u2600-\u27BF\uD83C-\uDBFF]", body);
    }

    /// <summary>
    /// An account without the review capability is refused the whole screen, not shown an empty one.
    /// <b>Which guard could this pass while being wrong:</b> an authoring gate, which would let the
    /// uploading author open the audit of their own batch and learn the keys.
    /// </summary>
    [Fact]
    public async Task An_author_without_the_review_capability_cannot_open_the_queue()
    {
        using var client = site.CreateClientForStaff(Permissions.ContentAuthor);

        var response = await client.GetAsync("/admin/audit/7");

        // MVC's cookie scheme sends a signed-in but unauthorised account to the denied page rather
        // than returning 403; either way it is refused the queue, and the queue is not rendered.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/auth/denied", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reveal page renders both values side by side, which is what makes the judgement a comparison
    /// rather than a recollection. <b>Which guard could this pass while being wrong:</b> a page that
    /// shows the key alone, leaving the reviewer to remember what they answered.
    /// <para>
    /// It is reached by POST - the only verb that may return a key, because a GET is bookmarkable,
    /// prefetchable and lives in history. The token is taken from the queue page, which is the real
    /// flow: a reviewer reads the queue, then asks for the key.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_reveal_page_shows_the_solve_beside_the_key()
    {
        using var client = site.CreateClientForReviewer();

        var queue = await client.GetStringAsync("/admin/audit/7");
        var token = Token(queue);

        var response = await client.PostAsync(
            "/admin/audit/7/strata/900/samples/502/reveal",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"audit-solved-label\"", body, StringComparison.Ordinal);
        Assert.Contains("id=\"audit-key-label\"", body, StringComparison.Ordinal);
        Assert.Contains("KEY-IS-B", body, StringComparison.Ordinal);
        Assert.Contains("A <strong>key error</strong> is a wrong answer key", body, StringComparison.Ordinal);
    }

    /// <summary>The anti-forgery token a rendered page carries, for a test that must post a form.</summary>
    private static string Token(string body)
    {
        var match = Regex.Match(body, @"name=""__RequestVerificationToken""[^>]*value=""(?<token>[^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(body, @"value=""(?<token>[^""]+)""[^>]*name=""__RequestVerificationToken""");
        }

        Assert.True(match.Success, "the page carries no anti-forgery token");
        return match.Groups["token"].Value;
    }

    /// <summary>
    /// The real WebMVC host. Only the outbound API is answered, and only for the calls these pages make -
    /// an unexpected call fails loudly rather than rendering an empty page that would pass.
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

        /// <summary>A staff client holding the review capability, which is what the screen requires.</summary>
        public HttpClient CreateClientForReviewer() => CreateClientForStaff(Permissions.ContentReview);

        public HttpClient CreateClientForStaff(params string[] permissions)
        {
            var held = permissions.Length == 0 ? [Permissions.ContentReview] : permissions;

            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                AuthClaims.For(2, "reviewer@test.local", Roles.Admin, "Staff", held),
                CookieAuthenticationDefaults.AuthenticationScheme,
                nameType: IAccessTokenFactory.ClaimEmail,
                roleType: IAccessTokenFactory.ClaimRole));
            var ticket = new AuthenticationTicket(
                principal,
                new AuthenticationProperties(new Dictionary<string, string?>
                {
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
    /// The upstream calls these pages make. The fixture is what criterion 4 describes: two strata, the
    /// first pending with one unsolved, one solved-unrevealed and one judged sample, the second
    /// quarantined - so the page renders every state at once and 3 reviewed / 47 unreviewed.
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
                new AuthUserDto(2, "reviewer@test.local", "Staff", Roles.Admin, true,
                    (args![0] as string ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .ToList())),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Staff", null, "Asia/Ho_Chi_Minh")),
            nameof(IAdminAuditClient.GetAuditQueueAsync) => Task.FromResult(Queue()),
            nameof(IAdminAuditClient.RevealSampleKeyAsync) => Task.FromResult(
                new AuditKeyRevealDto(502, 21, "KEY-IS-B", "The passage names the ledger.",
                    "C", new DateTimeOffset(2026, 10, 14, 10, 5, 0, TimeSpan.Zero))),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };

        private static AuditQueueDto Queue() => new(
            7, "reading-pool.json", "done",
            [
                new AuditStratumDto(
                    900, 7, "R-DET", "B1", "reading", 20, 2, Sampled: 2, Unsampled: 18,
                    "pending", null, null,
                    [
                        new AuditSampleDto(501, 20, "What time does the depot open?",
                            "The night ledger.", "The depot shutter.", "The depot supervisor.", "The office doorway.",
                            null, null, null, false, null),
                        new AuditSampleDto(502, 21, "Which notice is posted at gate 3?",
                            "The closure notice.", "The rota.", "The ledger extract.", "The shutter schedule.",
                            "C", new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero),
                            null, false, null),
                        new AuditSampleDto(503, 22, "What is the supervisor's name?",
                            "Mara.", "Ovid.", "Sena.", "Tomas.",
                            "A", new DateTimeOffset(2026, 10, 14, 9, 50, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 10, 14, 9, 55, 0, TimeSpan.Zero), true, "correct"),
                    ]),
                new AuditStratumDto(
                    901, 7, "R-INF", "C1", "reading", 30, 1, Sampled: 1, Unsampled: 29,
                    "quarantined", "key_error", new DateTimeOffset(2026, 10, 14, 11, 0, 0, TimeSpan.Zero),
                    [
                        new AuditSampleDto(504, 30, "What does the writer imply about the delay?",
                            "An accident.", "A strike.", "A supplier.", "The weather.",
                            "D", new DateTimeOffset(2026, 10, 14, 10, 30, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 10, 14, 10, 35, 0, TimeSpan.Zero), true, "key_error"),
                    ]),
            ],
            SampledTotal: 3,
            UnsampledTotal: 47);
    }
}
