using System.Globalization;

namespace Contracts;

/// <summary>
/// #152: a stored score as the learner reads it. Certificates whose scale is kept in tenths (IELTS,
/// VSTEP — their bands read 40–90 for 4.0–9.0) store 80 for 8.0, so printing the stored integer is
/// wrong by a factor of ten: it is how "75 · out of 90" appeared for a 7.5.
/// <para>
/// The factor is a property of the certificate, not of any one page, and this is the only place the
/// division happens. Every rendered score goes through it.
/// </para>
/// </summary>
public static class ScoreDisplay
{
    /// <summary>
    /// How many stored units make one reported point. A certificate that averages to a half step keeps
    /// its marks in tenths; everything else stores the number it reports.
    /// </summary>
    public static int FactorFor(string? scoreAggregationRule) =>
        string.Equals(scoreAggregationRule, "avg_half_step", StringComparison.OrdinalIgnoreCase) ? 10 : 1;

    /// <summary>The score as text: whole points stay whole, tenths show one decimal.</summary>
    public static string Format(int stored, int factor) =>
        (stored / (decimal)factor).ToString("0.#", CultureInfo.InvariantCulture);
}
