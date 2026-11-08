using Contracts;
using Microsoft.Extensions.Logging;

namespace WebMVC.Models;

/// <summary>
/// #218 / LRN-16: one column of the horizon, printed from the certificate's own band table.
/// <para>
/// <b>The axis is the certificate's bands, never the six CEFR levels.</b> TOEIC's 785-940 is labelled
/// "B2" internally and is not CEFR B2 (PRN-03), so a fixed A1-C2 axis would relabel every certificate
/// that does not use the ladder. <see cref="Code"/> is the certificate's code and <see cref="Name"/> is
/// the certificate's own readable name, which is what the header prints.
/// </para>
/// </summary>
/// <param name="Code">The certificate's band code, used only to place a row in this column.</param>
/// <param name="Name">The certificate's own name for the band; falls back to the code when it has none.</param>
/// <param name="MinTotal">The band's lower bound, so the column is as wide as the range it covers.</param>
/// <param name="MaxTotal">The band's upper bound.</param>
public sealed record PlacementBandColumnViewModel(string Code, string Name, int MinTotal, int MaxTotal)
{
    /// <summary>One unit per score point, floored at one, so a column that exists is never zero-width.</summary>
    public int Width => Math.Max(1, MaxTotal - MinTotal);

    /// <summary>
    /// A band code is a database key. A code carrying an underscore would reach the learner as
    /// "below_A1", which is the raw-key defect this page must not ship, so the printed label is the
    /// certificate's name and the code is only a fallback when the certificate published none.
    /// </summary>
    public string Label => string.IsNullOrWhiteSpace(Name) || Name.Contains('_', StringComparison.Ordinal)
        ? CleanCode(Code)
        : Name;

    private static string CleanCode(string code) =>
        code.Replace('_', ' ').Replace("below A1", "Below A1", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// #218 / LRN-16, LRN-17: the overall floor, with its reason in words.
/// <para>
/// The overall is the <b>weakest measured skill</b>, never an average, and the placard says which skill
/// holds it down and which one reached highest. A number whose meaning the learner must infer is the
/// defect LRN-17 names.
/// </para>
/// </summary>
public sealed record PlacementFloorViewModel
{
    /// <summary>"A2 (10/100)" — the limiting skill's own coordinate, so the headline is checkable.</summary>
    public string Coordinate { get; init; } = string.Empty;

    /// <summary>The band name the certificate gives that coordinate.</summary>
    public string BandName { get; init; } = string.Empty;

    /// <summary>"Held down by Reading." Empty when only one skill was measured.</summary>
    public string LimitingSkillText { get; init; } = string.Empty;

    /// <summary>The sentence naming the ceiling and the floor. Empty when there is nothing to compare.</summary>
    public string ReasonText { get; init; } = string.Empty;

    /// <summary>"Speaking was not measured, so it is excluded from this floor." Empty when none is missing.</summary>
    public string ExcludedText { get; init; } = string.Empty;

    /// <summary>False when nothing could be measured, in which case no floor is printed at all.</summary>
    public bool HasFloor { get; init; }

    /// <summary>"Temporarily" when a productive skill is still grading, so the floor can still move.</summary>
    public bool IsProvisional { get; init; }

    /// <summary>The word above the number: "temporary floor" while grading, "floor" when settled.</summary>
    public string Label => IsProvisional ? "Temporary overall floor" : "Overall floor";

    /// <summary>The caption under the number, saying what the overall is and is not.</summary>
    public string Caption => IsProvisional
        ? "The lowest measured skill so far. This floor drops or holds when the grading finishes; it can never rise above a measured skill."
        : "The lowest measured skill. This is not an average: one weak skill is what limits the whole result.";
}
