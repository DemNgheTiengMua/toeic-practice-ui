using System;
using System.Collections.Generic;
using System.Linq;

namespace Services;

/// <summary>
/// #206 / story 29: which items of a cell the audit draws.
/// <para>
/// <b>The draw is stratified, not random, and this class is why.</b> A random 10% of a 200-item batch can
/// contain no C1 or C2 item at all - those cells hold four or five items by construction - so the bands
/// where a wrong key does the most damage are exactly the ones randomness omits. The rule here takes
/// every cell first and only then decides how many of its items to draw.
/// </para>
/// <para>
/// <b>Per cell: 10%, rounded UP, with a floor of one and a ceiling of the cell's own size.</b>
/// Rounding up rather than down is the floor for a very small cell: 10% of one item rounds down to
/// zero, and a zero-item sample is a cell outside the audit. So:
/// 1 item → 1, 10 → 1, 19 → 2, 20 → 2, 100 → 10.
/// </para>
/// <para>
/// <b>Within a cell the draw is evenly spread, not the lowest ids.</b> Taking the first <em>k</em> items
/// of a cell samples whatever was authored first - often one stimulus's children, which then hides the
/// shared-passage defect the sample exists to find. The spread below is deterministic, so a re-run draws
/// the same items and a reviewer can be told which ones to solve twice.
/// </para>
/// </summary>
public static class StratifiedSample
{
    /// <summary>The audit samples a tenth of every cell.</summary>
    public const decimal Proportion = 0.10m;

    /// <summary>
    /// How many of <paramref name="itemCount"/> a cell contributes. Never zero for a non-empty cell -
    /// see the class comment for why that floor is the whole point of the ticket.
    /// </summary>
    public static int SizeFor(int itemCount)
    {
        if (itemCount <= 0)
        {
            return 0;
        }

        // Ceiling of 10%, then clamped. Integer arithmetic on purpose: a double rounds 0.1*10 to
        // 1.0000000000000002 on some platforms and ceil() then yields 2.
        var tenth = (itemCount + 9) / 10;
        return Math.Clamp(tenth, 1, itemCount);
    }

    /// <summary>
    /// The indices of <paramref name="items"/> to draw, evenly spread over the ordered list.
    /// <para>
    /// For a cell of 20 drawn twice this is indices 0 and 10 - the first and the middle, not the first
    /// two. <paramref name="items"/> must already be in a stable order (the caller orders by id), or two
    /// runs would disagree about which items were reviewed.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> IndicesFor(int itemCount, int sampleSize)
    {
        var take = Math.Clamp(sampleSize, 0, itemCount);
        if (take == 0)
        {
            return [];
        }

        if (take == itemCount)
        {
            return Enumerable.Range(0, itemCount).ToList();
        }

        var picked = new List<int>(take);
        for (var i = 0; i < take; i++)
        {
            // floor(i * N / k): the k evenly spaced positions over N. i < k <= N keeps this < N, and
            // the multiply-before-divide keeps it monotonic, so no index repeats.
            var index = (int)((long)i * itemCount / take);
            picked.Add(index);
        }

        return picked;
    }
}
