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
/// #203 / S-05 and AC6: the studio screen renders the three things the ticket names - the split
/// envelope, the overlap indicator and the gap badge - and each is <b>wired</b>, not merely present.
/// <para>
/// Rendered through the real host, because the defect this catches is a render one: a marker that exists
/// in the markup but has no data attribute the script reads is a badge that never moves. The assertions
/// are on the element's own identity (<c>data-state</c>, <c>data-locked</c>, <c>data-configuration</c>),
/// which is what the page's script and the stylesheet both key on.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class StimulusStudioViewTests : IClassFixture<StimulusStudioViewTests.Site>
{
    private readonly Site site;

    public StimulusStudioViewTests(Site site) => this.site = site;

    /// <summary>A B2 reading picker: four locked slots, and an envelope of B1-B2-C1.</summary>
    [Fact]
    public async Task The_screen_renders_the_split_envelope_with_the_stimulus_pinned_beside_its_children()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/stimulus?skill=reading");

        // ADM-07: the left pane is the stimulus and the right pane is the stack of children.
        Assert.Contains("studio-canvas", body);
        Assert.Contains("studio-pane--stimulus", body);
        Assert.Contains("studio-pane--children", body);

        // The two indicators are present as elements with ids the script addresses.
        Assert.Contains("id=\"studio-overlap-indicator\"", body);
        Assert.Contains("id=\"studio-overlap-value\"", body);
        Assert.Contains("id=\"studio-overlap-note\"", body);
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a badge rendered with no binding. It would
    /// pass an "is the element present" assertion and never update. The assertion here is on the data
    /// attributes the script reads: <c>data-configuration</c> carries the quotas, and
    /// <c>data-state</c> is what the stylesheet colours.
    /// </summary>
    [Fact]
    public async Task The_indicators_carry_the_state_attribute_and_the_quotas_are_shipped_to_the_page()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/stimulus?skill=reading");

        var overlap = Element(body, "studio-overlap-indicator");
        Assert.Contains("data-state", overlap);

        var gap = Element(body, "studio-gap-indicator");
        Assert.Contains("data-state", gap);

        // The quota the level control locks comes from the server, not a copy in the page.
        var configuration = Regex.Match(body, @"data-configuration=""(?<json>[^""]+)""");
        Assert.True(configuration.Success, "the page ships no configuration for the script");

        var json = WebUtility.HtmlDecode(configuration.Groups["json"].Value);
        Assert.Contains("\"locked\":4", json);
        Assert.Contains("\"allowedChildLevels\":[\"B1\",\"B2\",\"C1\"]", json);
        Assert.Contains("\"minimumAtLevel\":3", json);
        Assert.Contains("\"spanUnit\":\"char\"", json);
    }

    /// <summary>
    /// The level control is a set of placards, not a dropdown (ADM-08). <b>Which guard could this pass
    /// while being wrong:</b> rendering six <c>&lt;option&gt;</c> elements inside a select is also "six
    /// levels offered", and it is exactly the control the specification refuses.
    /// </summary>
    [Fact]
    public async Task The_level_control_is_a_group_of_placards_and_not_a_dropdown()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/stimulus?skill=reading");

        Assert.Contains("role=\"radiogroup\"", body);
        var placards = Regex.Matches(body, @"class=""studio-level""[^>]*data-level=""(?<level>[A-C][12])""");
        Assert.Equal(6, placards.Count);
        Assert.Equal(
            ["A1", "A2", "B1", "B2", "C1", "C2"],
            placards.Select(match => match.Groups["level"].Value).ToList());

        // And no <select> whose options are the six bands: the dropdown is what ADM-08 replaces.
        Assert.DoesNotMatch(@"<select[^>]*id=""studio-level""", body);
    }

    /// <summary>
    /// A listening bundle measures seconds and shows the gap badge together; a reading bundle hides it,
    /// because a character span has no adjacency in time. <b>Which guard could this pass while being
    /// wrong:</b> showing the gap badge on a reading bundle reports a gap of nothing that reads as zero
    /// seconds, which the author would try to fix.
    /// </summary>
    [Fact]
    public async Task A_listening_bundle_shows_the_gap_badge_and_a_reading_bundle_does_not()
    {
        using var client = site.CreateClientForStaff();

        var listening = await client.GetStringAsync("/admin/studio/stimulus?skill=listening");
        var reading = await client.GetStringAsync("/admin/studio/stimulus?skill=reading");

        Assert.DoesNotContain("hidden", Element(listening, "studio-gap-indicator"));
        Assert.Contains("hidden", Element(reading, "studio-gap-indicator"));

        var listeningJson = WebUtility.HtmlDecode(
            Regex.Match(listening, @"data-configuration=""(?<json>[^""]+)""").Groups["json"].Value);
        Assert.Contains("\"spanUnit\":\"sec\"", listeningJson);
    }

    /// <summary>
    /// AC5 at the screen: the save is disabled until an analysis says the bundle is acceptable, and the
    /// page says what blocks it. A save button that is enabled on load is a bundle nobody judged.
    /// </summary>
    [Fact]
    public async Task The_save_control_is_disabled_until_a_bundle_is_acceptable()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/stimulus?skill=reading");

        var save = Element(body, "studio-save");
        Assert.Contains("disabled", save);
        Assert.Contains("id=\"studio-save-status\"", body);
        Assert.Contains("id=\"studio-refusals\"", body);
    }

    /// <summary>
    /// D19 and the design system: no raw skill or level code reaches the page as a label, and no emoji
    /// or bare arrow glyph stands in for an icon. <b>Which guard could this pass while being wrong:</b>
    /// asserting only that the words appear leaves "reading" in the heading, which the registry exists
    /// to prevent.
    /// </summary>
    [Fact]
    public async Task The_heading_names_the_skill_and_the_page_carries_no_typed_glyphs()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/stimulus?skill=reading");

        Assert.Contains(">Reading<", body);
        Assert.DoesNotContain(">reading<", body);

        // No emoji or pictographic glyph. The en dash in a span is punctuation, not an icon, and the
        // design system's own rule is about marks that stand in for a word.
        Assert.DoesNotMatch(@"[\u2190-\u21FF\u2300-\u23FF\u2600-\u27BF\uD83C-\uDBFF]", body);
    }

    /// <summary>
    /// The three forwards the page's script calls are MVC endpoints with anti-forgery, never API routes
    /// (ADR-0002). <b>Which guard could this pass while being wrong:</b> a screen that posted straight
    /// to <c>/api/...</c> would work in dev and put the cookie's token in a request the browser made
    /// itself.
    /// </summary>
    [Fact]
    public void The_page_posts_to_the_mvc_forwarding_endpoints_and_carries_the_token()
    {
        var script = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "js", "admin", "stimulus-studio.js"));

        Assert.Contains("/admin/studio/stimulus/analyse", script);
        Assert.Contains("/admin/studio/stimulus", script);
        Assert.Contains("RequestVerificationToken", script);
        Assert.DoesNotContain("/api/admin/studio", script);
    }

    /// <summary>
    /// The stylesheet obeys the design system: no radius, no shadow, no gradient. <b>Which guard could
    /// this pass while being wrong:</b> a page that relies on the global reset for most elements and
    /// sets a radius on one panel is still the defect, so the scan is over the whole stylesheet.
    /// </summary>
    [Fact]
    public void The_stylesheet_sets_no_radius_no_shadow_and_no_gradient()
    {
        var css = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "css", "admin", "stimulus-studio.css"));

        // `box-shadow: none` and `border-radius: 0` are the system's own declarations and are the point;
        // a non-none value or a non-zero radius is the defect. Each is matched on its value, so the
        // declarations that enforce the rule do not fail the test that enforces the rule.
        foreach (Match match in Regex.Matches(css, @"box-shadow:\s*(?<value>[^;]+);"))
        {
            Assert.Equal("none", match.Groups["value"].Value.Trim());
        }

        foreach (Match match in Regex.Matches(css, @"border-radius:\s*(?<value>[^;]+);"))
        {
            Assert.Equal("0", match.Groups["value"].Value.Trim());
        }

        // A gradient is never a value this system uses, in any property.
        foreach (var rule in new[] { "linear-gradient", "radial-gradient", "conic-gradient", "backdrop-filter" })
        {
            Assert.DoesNotContain(rule, css);
        }
    }

    /// <summary>The opening tag of the element with the given id, so an assertion cannot match a peer.</summary>
    private static string Element(string body, string id)
    {
        var match = Regex.Match(
            body,
            $@"<(?<tag>[a-z0-9]+)(?<attrs>[^>]*\bid=""{Regex.Escape(id)}""[^>]*)>");
        Assert.True(match.Success, $"the page renders no element with id '{id}'");
        return match.Value;
    }

    /// <summary>
    /// The real WebMVC host: routing, the cookie scheme, the authorization policy and the Razor view are
    /// production code. Only the outbound API is answered, and only for the calls this page makes.
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

    /// <summary>
    /// The three upstream calls the page and the shell make, and nothing else - an unexpected call
    /// fails loudly rather than rendering an empty page that would pass.
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
                new AuthUserDto(1, "staff@test.local", "Staff", Roles.Admin, true, [Permissions.ContentAuthor])),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Staff", null, "Asia/Ho_Chi_Minh")),
            nameof(IStimulusStudioClient.GetStimulusStudioOptionsAsync) => Task.FromResult(Options((string)args![0]!)),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };

        /// <summary>
        /// The quota set the API would produce: read off the same table the service reads, so a change to
        /// D0.3's numbers fails the service test rather than being papered over here.
        /// </summary>
        private static StimulusStudioOptionsDto Options(string skill)
        {
            var reading = skill != "listening";
            var topics = reading
                ? new[]
                {
                    new StimulusStudioTopicDto(1, "R-GIST", "Global comprehension of a passage"),
                    new StimulusStudioTopicDto(2, "R-DET", "Local factual retrieval and paraphrase matching"),
                    new StimulusStudioTopicDto(3, "R-INF", "Bridging inference and logical deduction"),
                    new StimulusStudioTopicDto(4, "R-ATT", "Author stance and pragmatic intent"),
                    new StimulusStudioTopicDto(5, "R-COH", "Cohesion and text organisation"),
                    new StimulusStudioTopicDto(6, "R-GRAM", "Grammatical form in context"),
                }
                : new[]
                {
                    new StimulusStudioTopicDto(7, "L-GIST", "Main idea of a spoken text"),
                    new StimulusStudioTopicDto(8, "L-DET", "Explicit factual detail and extraction"),
                };

            var types = reading
                ? new[] { "sentence_cloze", "passage_comprehension", "text_insertion_cohesion" }
                : new[] { "short_exchange", "extended_dialogue", "monologue_talk" };

            var quotas = new List<StimulusStudioQuotaDto>();
            foreach (var level in new[] { "A1", "A2", "B1", "B2", "C1", "C2" })
            {
                foreach (var type in types)
                {
                    var (locked, allowed) = (level, type) switch
                    {
                        ("A1", _) => (2, new[] { "A1" }),
                        ("A2", "sentence_cloze") => (1, new[] { "A2" }),
                        ("A2", _) => (2, new[] { "A2" }),
                        ("B1", "sentence_cloze") => (1, new[] { "B1" }),
                        ("B1", _) => (3, new[] { "A2", "B1", "B2" }),
                        ("B2", "sentence_cloze") => (1, new[] { "B2" }),
                        ("B2", _) => (4, new[] { "B1", "B2", "C1" }),
                        ("C1", "sentence_cloze") => (1, new[] { "C1" }),
                        ("C1", _) => (4, new[] { "B2", "C1", "C2" }),
                        ("C2", "sentence_cloze") => (1, new[] { "C2" }),
                        _ => (4, new[] { "C1", "C2" }),
                    };

                    var minimumAtLevel = (locked * 2 + 2) / 3;
                    quotas.Add(new StimulusStudioQuotaDto(
                        level, type, locked, locked == 1 ? 1 : 2, locked == 1 ? 1 : 4, allowed, minimumAtLevel));
                }
            }

            return new StimulusStudioOptionsDto(
                topics,
                ["A1", "A2", "B1", "B2", "C1", "C2"],
                types,
                quotas,
                reading ? "char" : "sec");
        }
    }
}
