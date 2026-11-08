namespace WebMVC.Models;

/// <summary>
/// #238 / SCR-PRAC-01: one plate of the objective practice matrix.
/// <para>
/// <b>State</b> is the four-state mastery the service produced, carried as its own string so the view
/// renders what it was given rather than re-deriving a bool. <b>ReopensDisplay</b> is the instant in the
/// learner's own zone, already formatted by the controller — the view prints a string, it never converts.
/// </para>
/// <para>
/// The design system's inversion rule applies to selection, not rest: a resting cell stays painted on the
/// wall and is marked with an attribute, not merely faded, so its reopen time stays readable and the
/// state is announced rather than carried by colour alone.
/// </para>
/// </summary>
public sealed record PracticeMatrixRowViewModel(
    string TopicCode,
    string TopicName,
    string LevelLabel,
    string MasteryState,
    string MasteryLabel,
    bool IsPrioritised,
    bool IsResting,
    string? ReopensDisplay)
{
    /// <summary>
    /// The one place a state code becomes the words on the plate. A cell's state is a fact about the
    /// learner's work, so the label names it plainly and attaches the next step.
    /// </summary>
    public static string LabelFor(string state) => state switch
    {
        "Mastered" => "Mastered",
        "Reviewing" => "Reviewing",
        "Learning" => "Learning",
        _ => "Untested",
    };

    /// <summary>The class the plate carries. Resting is a distinct attribute, never a tint of mastery.</summary>
    public string CssClass =>
        "matrix__cell" + (IsPrioritised ? " is-prioritised" : "") + (IsResting ? " is-resting" : "");

    /// <summary>
    /// The accessible sentence for one plate. The dimmed state is announced here, so a learner using a
    /// screen reader is told the cell is resting and when it returns — not just shown a paler rectangle.
    /// </summary>
    public string AccessibleLabel =>
        $"{TopicCode} {LevelLabel}, {MasteryLabel}"
        + (IsPrioritised ? ", recommended this week" : "")
        + (IsResting && ReopensDisplay is { } reopens ? $", resting, reopens {reopens}" : "");

    /// <summary>The line under the plate: when it reopens, or that it is open now.</summary>
    public string StatusLine =>
        IsResting && ReopensDisplay is { } reopens ? $"Reopens {reopens}" : "Open now";
}
