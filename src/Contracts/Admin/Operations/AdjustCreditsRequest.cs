
namespace Contracts;

public sealed record AdjustCreditsRequest(
    int Delta,
    string Reason);
