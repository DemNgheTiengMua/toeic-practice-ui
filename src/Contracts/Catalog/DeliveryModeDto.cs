using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Exams_DeliveryMode: strict_exam, practice.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeliveryModeDto>))]
public enum DeliveryModeDto
{
    [JsonStringEnumMemberName("strict_exam")] StrictExam,
    [JsonStringEnumMemberName("practice")] Practice,
}
