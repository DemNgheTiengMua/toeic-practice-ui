
namespace Contracts;

public sealed record MockGatewayActionRequest(
    string OrderCode,
    string Action,
    string? ErrorCode = null);
