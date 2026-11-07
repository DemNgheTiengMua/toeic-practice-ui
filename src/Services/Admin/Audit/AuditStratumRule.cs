using System;
using System.Collections.Generic;
using System.Linq;

namespace Services;

/// <summary>
/// #206 / stories 31 and 32: the one place the audit's decision rule is written.
/// <para>
/// <b>Story 31 - one key error quarantines the whole stratum.</b> Not "the item", not "at a rate":
/// one. A key error is systematic - the same mistake is in every item authored against that key's
/// pattern - so a 10% sample that finds one is evidence about the other 90%, not about one row.
/// </para>
/// <para>
/// <b>Story 32 - a non-key defect rate above 5%.</b> The comparison is <c>&gt;</c>, not <c>&gt;=</c>,
/// and it is computed over the <em>stratum's sample</em>, never over the batch. Both halves are
/// pinned by a test: at a sample of 20, one non-key defect is exactly 5% and must NOT quarantine; two
/// (10%) must. A batch-wide denominator would let a clean 100-item cell dilute a broken 2-item one,
/// which is the defect stratification exists to prevent.
/// </para>
/// <para>
/// A defect rate is only meaningful over what was judged, so the denominator is the number of verdicts
/// recorded, not <c>SampleSize</c>. An unjudged item is not evidence in either direction.
/// </para>
/// </summary>
public static class AuditStratumRule
{
    /// <summary>The threshold, stated once. Story 32: "a non-key defect rate above 5%".</summary>
    public const decimal NonKeyDefectThreshold = 0.05m;

    public const string KeyError = "key_error";
    public const string NonKeyDefectRate = "non_key_defect_rate";

    /// <summary>
    /// The decision the verdicts justify, or null while the stratum is still undecided.
    /// <para>
    /// Precedence follows the stories' own logic rather than the order they are written in: a key error
    /// is the stronger finding and is reported as the reason even when the defect rate is also over.
    /// A stratum that says "5% of items were defective" when one of them had a wrong key sends the
    /// author looking for level drift instead of a bad key.
    /// </para>
    /// </summary>
    public static (string Status, string? Reason)? Decide(IReadOnlyCollection<string> verdicts)
    {
        if (verdicts.Count == 0)
        {
            return null;
        }

        if (verdicts.Any(v => string.Equals(v, "key_error", StringComparison.Ordinal)))
        {
            return ("quarantined", KeyError);
        }

        var defective = verdicts.Count(v => string.Equals(v, "non_key_defect", StringComparison.Ordinal));
        var rate = (decimal)defective / verdicts.Count;

        // "above 5%", so equal is a pass. See the class comment and the boundary test.
        return rate > NonKeyDefectThreshold
            ? ("quarantined", NonKeyDefectRate)
            : ("approved", null);
    }
}
