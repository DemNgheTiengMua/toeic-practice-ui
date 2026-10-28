using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Orders_Gateway: payos, mock.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentGatewayDto>))]
public enum PaymentGatewayDto
{
    [JsonStringEnumMemberName("payos")] Payos,
    [JsonStringEnumMemberName("mock")] Mock,
}
