
namespace Contracts;

public sealed record AdminPaymentEventDto(
    int EventId,
    int OrderId,
    PaymentEventSourceDto Source,
    GatewayStatusDto? ReportedStatus,
    bool SignatureValid,
    bool WasApplied,
    string? Note,
    string? Payload,
    DateTimeOffset CreatedAt);
