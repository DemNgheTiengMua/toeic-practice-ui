namespace Contracts;

public sealed record ComplaintEligibleOrderDto(
    int OrderId,
    string OrderCode,
    int Credits,
    decimal Amount,
    DateTimeOffset CreatedAt,
    string Status);
