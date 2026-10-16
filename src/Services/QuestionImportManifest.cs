using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services;

// The ingest manifest (ADR-0007), as authored. Every field is nullable so a missing value is a
// validation message, not a deserialisation crash; QuestionImportService decides what is required.

internal sealed class ImportManifest
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
    };
}

internal sealed class ImportManifestExam
{
    public string? Title { get; set; }
    public string? Purpose { get; set; }
    public int? DurationMinutes { get; set; }
    public string? SourceName { get; set; }
    public string? Description { get; set; }
    public int? QuestionCount { get; set; }
}

internal sealed class ImportManifestSection
{
    public string? SectionCode { get; set; }
    public List<ImportManifestGroup>? Groups { get; set; }
}

internal sealed class ImportManifestGroup
{
    public string? Passage { get; set; }
    public string? AudioFile { get; set; }
    public int? AudioStartMs { get; set; }
    public int? AudioEndMs { get; set; }
    public string? ImageFile { get; set; }
    public string? ImageAlt { get; set; }
    public string? Transcript { get; set; }
    public int? PrepSeconds { get; set; }
    public int? ResponseSeconds { get; set; }
    public int? DisplayOrder { get; set; }
    public List<ImportManifestQuestion>? Questions { get; set; }
}

internal sealed class ImportManifestQuestion
{
    public string? Stem { get; set; }
    public int? DifficultyLevel { get; set; }
    public string? QuestionType { get; set; }
    public string? CategoryCode { get; set; }
    public string? Explanation { get; set; }
    public string? Hint { get; set; }
    public int? PrepSeconds { get; set; }
    public int? ResponseSeconds { get; set; }
    public int? MinWords { get; set; }
    public int? MaxWords { get; set; }
    public int? DisplayOrder { get; set; }
    public List<ImportManifestOption>? Options { get; set; }
    public List<ImportManifestCriterion>? Criteria { get; set; }
}

internal sealed class ImportManifestOption
{
    public string? Label { get; set; }
    public string? Text { get; set; }
    public bool IsCorrect { get; set; }
    public string? Rationale { get; set; }
}

internal sealed class ImportManifestCriterion
{
    public string? Code { get; set; }
}
