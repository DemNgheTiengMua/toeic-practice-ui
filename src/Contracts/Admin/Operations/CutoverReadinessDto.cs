namespace Contracts;

/// <summary>
/// #247 / #248: what stands between this database and a cutover, as a list rather than a verdict.
/// <para>
/// <b>Why this exists as code.</b> Both tickets say "no attempt is mid-sitting on a legacy exam, and
/// the check that enforces it is automated". A sentence in an operations document is not a check: it
/// is a step somebody has to remember on the one night it matters. The condition is a query, so it is
/// a query here, and the cutover script refuses to continue when this returns any blocker.
/// </para>
/// <para>
/// <b>Why a list and not a boolean.</b> "Not ready" is not actionable by itself. Each entry names the
/// thing that is wrong and how many rows it affects, so the person running the cutover knows whether
/// they are looking at one abandoned sitting or a whole exam's worth.
/// </para>
/// </summary>
public sealed record CutoverReadinessDto(IReadOnlyList<CutoverBlockerDto> Blockers)
{
    /// <summary>Nothing blocks the cutover. The only state a cutover may run in.</summary>
    public bool Ready => Blockers.Count == 0;
}

/// <summary>
/// One condition that blocks the cutover. <see cref="Code"/> is stable and is what a script branches
/// on; <see cref="Message"/> is what a human reads.
/// </summary>
public sealed record CutoverBlockerDto(string Code, string Message, int AffectedRows);
