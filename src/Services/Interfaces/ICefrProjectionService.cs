using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #210: the reasons a projection refuses to answer. They are distinct strings on purpose — "publish
/// a cut-score table", "that version does not exist", "this certificate carries no study-hour
/// evidence" and "this level has no row" each send an admin or a learner somewhere different, and one
/// shared <c>cannot_judge</c> would hide that.
/// </summary>
public static class CefrProjection
{
    /// <summary>The table has no row for this coordinate's level. Never the nearest row.</summary>
    public const string NoCutScoreRowForLevel = "no_cut_score_row_for_level";

    /// <summary>The certificate publishes no cut-score version at all.</summary>
    public const string NoCutScoreTable = "no_cut_score_table";

    /// <summary>The requested version is not published for this certificate.</summary>
    public const string UnknownCutScoreTableVersion = "unknown_cut_score_table_version";

    /// <summary>Every band the projection could be checked against has NULL study hours, so the
    /// certificate states no study-hour evidence (LVL-16). A prorated number is refused.</summary>
    public const string BandsCarryNoStudyHours = "bands_carry_no_study_hours";
}

/// <summary>
/// #210 / D6 / LVL-11..13: projects a learner's per-skill CEFR coordinate onto a certificate's own
/// scale, then aggregates by that certificate's rule.
/// <para>
/// <b>A lookup, not a computation.</b> The cut scores come from <c>CefrCutScores</c> and nowhere else;
/// there is deliberately no arithmetic that turns a coordinate into a score. That is the difference
/// between this and the label it replaces: §4.6's numbers are taken from published standard-setting
/// studies, and 785 is the sum of Listening 400 and Reading 385 rather than a threshold a total may
/// be tested against (LVL-12).
/// </para>
/// <para>
/// <b>PRN-03: a band is not a CEFR level.</b> This interface returns a level per skill and a score
/// per certificate, and <see cref="CertificateProjection"/> has no level member at all, so the
/// "TOEIC 785–940 is B2" claim cannot be expressed here.
/// </para>
/// </summary>
public interface ICefrProjectionService
{
    /// <summary>
    /// Projects each skill in <paramref name="coordinates"/> and aggregates them.
    /// </summary>
    /// <param name="certId">The certificate whose scale and rule are used.</param>
    /// <param name="coordinates">One measured coordinate per skill. A skill absent from this map is
    /// not projected and is not counted in the total.</param>
    /// <param name="tableVersion">The cut-score version to read. Null reads the newest, which is what
    /// a fresh projection wants; a recorded projection passes the version it stored.</param>
    Task<CertificateProjection> ProjectAsync(
        int certId,
        IReadOnlyDictionary<int, (CefrLevel Level, int PositionInBand)> coordinates,
        int? tableVersion = null,
        CancellationToken cancellationToken = default);
}
