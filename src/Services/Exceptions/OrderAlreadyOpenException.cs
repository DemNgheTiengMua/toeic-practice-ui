using BusinessObjects;

namespace Services;

public sealed class OrderAlreadyOpenException : Exception
{
    public string OrderCode { get; }
    public int Credits { get; }
    public decimal Amount { get; }
    public DateTimeOffset CreatedAt { get; }
    public OrderStatus Status { get; }

    public OrderAlreadyOpenException(
        string orderCode,
        int credits,
        decimal amount,
        DateTimeOffset createdAt,
        OrderStatus status,
        string message = "You already have an open order that must be completed or expired.")
        : base(message)
    {
        OrderCode = orderCode;
        Credits = credits;
        Amount = amount;
        CreatedAt = createdAt;
        Status = status;
    }
}
