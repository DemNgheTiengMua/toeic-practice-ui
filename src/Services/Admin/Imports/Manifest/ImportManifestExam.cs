using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services;

public sealed class ImportManifestExam
{
    public string? Title { get; set; }
    public string? Purpose { get; set; }
    public string? DeliveryMode { get; set; }
    public int? DurationMinutes { get; set; }
    public string? SourceName { get; set; }
    public string? Description { get; set; }
    public int? QuestionCount { get; set; }
}
