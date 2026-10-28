using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>Sections LayoutType.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LayoutTypeDto>))]
public enum LayoutTypeDto
{
    [JsonStringEnumMemberName("STANDARD_VERTICAL")] StandardVertical,
    [JsonStringEnumMemberName("SPLIT_HORIZONTAL")] SplitHorizontal,
    [JsonStringEnumMemberName("MEDIA_CARD")] MediaCard,
    [JsonStringEnumMemberName("CLUSTER_3")] Cluster3,
}
