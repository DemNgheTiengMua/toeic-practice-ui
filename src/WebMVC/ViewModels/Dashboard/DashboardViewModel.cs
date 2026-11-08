using Contracts;

namespace WebMVC.Models;

/// <param name="Dashboard">The active certificate's activity, from the API (issue #7).</param>
/// <param name="EmailVerified">Live from the API; null when the API couldn't be reached.</param>
/// <param name="Activation">"sent" / "failed" right after a resend, otherwise null.</param>
public sealed record DashboardViewModel(
    CurrentUser User, LearnerDashboardDto Dashboard, bool? EmailVerified, string? Activation)
{
    public DashboardPresentation Presentation { get; init; } = null!;

    /// <summary>#241: block 3's table. Its column count is the certificate's skill count, so the view
    /// renders one column per examined skill rather than a fixed four.</summary>
    public SkillStandingTable Skills { get; init; } = null!;

    public bool ShowVerificationReminder => EmailVerified == false;

    /// <summary>#241 / LRN-13: nothing measured means one block - the action and the placement
    /// sentence - and no goal or per-skill figures at all.</summary>
    public bool IsUnplaced => Dashboard.Goal.HasNoMeasurement;

    /// <summary>The partials this page renders, in order. Three, or one when nothing is measured.
    /// <para>
    /// LRN-12: the list lives here, in one place, so "exactly three" is an assertion on a value rather
    /// than a count of <c>&lt;partial&gt;</c> strings in a view. The view loops it; the test counts it.
    /// </para></summary>
    public IReadOnlyList<string> Blocks => IsUnplaced ? DashboardBlocks.Unplaced : DashboardBlocks.Placed;

    /// <summary>#211: the overall floor and the skill that produced it. Null when nothing measured.</summary>
    public string? OverallLevelCode => Dashboard.Goal.OverallLevelCode;
    public string? LimitingSkillName => Dashboard.Goal.LimitingSkillName;
}
