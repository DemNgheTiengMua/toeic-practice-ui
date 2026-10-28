using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_ImportLog_Level: info, warn, error.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImportLogLevelDto>))]
public enum ImportLogLevelDto
{
    [JsonStringEnumMemberName("info")] Info,
    [JsonStringEnumMemberName("warn")] Warn,
    [JsonStringEnumMemberName("error")] Error,
}
