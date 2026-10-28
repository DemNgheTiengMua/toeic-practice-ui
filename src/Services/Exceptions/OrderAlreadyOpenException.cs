using BusinessObjects;

namespace Services;

public sealed class OrderAlreadyOpenException : Exception
{
    public string OrderCode { get; }
    public string PackageName { get; }
    public decimal Amount { get; }
    public DateTimeOffset CreatedAt { get; }
    public OrderStatus Status { get; }

    public OrderAlreadyOpenException(
        string orderCode,
        string packageName,
        decimal amount,
        DateTimeOffset createdAt,
        OrderStatus status,
        string message = "You already have an open order that must be completed or expired.")
        : base(message)
    {
        OrderCode = orderCode;
        PackageName = packageName;
        Amount = amount;
        CreatedAt = createdAt;
        Status = status;
    }
}
