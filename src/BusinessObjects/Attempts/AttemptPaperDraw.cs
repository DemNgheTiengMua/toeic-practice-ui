using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #274: the paper a sitting is served, as a draw hands it over.
/// <para>
/// <b>Why this type exists at all.</b> A paper normally comes from the exam's shared roster, which is
/// per EXAM. A placement sitting draws its own per learner, so the draw needs a way to say "here is
/// what this candidate gets" without rewriting the roster - which
/// <c>UQ_ExamItems_Exam_Section_Order</c> makes impossible anyway.
/// </para>
/// <para>
/// It carries two facts, and they are recorded in two tables on purpose: <see cref="Bundles"/> is WHY
/// (the stage plan and the band pair stage one routed to, a derived fact ADR-0012 guards) and
/// <see cref="Positions"/> is WHAT (the roster positions, in sitting order). Splitting them keeps the
/// guarded fact small enough to have exactly one writer.
/// </para>
/// </summary>
/// <param name="Positions">
/// The roster positions to serve, in the order the learner meets them. Position numbers are 1-based and
/// contiguous; a gap would leave the navigator with a hole in it.
/// </param>
/// <param name="Bundles">
/// One row per skill per stage. Empty for a paper that is served whole, which is the shared roster's
/// shape - and the reason the two branches return the same projection.
/// </param>
public sealed record AttemptPaperDraw(
    IReadOnlyList<AttemptPaperPosition> Positions,
    IReadOnlyList<AttemptPaperBundle> Bundles);

/// <summary>#274: one served position: which roster position, and where it sits in the sitting.</summary>
public sealed record AttemptPaperPosition(int PositionNumber, int ExamItemId);

/// <summary>
/// #274: why one skill of this sitting was drawn as it was. <c>StageNumber</c> is 1 for the fixed
/// per-band draw every objective skill gets and 2 for the pair stage one routed to.
/// </summary>
public sealed record AttemptPaperBundle(
    int SkillId,
    int StageNumber,
    CefrLevel BandLower,
    CefrLevel BandUpper);
