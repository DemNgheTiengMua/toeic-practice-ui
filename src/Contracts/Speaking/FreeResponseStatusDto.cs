using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_FreeResp_Status: draft, pending_ai, graded.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FreeResponseStatusDto>))]
public enum FreeResponseStatusDto
{
    [JsonStringEnumMemberName("draft")] Draft,
    [JsonStringEnumMemberName("pending_ai")] PendingAi,
    [JsonStringEnumMemberName("graded")] Graded,
}
