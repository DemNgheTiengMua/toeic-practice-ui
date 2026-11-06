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
/// #202 / S-01: the coverage matrix renders a cell that holds nothing as a <b>deficit</b>, not as a
/// blank.
/// <para>
/// Rendered through the real host rather than read out of the markup, because the defect this catches
/// is a rendering one: a view built by grouping over items simply has no element for an empty cell, and
/// the page then shows a gap that reads as "nothing to report here" — the exact failure the screen
/// exists to prevent. A source-level assertion could not tell an empty cell that renders from an empty
/// cell that vanished.
/// </para>
/// <para>
/// The matrix used here is the honest one a fresh warehouse produces: every cell at zero. One cell is
/// given stock so the test can also prove a filled cell is <em>not</em> painted as a deficit — a view
/// that printed "5 short" on every plaque would otherwise pass.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class AdminCoverageViewTests : IClassFixture<AdminCoverageViewTests.Site>
{
    private readonly Site site;

    public AdminCoverageViewTests(Site site) => this.site = site;

    /// <summary>Three cells in one topic: A2 holds nothing, B1 is at budget, C1 is the odd figure.</summary>
    private static AdminCoverageMatrixDto Matrix() => new(
    [
        new AdminCoverageCellDto(2, "reading", "R-DET", "Local factual retrieval", "A2", 30, 0, 0, 30),
        new AdminCoverageCellDto(2, "reading", "R-DET", "Local factual retrieval", "B1", 30, 30, 10, 0),
        // 2 active against a target of 9 with a deficit of 9: not 30 - 2, so a recompute shows up.
        new AdminCoverageCellDto(2, "reading", "R-DET", "Local factual retrieval", "C1", 9, 2, 1, 9),
    ]);

    [Fact]
    public async Task A_cell_holding_nothing_renders_its_deficit_instead_of_a_blank()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/coverage");

        // The plaque exists, is painted as empty, and says both the figure and the noun.
        var cell = CellFor(body, "R-DET", "A2");
        Assert.Contains("coverage-cell--empty", cell);
        Assert.Contains("0<span class=\"coverage-cell__of\">/</span>30", cell);
        Assert.Contains("30 short", cell);

        // And a blank is genuinely absent: the empty cell is not rendered as an empty <td>.
        Assert.DoesNotContain("coverage-cell \"></td>", body);
    }

    [Fact]
    public async Task A_cell_at_budget_is_not_painted_as_a_deficit()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/coverage");

        var filled = CellFor(body, "R-DET", "B1");
        Assert.Contains("coverage-cell--met", filled);
        Assert.Contains("0 short", filled);
        Assert.DoesNotContain("30 short", filled);
    }

    /// <summary>
    /// Criterion 3 belongs to the view layer too: the screen shows the number the view produced and
    /// never re-derives one. The fixture's deficit is deliberately <b>not</b> the arithmetic a
    /// controller would compute — 30 − 2 is 28, the payload says 9 — so a controller or a Razor
    /// expression that subtracted targets from counts renders 28 and fails here. The number is wrong
    /// on purpose; that is the only way a recompute is detectable at all.
    /// </summary>
    [Fact]
    public async Task Every_figure_on_the_screen_is_the_one_the_view_reported()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/coverage");

        var cell = CellFor(body, "R-DET", "C1");
        Assert.Contains("2<span class=\"coverage-cell__of\">/</span>9", cell);
        Assert.Contains("9 short", cell);
        Assert.Contains("1 eligible", cell);
        Assert.DoesNotContain("28 short", cell);
    }

    /// <summary>D19: no raw code reaches the page. The level and the skill are the registries' text.</summary>
    [Fact]
    public async Task The_screen_names_its_skill_and_levels_in_words()
    {
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/coverage");

        Assert.Contains(">Reading<", body);
        Assert.Contains(">A2<", body);
        Assert.DoesNotContain(">reading<", body);
        Assert.DoesNotContain(">listening<", body);
    }

    /// <summary>
    /// The cell's whole markup, found by the level header it sits under. Regex rather than a DOM
    /// because there is no HTML parser in this project; the header order is read out of the page
    /// itself, so a reordered matrix fails here instead of silently checking the wrong cell.
    /// </summary>
    private static string CellFor(string body, string topicCode, string level)
    {
        Assert.Contains(topicCode, body);

        var headers = Regex.Matches(body, @"<th scope=""col"" class=""coverage-grid__level font-mono"">(?<level>[^<]+)</th>")
            .Select(m => m.Groups["level"].Value.Trim())
            .ToList();
        var index = headers.IndexOf(level);
        Assert.True(index >= 0, $"the page renders no '{level}' column; headers were {string.Join(", ", headers)}");

        var cells = Regex.Matches(body, @"<td class=""coverage-cell[^""]*""[^>]*>(?<body>[\s\S]*?)</td>")
            .Select(m => m.Value)
            .ToList();
        Assert.Equal(headers.Count, cells.Count);

        return cells[index];
    }

    /// <summary>
    /// The real WebMVC host: routing, the cookie scheme, the authorization policy and the Razor view
    /// are all production code. Only the outbound API is answered, and only for the calls this page
    /// makes — an unexpected one fails loudly rather than rendering an empty page that would pass.
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
            nameof(IAdminCoverageClient.GetAdminCoverageAsync) => Task.FromResult(Matrix()),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };

        private static AdminCoverageMatrixDto Matrix() => new(
        [
            new AdminCoverageCellDto(2, "reading", "R-DET", "Local factual retrieval", "A2", 30, 0, 0, 30),
            new AdminCoverageCellDto(2, "reading", "R-DET", "Local factual retrieval", "B1", 30, 30, 10, 0),
            new AdminCoverageCellDto(2, "reading", "R-DET", "Local factual retrieval", "C1", 9, 2, 1, 9),
        ]);
    }
}
