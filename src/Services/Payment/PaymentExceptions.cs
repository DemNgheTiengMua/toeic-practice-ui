using BusinessObjects;
namespace Services;

public sealed class EmailNotVerifiedException(string message = "Email verification is required before purchasing credits.")
    : Exception(message);

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

public sealed class OrderNotFoundException(string orderCode)
    : Exception($"Order '{orderCode}' was not found.")
{
    public string OrderCode { get; } = orderCode;
}

public sealed class PackageNotFoundException(int packageId)
    : Exception($"Package '{packageId}' was not found.")
{
    public int PackageId { get; } = packageId;
}
