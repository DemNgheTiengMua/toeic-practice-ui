namespace Contracts;

/// <summary>
/// #202 / S-01: one cell of the warehouse matrix, as the screen reads it.
/// <para>
/// Every field is a value the database view produced. The screen adds no number of its own — no
/// totals, no percentages, no "remaining after this" — because a second arithmetic path over the same
/// stock is how a coverage screen starts disagreeing with the pool it reports.
/// </para>
/// </summary>
public sealed record AdminCoverageCellDto(
    int TopicId,
    string Modality,
    string TopicCode,
    string TopicName,
    string CefrLevel,
    int TargetItemCount,
    int ActiveItemCount,
    int PlacementEligibleItemCount,
    int Deficit);

/// <summary>#202: the whole matrix. Always 120 cells, empty ones included.</summary>
public sealed record AdminCoverageMatrixDto(IReadOnlyList<AdminCoverageCellDto> Cells);
