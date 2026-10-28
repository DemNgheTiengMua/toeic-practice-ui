using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_ImportBatches_Status: pending, done, failed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImportBatchStatusDto>))]
public enum ImportBatchStatusDto
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("failed")] Failed,
}
