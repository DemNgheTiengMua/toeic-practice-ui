
namespace Contracts;

public sealed record ComplaintEligibleOrderDto(
    int OrderId,
    string OrderCode,
    string PackageName,
    decimal Amount,
    DateTimeOffset CreatedAt,
    string Status);
