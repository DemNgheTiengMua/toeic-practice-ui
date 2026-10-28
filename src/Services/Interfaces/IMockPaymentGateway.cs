using BusinessObjects;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Services;

public interface IMockPaymentGateway : IPaymentGateway
{
    void SetOutcome(long paymentNo, GatewayStatus status, string? errorCode = null);
    bool IsCancelled(long paymentNo);
    IReadOnlyCollection<long> CancelledPaymentNos { get; }
}
