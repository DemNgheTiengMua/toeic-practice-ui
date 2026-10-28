using BusinessObjects;

namespace Services;

public sealed class OrderNotFoundException(string orderCode)
    : Exception($"Order '{orderCode}' was not found.")
{
    public string OrderCode { get; } = orderCode;
}
