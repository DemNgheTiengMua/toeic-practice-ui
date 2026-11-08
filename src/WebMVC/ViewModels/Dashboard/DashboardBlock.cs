namespace WebMVC.Models;

/// <summary>
/// #241 / LRN-11, LRN-12: the dashboard's block list. Each entry is the partial that renders one
/// block, so the view is a single loop and a fourth block is an entry in this list rather than a
/// partial someone remembered to place.
/// <para>
/// There is deliberately no fourth entry. LRN-13 collapses the list to one - the action and the
/// placement sentence - because an unplaced learner has no goal figures and no per-skill columns to
/// show, and a grid of empty numbers is how a product looks broken at the one moment they have least
/// reason to trust it.
/// </para>
/// </summary>
public static class DashboardBlocks
{
    /// <summary>Block 1: the next action, the cascade step that chose it, and this week's execution.</summary>
    public const string NextAction = "_DashboardHero";

    /// <summary>Block 2: goal standing and feasibility - four tabular cells, no progress bar.</summary>
    public const string GoalStanding = "_DashboardGoal";

    /// <summary>Block 3: per-skill standing - one column per examined skill.</summary>
    public const string PerSkillStanding = "_DashboardSkills";

    /// <summary>LRN-11: exactly three, in D21a's order.</summary>
    public static IReadOnlyList<string> Placed { get; } = [NextAction, GoalStanding, PerSkillStanding];

    /// <summary>LRN-13: one block, one action, and the sentence about placement.</summary>
    public static IReadOnlyList<string> Unplaced { get; } = [NextAction];
}
