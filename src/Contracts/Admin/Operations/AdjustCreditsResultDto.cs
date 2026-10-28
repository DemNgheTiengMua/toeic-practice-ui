
namespace Contracts;

public sealed record AdjustCreditsResultDto(
    int TxnId,
    int UserId,
    int NewBalance,
    int Delta,
    string Reason);
