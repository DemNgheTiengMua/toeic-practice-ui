using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Refunds_Status. A credit refund ends at <see cref="Approved"/>: the ledger, not a
/// bank, is where the value lands.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RefundStatusDto>))]
public enum RefundStatusDto
{
    [JsonStringEnumMemberName("requested")] Requested,
    [JsonStringEnumMemberName("approved")] Approved,
    [JsonStringEnumMemberName("settled")] Settled,
    [JsonStringEnumMemberName("rejected")] Rejected,
}
