
namespace Contracts;

public sealed record AdminNeedsAttentionDto(
    int DraftExamsCount,
    int StalePendingOrdersCount,
    int OpenComplaintsCount);
