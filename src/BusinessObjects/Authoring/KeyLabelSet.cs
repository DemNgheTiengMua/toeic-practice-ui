namespace BusinessObjects;

/// <summary>
/// #250: the canonical form of <c>ItemVersions.KeyLabel</c>, and the one place that decides what a
/// key means.
/// <para>
/// <b>Why a set.</b> The pool could hold one key label, which is the four-option single-best-answer
/// shape. The paper's <c>multi_select</c> marks two or more options correct, and there was no way to
/// say so: the importer refused a file with two correct options, and the scorer compared one label
/// with <c>==</c>. Both are the same missing concept. A key is a <em>set of labels</em>; one label is
/// the special case of a set with one member.
/// </para>
/// <para>
/// <b>Why the canonical form is a string and not a column per label.</b> Four nullable columns
/// (<c>KeyA</c>..<c>KeyD</c>) would need a rule for which one wins when two disagree and would make
/// "is this option correct" a four-way OR at every reader. One canonical string keeps the invariant
/// checkable by the table's own CHECK and makes the reader's question a single membership test.
/// </para>
/// <para>
/// <b>The canonical form is load-bearing, not cosmetic.</b> Ascending, comma-separated, no spaces,
/// no repeats: <c>'A'</c>, <c>'A,C'</c>, <c>'A,B,D'</c>. Two writers producing 'C,A' and 'A,C' for
/// the same answer would compare unequal, and a re-import would look like a change.
/// </para>
/// </summary>
public static class KeyLabelSet
{
    /// <summary>The four labels a pool item may use. Order is the canonical order.</summary>
    public static readonly IReadOnlyList<string> Allowed = ["A", "B", "C", "D"];

    /// <summary>
    /// The canonical key for <paramref name="labels"/>, or null when the set is empty or names a label
    /// outside A-D. Null is "no key", which is the productive shape and never an error.
    /// </summary>
    public static string? From(IEnumerable<string?> labels)
    {
        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            var clean = label?.Trim().ToUpperInvariant();
            if (clean is null)
            {
                continue;
            }

            if (!Allowed.Contains(clean))
            {
                return null;
            }

            chosen.Add(clean);
        }

        return chosen.Count == 0 ? null : string.Join(',', Allowed.Where(chosen.Contains));
    }

    /// <summary>
    /// The canonical form of an already-encoded key, or null when it is malformed. Used by writers
    /// that receive a key from a client rather than a set of options.
    /// </summary>
    public static string? Normalise(string? keyLabel) =>
        string.IsNullOrWhiteSpace(keyLabel) ? null : From(Split(keyLabel));

    /// <summary>The labels <paramref name="keyLabel"/> names, in canonical order. Empty when malformed.</summary>
    public static IReadOnlyList<string> Split(string? keyLabel)
    {
        if (string.IsNullOrWhiteSpace(keyLabel))
        {
            return [];
        }

        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in keyLabel.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var clean = part.ToUpperInvariant();
            if (!Allowed.Contains(clean))
            {
                return [];
            }

            chosen.Add(clean);
        }

        return Allowed.Where(chosen.Contains).ToList();
    }

    /// <summary>Whether <paramref name="label"/> is one of the labels the key marks correct.</summary>
    public static bool Contains(string? keyLabel, string? label)
    {
        var clean = label?.Trim().ToUpperInvariant();
        return clean is not null && Split(keyLabel).Contains(clean);
    }

    /// <summary>The first label of the key, or null. For a cell-balance heuristic that needs one label.</summary>
    public static string? First(string? keyLabel) => Split(keyLabel).FirstOrDefault();
}
