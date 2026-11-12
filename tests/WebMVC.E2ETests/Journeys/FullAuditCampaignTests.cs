using Microsoft.Playwright;
using NUnit.Framework;
using WebMVC.E2ETests.Support;

namespace WebMVC.E2ETests.Journeys;

/// <summary>
/// The full audit campaign: every role, every screen, one screenshot each, sorted into
/// <c>docs/audit/screenshots/full-audit/&lt;role&gt;/&lt;flow&gt;/</c>.
/// <para>
/// <b>What this proves and what it does not.</b> It proves a screen can be reached and drawn with the
/// seeded data, and it records what that looked like so a human can audit it later. It does not assert
/// pixels; the content assertions live in the journeys beside it, and a capture suite that also asserted
/// would be two tests disagreeing about one fact. The single assertion here is money, because an
/// abbreviated amount is not a rendering preference — it is a figure that cannot equal the database.
/// </para>
/// <para>
/// <b>Why every role.</b> The rebuild created a learner surface, a staff surface and a mentor surface, and
/// the permissions are composable rather than role-named. A defect that only appears when an admin holds
/// two namespaces, or when a learner has a mentor profile, is invisible to a campaign that signs in as one
/// account.
/// </para>
/// <para>
/// Run with <c>E2E_BASE_URL=http://localhost:5000</c> against a fresh
/// <c>scripts/seed-manual-test.sh fresh</c>.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class FullAuditCampaignTests : PlaywrightTestBase
{
    private const string Campaign = "full-audit";

    /// <summary>Every GET screen the admin surface has. A route that opens a sitting is captured
    /// elsewhere, because it mutates seeded state.</summary>
    private static readonly string[] AdminRoutes =
    [
        "/admin",
        "/admin/certificates",
        "/admin/certificates/wizard",
        "/admin/exams",
        "/admin/exams/create",
        "/admin/exams/1/edit",
        "/admin/exams/1/preview",
        "/admin/exams/1/studio",
        "/admin/exams/1/assembly",
        "/admin/exams/1/audit",
        "/admin/coverage",
        "/admin/ingestion",
        "/admin/studio/stimulus",
        "/admin/studio/productive",
        "/admin/studio/rubric",
        "/admin/lessons",
        "/admin/lessons/create",
        "/admin/lessons/1/edit",
        "/admin/lessons/1/preview",
        "/admin/mentors",
        "/admin/mentors/create",
        "/admin/mentors/1/edit",
        "/admin/mentors/1/slots",
        "/admin/users",
        "/admin/packages",
        "/admin/packages/create",
        "/admin/packages/edit/1",
        "/admin/orders",
        "/admin/orders/1/detail",
        "/admin/complaints",
        "/admin/complaints/1",
        "/admin/refunds",
        "/admin/audit",
    ];

    private static readonly string[] LearnerRoutes =
    [
        "/",
        "/certificates",
        "/dashboard",
        "/learning-path",
        "/exams",
        "/exams/history",
        "/practice",
        "/writing",
        "/speaking",
        "/placement",
        "/placement/target-band",
        "/weakness-analysis",
        "/saved-questions",
        "/mentors",
        "/mentors/bookings",
        "/wallet",
        "/pricing",
        "/checkout",
        "/support",
        "/support/report",
        "/support/history",
        "/profile",
        "/profile/password",
    ];

    private static readonly string[] MentorRoutes =
    [
        "/Dashboard",
        "/mentors/1",
        "/mentors/bookings",
    ];

    [Test]
    public async Task Admin_every_screen()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");
        var shots = new ScreenshotCapture(Page, Campaign, "admin");

        foreach (var route in AdminRoutes)
        {
            var flow = FlowFor(route);
            // The route is the flow so two similar screens cannot overwrite each other's image.
            await shots.VisitAsync($"{BaseUrl}{route}", flow, Slug(route));

            // Money only appears on a handful of screens, but the guard is cheap and a wrong figure is
            // the one thing this campaign must not photograph silently.
            if (route is "/admin" or "/admin/packages" or "/admin/orders" or "/admin/refunds")
            {
                await shots.AssertNoAbbreviatedMoneyAsync(route);
            }
        }

        Assert.That(shots.ConsoleErrors, Is.Empty,
            "Console errors while walking the admin surface: " + string.Join(" | ", shots.ConsoleErrors));
    }

    [Test]
    public async Task Learner_with_history_every_screen()
    {
        await SignInAsync("learner@demo.local", "Demo1234!");
        var shots = new ScreenshotCapture(Page, Campaign, "learner-history");

        foreach (var route in LearnerRoutes)
        {
            await shots.VisitAsync($"{BaseUrl}{route}", FlowFor(route), Slug(route));

            if (route is "/wallet" or "/checkout" or "/pricing")
            {
                await shots.AssertNoAbbreviatedMoneyAsync(route);
            }
        }

        Assert.That(shots.ConsoleErrors, Is.Empty,
            "Console errors while walking the learner surface: " + string.Join(" | ", shots.ConsoleErrors));
    }

    [Test]
    public async Task Learner_clean_slate_empty_states()
    {
        await SignInAsync("learner.fresh@demo.local", "Demo1234!");
        var shots = new ScreenshotCapture(Page, Campaign, "learner-fresh");

        // The empty state is a screen too, and the one most likely to regress unseen.
        foreach (var route in LearnerRoutes)
        {
            await shots.VisitAsync($"{BaseUrl}{route}", FlowFor(route), Slug(route));

            if (route is "/wallet" or "/checkout" or "/pricing")
            {
                await shots.AssertNoAbbreviatedMoneyAsync(route);
            }
        }
    }

    [Test]
    public async Task Mentor_profile_holder_every_screen()
    {
        await SignInAsync("mentor.khoa@demo.local", "Demo1234!");
        var shots = new ScreenshotCapture(Page, Campaign, "mentor");

        foreach (var route in MentorRoutes)
        {
            await shots.VisitAsync($"{BaseUrl}{route}", FlowFor(route), Slug(route));
        }
    }

    [Test]
    public async Task Signed_out_public_surface()
    {
        var shots = new ScreenshotCapture(Page, Campaign, "signed-out");

        foreach (var route in new[] { "/", "/certificates", "/pricing", "/auth/sign-in", "/auth/register", "/auth/forgot-password" })
        {
            await shots.VisitAsync($"{BaseUrl}{route}", "public", Slug(route));
        }
    }

    [Test]
    public async Task Admin_is_refused_a_learner_screen_and_the_refusal_is_captured()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");
        var shots = new ScreenshotCapture(Page, Campaign, "permission");

        // A refusal that reads like a server fault is a defect; the image is what lets a human judge it.
        await shots.VisitAsync($"{BaseUrl}/practice", "admin-on-learner-route", "practice-refused");
        await shots.VisitAsync($"{BaseUrl}/wallet", "admin-on-learner-route", "wallet-refused");
    }

    [Test]
    public async Task Learner_is_refused_an_admin_screen_and_the_refusal_is_captured()
    {
        await SignInAsync("learner@demo.local", "Demo1234!");
        var shots = new ScreenshotCapture(Page, Campaign, "permission");

        await shots.VisitAsync($"{BaseUrl}/admin", "learner-on-admin-route", "admin-refused");
        await shots.VisitAsync($"{BaseUrl}/admin/users", "learner-on-admin-route", "users-refused");
    }

    /// <summary>Groups images by the area a reviewer thinks in, not by URL shape.</summary>
    private static string FlowFor(string route)
    {
        if (route.StartsWith("/admin/exams") || route.StartsWith("/admin/studio"))
        {
            return "exams-and-studio";
        }

        if (route.StartsWith("/admin/certificates") || route == "/admin/coverage")
        {
            return "curriculum";
        }

        if (route.StartsWith("/admin/users") || route.StartsWith("/admin/orders")
            || route.StartsWith("/admin/packages") || route.StartsWith("/admin/complaints")
            || route.StartsWith("/admin/refunds") || route.StartsWith("/admin/mentors"))
        {
            return "operations";
        }

        if (route.StartsWith("/admin"))
        {
            return "administration";
        }

        if (route.StartsWith("/exams") || route.StartsWith("/placement"))
        {
            return "sitting-and-results";
        }

        if (route.StartsWith("/practice") || route.StartsWith("/learning-path") || route.StartsWith("/weakness")
            || route.StartsWith("/writing") || route.StartsWith("/speaking"))
        {
            return "study";
        }

        if (route.StartsWith("/mentors") || route == "/wallet" || route == "/pricing" || route == "/checkout")
        {
            return "money-and-mentors";
        }

        if (route.StartsWith("/support") || route.StartsWith("/profile"))
        {
            return "account";
        }

        if (route is "/" or "/certificates" or "/dashboard")
        {
            return "home";
        }

        if (route.StartsWith("/auth"))
        {
            return "public";
        }

        return "other";
    }

    /// <summary>Turns a route into a filename a human can read and sort.</summary>
    private static string Slug(string route) =>
        route == "/"
            ? "01-root"
            : route.Trim('/').Replace('/', '-');
}
