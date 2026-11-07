using BusinessObjects;

namespace BusinessObjects;

/// <summary>
/// #222 / OQ-TR-01: the two faces of "a mock result and the learner's state".
/// <para>
/// <b>This split is the ticket.</b> A mock result is a <see cref="MockResultProjection"/>: a read that
/// has no way to write anything. Syncing it to the learner's per-skill state is a separate, explicit
/// act - <see cref="IMockResultSyncService.SyncAsync"/> - and it is the only caller of
/// <see cref="ISkillStateWriter.RecordAsync"/> on this path. If a read path could reach the writer, the
/// "a mock never writes the learner's state" criterion would be broken no matter what the tests said.
/// </para>
/// </summary>
/// <param name="AttemptId">The mock attempt this projection is of.</param>
/// <param name="CertificateScore">
/// The attempt's own total on the certificate's scale. <b>Labelled a certificate score</b>, never a
/// level: it is whatever the certificate's band geometry says, and PRN-03 refuses to call it anything
/// else.
/// </param>
/// <param name="BandCode">The certificate's own band row for that total; a band, not a level.</param>
/// <param name="BandName">That band's readable name, from the band table.</param>
/// <param name="ScoreDisplayFactor">Stored units per reported point (#152).</param>
/// <param name="Projected">
/// The paper's scaled scores read back against the certificate's cut-score table, or null for an
/// attempt that predates every published version - a preserved historical attempt, which renders its
/// native score and band and must not gain a projection retroactively.
/// </param>
/// <param name="Stored">The learner's stored per-skill state, read so the screen can say what differs. Read only.</param>
/// <param name="Historical">
/// True when the attempt carries no cut-score version to project against. The page says so rather than
/// inventing one.
/// </param>
public sealed record MockResultProjection(
    int AttemptId,
    int CertId,
    int CertificateScore,
    string? BandCode,
    string? BandName,
    int ScoreDisplayFactor,
    ScaledScoreProjection? Projected,
    LearnerStanding? Stored,
    bool Historical)
{
    /// <summary>
    /// True when the stored state and the paper disagree on at least one syncable skill, so the
    /// one-click offer is shown. False for a historical attempt (nothing to project), for a paper whose
    /// projection is <c>cannot_judge</c> as a whole (nothing to write), and when every projected level
    /// already matches.
    /// <para>
    /// A paper with one unjudged skill offers no sync even though another skill could be projected:
    /// writing half a paper's vector would let one rushed section edit half the learner's state, which
    /// is what the explicit sync exists to prevent.
    /// </para>
    /// </summary>
    public bool SyncOffered => !Historical
        && Projected is not null
        && Projected.Outcome != ProjectionOutcome.CannotJudge
        && Projected.SkillIdsDifferingFrom(Stored).Count > 0;
}
