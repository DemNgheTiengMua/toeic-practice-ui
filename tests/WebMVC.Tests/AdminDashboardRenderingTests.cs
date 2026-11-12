using System.Reflection;
using System.Security.Claims;
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
/// #284: the dashboard's money readouts, asserted on the HTML a staff member receives.
/// <para>
/// <b>Why the real Razor pipeline and not a source grep.</b> The three claims this ticket makes about the
/// screen are all rendering claims: the figure is negative, the minus is U+2212 and not a hyphen, and the
/// value carries the refusal colour. A view-model test proves the class was <em>chosen</em>; only the
/// rendered page proves the markup used it.
/// </para>
/// <para>
/// <b>Why the minus is asserted as <c>&amp;#x2212;</c>.</b> Razor escapes non-ASCII on the way out, so the
/// page carries a numeric character reference and the browser paints the real glyph. The reference is the
/// evidence that the character is U+2212 and not a hyphen: a formatter that emitted <c>-</c> would render
/// <c>-</c> here, not an escape.
/// </para>
/// <para>
/// The dashboard is stubbed at the API client because the figures are what this file is about; the
/// arithmetic is proved against a real database in <c>Services.Tests</c>.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class AdminDashboardRenderingTests
{
    /// <summary>A loss on the day and a profit on the month, so one page proves both renderings.</summary>
    private static AdminDashboardDto Mixed() => Dashboard(dayVnd: -250000m, monthVnd: 1800000m);

    private static AdminDashboardDto Dashboard(decimal dayVnd, decimal monthVnd) => new(
        ActiveLearnersCount: 1284,
        NewLearners7DaysCount: 47,
        CreditsUsedThisMonth: 3912,
        CreditsRefundedThisMonth: 18,
        CreditsPerDayAverage: 130.4m,
        RevenueTodayVnd: dayVnd,
        RevenueThisMonthVnd: monthVnd,
        PaidOrdersThisMonthCount: 328,
        MostAttemptedExams: [new(1, "ETS 2024 — Test 1", 612, 705m)],
        NeedsAttention: new AdminNeedsAttentionDto(0, 0, 0));

    /// <summary>
    /// A loss is lettered the way the design system letters a refusal: true minus sign, Sign Red. The exact
    /// U+2212 is asserted because the hyphen the default number formatting would produce is the defect
    /// #284 names.
    /// </summary>
    [Fact]
    public async Task A_negative_revenue_renders_in_sign_red_with_a_true_minus_sign()
    {
        using var site = new Site(Mixed());
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin");
        var figure = Figure(body, "Net revenue (today)");

        Assert.Contains("&#x2212;250,000", figure);
        Assert.Contains("text-danger", figure);

        // A hyphen or an en dash in front of the figure would read as a dash at text size.
        Assert.DoesNotContain(">-250,000", figure);
        Assert.DoesNotContain(">&#x2013;250,000", figure);
    }

    /// <summary>
    /// The other half: a figure that earned money must not be painted as a refusal. A screen that coloured
    /// every revenue figure red would pass the test above.
    /// </summary>
    [Fact]
    public async Task A_positive_revenue_is_not_painted_as_a_refusal()
    {
        using var site = new Site(Mixed());
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin");
        var figure = Figure(body, "Net revenue (this month)");

        Assert.Contains("1,800,000", figure);
        Assert.DoesNotContain("text-danger", figure);
        Assert.DoesNotContain("&#x2212;", figure);
    }

    /// <summary>Both money labels say the figure is net, which is the change of meaning this ticket makes.</summary>
    [Fact]
    public async Task Both_money_labels_say_net_so_the_figure_is_not_read_as_gross()
    {
        using var site = new Site(Mixed());
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin");

        Assert.Contains("Net revenue (today)", body);
        Assert.Contains("Net revenue (this month)", body);
        Assert.DoesNotContain("Revenue this month", body);

        // And the credits figure is its own readout, so a credit make-good cannot be mistaken for money.
        Assert.Contains("Credits refunded (this month)", body);
    }

    /// <summary>
    /// The stat block whose label is <paramref name="label"/>, found by its own marker so a reordered grid
    /// fails here instead of silently reading the neighbouring cell. Regex rather than a DOM because there
    /// is no HTML parser in this project.
    /// </summary>
    private static string Figure(string body, string label)
    {
        var marker = $"<div class=\"stat__label\">{label}</div>";
        var at = body.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the page renders no '{label}' figure");

        // The value div starts right after the label; the block ends at the hint div that follows it.
        var value = body.IndexOf("stat__", at + marker.Length, StringComparison.Ordinal);
        var hint = body.IndexOf("stat__hint", at + marker.Length, StringComparison.Ordinal);
        Assert.True(value > at && hint > value, $"the '{label}' figure has no value or hint");

        return body[at..body.IndexOf("</div>", hint, StringComparison.Ordinal)];
    }

    /// <summary>
    /// The real WebMVC host: routing, the cookie scheme, the authorization policy and the Razor view are
    /// all production code. Only the outbound API is answered, and only for the calls this page makes - an
    /// unexpected one fails loudly rather than rendering an empty page that would pass.
    /// </summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        private readonly AdminDashboardDto dashboard;
        private readonly Upstream upstream;

        public Site(AdminDashboardDto dashboard)
        {
            this.dashboard = dashboard;
            upstream = Upstream.Create(dashboard);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ICompanionApiClient>(upstream.Client));
        }

        public HttpClient CreateClientForStaff()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                AuthClaims.For(1, "staff@test.local", Roles.Admin, "Staff", [Permissions.SystemAdmin]),
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

    /// <summary>The upstream calls the page and the shell make, and nothing else.</summary>
    public class Upstream : DispatchProxy
    {
        public ICompanionApiClient Client { get; private set; } = null!;

        private AdminDashboardDto dashboard = null!;

        public static Upstream Create(AdminDashboardDto dashboard)
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, Upstream>();
            var stub = (Upstream)(object)proxy;
            stub.Client = proxy;
            stub.dashboard = dashboard;
            return stub;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IAuthClient.GetMeAsync) => Task.FromResult(
                new AuthUserDto(1, "staff@test.local", "Staff", Roles.Admin, true, [Permissions.SystemAdmin])),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Staff", null, "Asia/Ho_Chi_Minh")),
            nameof(IAdminDashboardClient.GetAdminDashboardAsync) => Task.FromResult(dashboard),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };
    }
}
