using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_PaymentEvents_ReportedStatus: pending, paid, failed, expired, cancelled.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GatewayStatusDto>))]
public enum GatewayStatusDto
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("paid")] Paid,
    [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("expired")] Expired,
    [JsonStringEnumMemberName("cancelled")] Cancelled,
}
