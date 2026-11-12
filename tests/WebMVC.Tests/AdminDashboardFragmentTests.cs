using System.Text.RegularExpressions;
using Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WebMVC.Tests;

/// <summary>
/// #286: <b>the fragment route, asserted on the bytes the script puts into the page.</b>
/// <para>
/// <b>Why a real host and not a controller call.</b> "Returns the metric region and not a full page" is a
/// claim about a response: no layout, no <c>head</c>, no script tags. A controller unit test can only see
/// that a <c>PartialViewResult</c> was named; the Razor pipeline is what decides what that becomes. The
/// negative-amount case is the same argument one step further - the figure must arrive already formatted,
/// because the point of the design is that no script formats a number.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class AdminDashboardFragmentTests
{
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
    /// The metric region and nothing else. A full page assigned into the region would nest a dashboard
    /// inside the dashboard and run every layout script a second time.
    /// </summary>
    [Fact]
    public async Task The_fragment_returns_the_metric_region_and_not_a_page()
    {
        using var site = new AdminDashboardRenderingTests.Site(Dashboard(100000m, 900000m));
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/dashboard/fragment");

        // The figures are there, so this is the region and not an empty success.
        Assert.Contains("Net revenue (today)", body);
        Assert.Contains("Most attempted exams", body);
        Assert.Contains("Needs attention", body);

        // And no page furniture: no doctype, no head, no layout shell, no script.
        Assert.DoesNotContain("<!DOCTYPE", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<head", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("app-sidebar", body);
        Assert.DoesNotMatch(new Regex(@"<script\b", RegexOptions.IgnoreCase), body);

        // The freshness stamp travels with the figures it describes.
        Assert.Contains("data-dashboard-updated", body);
    }

    /// <summary>
    /// The negative branch, on the fragment specifically: this is the path the live update takes, and a
    /// fragment that clamped a loss to zero would make a bad month invisible exactly when the dashboard is
    /// being watched. Razor escapes the character, so U+2212 arrives as a numeric reference - which is also
    /// what proves it is not the hyphen the default formatting would emit.
    /// </summary>
    [Fact]
    public async Task A_negative_amount_renders_negative_in_the_fragment()
    {
        using var site = new AdminDashboardRenderingTests.Site(Dashboard(-250000m, -1800000m));
        using var client = site.CreateClientForStaff();

        var body = await client.GetStringAsync("/admin/dashboard/fragment");

        // Scoped to each figure rather than to the page: a page-wide "text-danger" lookup was satisfied by
        // the sibling figure when this test was written, so a mutation that dropped the class from one
        // figure passed. Each figure is asked about its own block.
        Assert.Contains("&#x2212;250,000", Figure(body, "Net revenue (today)"));
        Assert.Contains("text-danger", Figure(body, "Net revenue (today)"));

        Assert.Contains("&#x2212;1,800,000", Figure(body, "Net revenue (this month)"));
        Assert.Contains("text-danger", Figure(body, "Net revenue (this month)"));

        // A zero or an unsigned figure would read as a healthy day; the sign assertion above is what proves
        // it is not, since a hyphen or a missing minus would fail the U+2212 lookup.
    }

    /// <summary>
    /// The block whose label is <paramref name="label"/>, ending at the hint that follows its value - so a
    /// class or a sign is attributed to the figure it belongs to and not to a neighbour.
    /// </summary>
    private static string Figure(string body, string label)
    {
        var marker = $"<div class=\"stat__label\">{label}</div>";
        var at = body.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the fragment renders no '{label}' figure");

        var hint = body.IndexOf("stat__hint", at + marker.Length, StringComparison.Ordinal);
        Assert.True(hint > at, $"the '{label}' figure has no hint");
        return body[at..body.IndexOf("</div>", hint, StringComparison.Ordinal)];
    }

    /// <summary>
    /// The fragment is on the admin controller, so it inherits the dashboard's policy. An anonymous caller
    /// must be refused before any figure is rendered - this is the same class of leak as an unauthenticated
    /// stream, and it is the cheapest one to close.
    /// </summary>
    [Fact]
    public async Task The_fragment_is_refused_to_an_unauthenticated_caller()
    {
        using var site = new AdminDashboardRenderingTests.Site(Dashboard(100000m, 900000m));
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/admin/dashboard/fragment");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("Net revenue", await response.Content.ReadAsStringAsync());
    }
}
