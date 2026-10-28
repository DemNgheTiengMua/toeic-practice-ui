using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services;

// The ingest manifest (ADR-0007), as authored. Every field is nullable so a missing value is a
// validation message, not a deserialisation crash; QuestionImportService decides what is required.

public sealed class ImportManifest
{
    /// <summary>Editor hint only; accepted so authors can point their editor at a schema.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public string? Version { get; set; }
    public ImportManifestExam? Exam { get; set; }
    public List<ImportManifestSection>? Sections { get; set; }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };
}
