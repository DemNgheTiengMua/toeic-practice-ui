using System;
using System.Collections.Generic;
using System.Linq;

namespace Services;

/// <summary>
/// #192: the canonical analytic rubric - four criteria per productive skill, seven bands each.
/// <para>
/// The four codes live here rather than only in the schema because the schema can refuse a fifth
/// criterion at the lock door, but the grader has to know which four it is being asked to score.
/// One list, read by both, is what stops the two drifting apart.
/// </para>
/// <para>
/// Scope note: <c>Skills</c> is certificate-scoped, so each certificate has its own four criteria for
/// its own speaking skill. This type prescribes the four <em>codes</em>, not rows; the certificate owns
/// the rows, and the lock checks that certificate's own rows against this set.
/// </para>
/// </summary>
public static class CanonicalRubric
{
    /// <summary>The highest band, and the band count minus one: the scale is integers 0..6 (D0.6).</summary>
    public const byte HighestBand = 6;

    /// <summary>Bands 0..6 - the seven a criterion must describe before its rubric can lock.</summary>
    public static readonly IReadOnlyList<byte> Bands = Enumerable.Range(0, HighestBand + 1).Select(b => (byte)b).ToArray();

    private static readonly IReadOnlyDictionary<string, string[]> ByModality =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["speaking"] = ["S_FC", "S_PR", "S_LR", "S_GR"],
            ["writing"] = ["W_TR", "W_CC", "W_LR", "W_GR"],
        };

    /// <summary>
    /// The four codes this modality's rubric is made of, or null when the modality has no analytic
    /// rubric at all. Listening and reading are scored by their key, not by descriptors, so they have
    /// no canonical set and cannot be locked (#192).
    /// </summary>
    public static IReadOnlyList<string>? CodesFor(string? modality) =>
        modality is not null && ByModality.TryGetValue(modality, out var codes) ? codes : null;

    /// <summary>True when this skill modality is scored against a written rubric at all.</summary>
    public static bool HasRubric(string? modality) => CodesFor(modality) is not null;

    /// <summary>
    /// Why <paramref name="codes"/> is not the canonical four for this modality, or null when it is.
    /// Both directions are compared: four rows is not four canonical codes, and ten codes is not
    /// "at least four". The message names what is missing and what is extra, because an author has
    /// to fix it from the text.
    /// </summary>
    public static string? DescribeMismatch(string? modality, IReadOnlyCollection<string> codes)
    {
        var canonical = CodesFor(modality);
        if (canonical is null)
        {
            return $"A {modality ?? "null"} skill has no analytic rubric: listening and reading are scored by their key, not by descriptors.";
        }

        var present = codes.Select(c => c.Trim().ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = canonical.Where(c => !present.Contains(c)).ToList();
        var extra = present.Where(c => !canonical.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();

        if (missing.Count == 0 && extra.Count == 0 && present.Count == canonical.Count) return null;

        var parts = new List<string>();
        if (missing.Count > 0) parts.Add("missing " + string.Join(", ", missing));
        if (extra.Count > 0) parts.Add("unexpected " + string.Join(", ", extra));
        return $"A {modality} rubric is exactly {string.Join(", ", canonical)}; this one has "
            + (parts.Count == 0 ? "a duplicate code" : string.Join(" and ", parts)) + ".";
    }
}
