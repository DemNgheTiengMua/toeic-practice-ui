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
/// #204 / S-07: the productive studio screen and its round-trip.
/// <para>
/// Two things are asserted here, and they are different defects.
/// </para>
/// <list type="number">
/// <item><b>The screen is wired.</b> The four elements the ticket names - the prompt pane, the part
/// stack, the placement cap and the word-range gauge - must be present <em>with the data attributes
/// the script reads</em>. A marker that exists in the markup but has no attribute the script keys on is
/// a badge that never moves.</item>
/// <item><b>A productive task round-trips into the pool.</b> The save goes through the real MVC
/// controller with the real anti-forgery pipeline, and the assertion is on the <em>request that
/// reached the API</em> - the shape the pool writer receives - rather than on a page that echoed what
/// the author typed. The last thing a screenshot-shaped test sees is the JSON it just sent.</item>
/// </list>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class ProductiveStudioViewTests : IClassFixture<ProductiveStudioViewTests.Site>
{
    private readonly Site site;

    public ProductiveStudioViewTests(Site site) => this.site = site;

    // -----------------------------------------------------------------------------------------
    // the screen
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a page with one pane. The ticket's whole
    /// shape is that the prompt and its parts are one document - a single pane is a form that could be
    /// a list of independent items, which is the shape that loses the second part.
    /// </summary>
    [Fact]
    public async Task The_screen_renders_the_prompt_pinned_beside_its_parts()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/productive?skill=writing");

        Assert.Contains("studio-canvas", body);
        Assert.Contains("studio-pane--stimulus", body);
        Assert.Contains("studio-pane--children", body);
        Assert.Contains("id=\"studio-prompt\"", body);
        Assert.Contains("id=\"studio-parts\"", body);
        Assert.Contains("id=\"studio-part-template\"", body);
    }

    /// <summary>
    /// AC4 at the screen. <b>Which guard could this pass while being wrong:</b> the cap living only in
    /// a refusal - the author types a 120-second task, presses save, and is told the limit. The element
    /// must exist on the page and carry the figure, so it is printed before the work.
    /// </summary>
    [Fact]
    public async Task The_placement_cap_is_on_the_screen_and_carries_the_figure_before_any_save()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/productive?skill=speaking");

        Assert.Contains("id=\"studio-placement-cap\"", body);
        Assert.Contains("id=\"studio-placement-cap-value\"", body);
        Assert.Contains("id=\"studio-placement\"", body);

        var configuration = Regex.Match(body, @"data-configuration=""(?<json>[^""]+)""");
        Assert.True(configuration.Success, "the page ships no configuration for the script");
        var json = WebUtility.HtmlDecode(configuration.Groups["json"].Value);
        Assert.Contains("\"placementRecordingCap\":60", json);
    }

    /// <summary>
    /// The types are D0.4's, with their windows and their visual requirement, shipped to the page so a
    /// type change is a lookup rather than a second copy of the table in JavaScript.
    /// </summary>
    [Fact]
    public async Task The_bounds_of_every_type_are_shipped_to_the_page()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/productive?skill=writing");
        var json = WebUtility.HtmlDecode(
            Regex.Match(body, @"data-configuration=""(?<json>[^""]+)""").Groups["json"].Value);

        Assert.Contains("\"code\":\"persuasive_essay\"", json);
        Assert.Contains("\"code\":\"visual_data_synthesis\"", json);
        Assert.Contains("\"visual\":\"required\"", json);
        Assert.Contains("\"minimumResponseSeconds\":840", json);
        Assert.Contains("\"minimumWords\":80", json);
    }

    /// <summary>
    /// The level control is a set of placards, not a dropdown (ADM-08). <b>Which guard could this pass
    /// while being wrong:</b> six <c>&lt;option&gt;</c> elements are also "six levels offered", and the
    /// dropdown is the control the specification refuses.
    /// </summary>
    [Fact]
    public async Task The_level_control_is_a_group_of_placards_and_not_a_dropdown()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/productive?skill=writing");

        Assert.Contains("role=\"radiogroup\"", body);
        var placards = Regex.Matches(body, @"class=""studio-level""[^>]*data-level=""(?<level>[A-C][12])""");
        Assert.Equal(6, placards.Count);
        Assert.Equal(
            ["A1", "A2", "B1", "B2", "C1", "C2"],
            placards.Select(match => match.Groups["level"].Value).ToList());

        Assert.DoesNotMatch(@"<select[^>]*id=""studio-level""", body);
    }

    /// <summary>
    /// D19 and the design system: no raw skill code as a heading, and no emoji or bare arrow glyph
    /// standing in for an icon.
    /// </summary>
    [Fact]
    public async Task The_heading_names_the_skill_and_the_page_carries_no_typed_glyphs()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/productive?skill=writing");

        Assert.Contains(">Writing<", body);
        Assert.DoesNotContain(">writing<", body);
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
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "js", "admin", "productive-studio.js"));

        Assert.Contains("/admin/studio/productive/analyse", script);
        Assert.Contains("/admin/studio/productive", script);
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
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "css", "admin", "productive-studio.css"));

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
    /// The save is disabled until an analysis says the task is acceptable, and the page says what
    /// blocks it. A save button enabled on load is a task nobody judged.
    /// </summary>
    [Fact]
    public async Task The_save_control_is_disabled_until_a_task_is_acceptable()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/studio/productive?skill=writing");

        var save = Element(body, "studio-save");
        Assert.Contains("disabled", save);
        Assert.Contains("id=\"studio-save-status\"", body);
        Assert.Contains("id=\"studio-refusals\"", body);
    }

    // -----------------------------------------------------------------------------------------
    // AC5: a productive task round-trips into the pool
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a save asserted by the page's own
    /// JavaScript, or by a controller that echoed the draft back. The assertion is on <em>what the API
    /// received</em>: the modality, the level, the topic, both parts with their own range and budget,
    /// and the placement flag. A request that dropped the second part, or sent one shared range, is the
    /// defect the ticket names - and it would still produce a cheerful "Saved" on screen.
    /// </summary>
    [Fact]
    public async Task A_two_part_writing_task_round_trips_into_the_pool_with_each_parts_own_range()
    {
        using var client = site.CreateClientForStaff();

        var request = new ProductiveTaskDraftRequest(
            ModalityDto.Writing,
            "B1",
            [
                new ProductiveSubTaskInput(
                    "transactional_correspondence", "Write the email described above.",
                    110, 150, null, 480, "The email should cover all three required actions."),
                new ProductiveSubTaskInput(
                    "persuasive_essay", "Write the essay described above.",
                    110, 300, null, 1800, "The essay should state a position and defend it."),
            ],
            "W-ARG",
            PromptCopy: "Read the notice and answer the two parts below.",
            DomainContext: "workplace");

        var response = await PostJsonAsync(client, "/admin/studio/productive", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sent = site.Upstream.LastSave;
        Assert.NotNull(sent);
        Assert.Equal(ModalityDto.Writing, sent!.Modality);
        Assert.Equal("B1", sent.Level);
        Assert.Equal("W-ARG", sent.TopicCode);
        Assert.Equal(2, sent.SubTasks.Count);

        Assert.Equal("transactional_correspondence", sent.SubTasks[0].ItemType);
        Assert.Equal((110, 150, (int?)480), (sent.SubTasks[0].MinWords, sent.SubTasks[0].MaxWords, sent.SubTasks[0].ResponseSeconds));
        Assert.Equal("persuasive_essay", sent.SubTasks[1].ItemType);
        Assert.Equal((110, 300, (int?)1800), (sent.SubTasks[1].MinWords, sent.SubTasks[1].MaxWords, sent.SubTasks[1].ResponseSeconds));

        // And the answer the screen paints from: the pool row's ids and state.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"stimulusId\"", body);
        Assert.Contains("\"parts\"", body);
    }

    /// <summary>
    /// The analysis forward carries the placement cap back to the screen, which is what makes AC4's
    /// "shown before saving" true rather than aspirational.
    /// </summary>
    [Fact]
    public async Task The_analysis_forward_returns_the_placement_cap_to_the_page()
    {
        using var client = site.CreateClientForStaff();

        var request = new ProductiveTaskDraftRequest(
            ModalityDto.Speaking,
            "B1",
            [
                new ProductiveSubTaskInput(
                    "argumentative_monologue", "Argue for or against the proposition.",
                    null, null, 60, 60, "The answer should defend one side with reasons."),
            ],
            "S-ARG",
            PromptCopy: "Read the proposition and answer.",
            PlacementEligible: true);

        var response = await PostJsonAsync(client, "/admin/studio/productive/analyse", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(60, document.RootElement.GetProperty("placementRecordingCap").GetInt32());
    }

    /// <summary>
    /// A refusal from the API reaches the page with its status and sentence, so the screen can say why
    /// rather than showing a bare failure. This is the same contract every staff controller has.
    /// </summary>
    [Fact]
    public async Task The_save_forward_surfaces_the_api_refusal_with_its_status()
    {
        using var client = site.CreateClientForStaff();
        site.Upstream.RefuseNextSave = true;

        var request = new ProductiveTaskDraftRequest(
            ModalityDto.Writing,
            "B1",
            [new ProductiveSubTaskInput("persuasive_essay", "Write the essay.", 110, 300, null, 900, "Defend a position.")],
            "W-ARG",
            PromptCopy: "Read the question.");

        var response = await PostJsonAsync(client, "/admin/studio/productive", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nothing was written", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ADR-0002 and §1.2: the browser never reaches the API, and every AJAX forward carries the
    /// anti-forgery token. The bearer is not passed by the controller - <c>CookieBearerHandler</c>
    /// reads it from the HttpOnly cookie in the HTTP pipeline, so a controller that read it too would
    /// be a second answer to "who is calling".
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> asserting only that the route renders.
    /// An action with the policy and no <c>[ValidateAntiForgeryToken]</c> accepts a cross-site POST,
    /// and an action with the token and no policy accepts any signed-in learner.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_forward_carries_the_anti_forgery_token_and_the_authoring_capability()
    {
        var controller = typeof(WebMVC.Controllers.AdminProductiveStudioController);

        var policy = controller.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(true)
            .Select(attribute => attribute.Policy)
            .ToList();
        Assert.Contains(Permissions.ContentAuthor, policy);

        foreach (var name in new[] { nameof(WebMVC.Controllers.AdminProductiveStudioController.Analyse),
                                     nameof(WebMVC.Controllers.AdminProductiveStudioController.Save) })
        {
            var action = controller.GetMethod(name);
            Assert.NotNull(action);
            Assert.NotEmpty(action!.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>(true));
        }
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        // The anti-forgery token the view renders: the controller refuses a form-less POST without it,
        // and a test that skipped it would prove the controller's policy is not wired.
        var page = await client.GetStringAsync("/admin/studio/productive?skill=writing");
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

    private static string Element(string body, string id)
    {
        var match = Regex.Match(
            body,
            $@"<(?<tag>[a-z0-9]+)(?<attrs>[^>]*\bid=""{Regex.Escape(id)}""[^>]*)>");
        Assert.True(match.Success, $"the page renders no element with id '{id}'");
        return match.Value;
    }

    /// <summary>
    /// The real WebMVC host: routing, the cookie scheme, the authorization policy, the anti-forgery
    /// middleware and the Razor view are production code. Only the outbound API is answered, and only
    /// for the calls this page makes.
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
    /// The upstream calls the page and the shell make, and nothing else - an unexpected call fails
    /// loudly rather than rendering an empty page that would pass. It records what the forwarding
    /// controller <em>sent</em>, which is what the round-trip assertion reads.
    /// </summary>
    public class Upstream : DispatchProxy
    {
        public ICompanionApiClient Client { get; private set; } = null!;

        public ProductiveTaskDraftRequest? LastSave { get; private set; }

        public bool RefuseNextSave { get; set; }

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

            nameof(IProductiveStudioClient.GetProductiveStudioOptionsAsync) => Task.FromResult(
                Options((string)args![0]!)),

            nameof(IProductiveStudioClient.AnalyseProductiveTaskAsync) => Task.FromResult(Analyse(args!)),

            nameof(IProductiveStudioClient.SaveProductiveTaskAsync) => Save(args!),

            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };

        private ProductiveTaskAnalysisDto Analyse(object?[] args)
        {
            var request = (ProductiveTaskDraftRequest)args[0]!;
            var placement = request.PlacementEligible;
            return new ProductiveTaskAnalysisDto(
                [],
                [],
                placement && request.Modality == ModalityDto.Speaking ? 60 : null,
                [], []);
        }

        private Task<ProductiveTaskBundleDto> Save(object?[] args)
        {
            var request = (ProductiveTaskDraftRequest)args[0]!;
            if (RefuseNextSave)
            {
                RefuseNextSave = false;
                throw new ApiException(
                    ApiError.Codes.Validation, "Refused", 400,
                    "The task was refused, so nothing was written: [studio.productive.words] sub-task 1.");
            }

            LastSave = request;

            // The pool row's shape: one part per sub-task, both draft and demo.
            var parts = request.SubTasks
                .Select((subTask, index) => new PoolItemDto(
                    100 + index, 200 + index, 1, 1, request.Modality, 1, request.TopicCode,
                    request.Level, subTask.ItemType, "demo", "draft", request.PlacementEligible,
                    900, subTask.Prompt, null, null, null, null, null, subTask.Explanation,
                    null, null, null, null, null, null, DateTimeOffset.UnixEpoch))
                .ToList();

            return Task.FromResult(new ProductiveTaskBundleDto(900, request.Level, parts));
        }

        /// <summary>
        /// The options the API would produce: read off D0.4's table through the same envelope the
        /// service uses, so a change to the numbers fails the service test rather than being papered
        /// over here.
        /// </summary>
        private static ProductiveStudioOptionsDto Options(string skill)
        {
            var writing = skill != "speaking";

            var topics = writing
                ? new[]
                {
                    new ProductiveStudioTopicDto(17, "W-CORR", "Transactional correspondence and register control"),
                    new ProductiveStudioTopicDto(18, "W-DATA", "Visual data, process, and informational synthesis"),
                    new ProductiveStudioTopicDto(19, "W-ARG", "Thesis-driven persuasive and opinion argumentation"),
                    new ProductiveStudioTopicDto(20, "W-DISC", "Multi-perspective problem-solution and evaluation"),
                }
                : new[]
                {
                    new ProductiveStudioTopicDto(13, "S-DESC", "Informational description and personal framing"),
                    new ProductiveStudioTopicDto(14, "S-INT", "Transactional interaction and option negotiation"),
                    new ProductiveStudioTopicDto(15, "S-ARG", "Sustained monologic argumentation and justification"),
                    new ProductiveStudioTopicDto(16, "S-SYN", "Integrated summary and visual/data interpretation"),
                };

            var types = (writing
                    ? new[]
                    {
                        new ProductiveTypeOptionDto("transactional_correspondence", "none", null, null, 480, 900, 50, 150),
                        new ProductiveTypeOptionDto("visual_data_synthesis", "required", null, null, 600, 1200, 60, 180),
                        new ProductiveTypeOptionDto("persuasive_essay", "none", null, null, 840, 1800, 80, 300),
                        new ProductiveTypeOptionDto("analytical_evaluation_essay", "none", null, null, 840, 1800, 80, 320),
                    }
                    : new[]
                    {
                        new ProductiveTypeOptionDto("descriptive_monologue", "optional", 30, 45, 45, 60, null, null),
                        new ProductiveTypeOptionDto("situational_negotiation", "none", 45, 60, 60, 90, null, null),
                        new ProductiveTypeOptionDto("argumentative_monologue", "none", 60, 60, 90, 120, null, null),
                        new ProductiveTypeOptionDto("visual_data_synthesis", "required", 45, 45, 60, 90, null, null),
                    })
                .ToList();

            return new ProductiveStudioOptionsDto(
                topics,
                ["A1", "A2", "B1", "B2", "C1", "C2"],
                types);
        }
    }
}
