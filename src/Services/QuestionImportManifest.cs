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

public sealed class ImportManifestSection
{
    public string? SectionCode { get; set; }
    public List<ImportManifestGroup>? Groups { get; set; }
}

public sealed class ImportManifestGroup
{
    public int? UnitNumber { get; set; }
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

public sealed class ImportManifestQuestion
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
    public int? MaxSelections { get; set; }
    public bool? AllowNumber { get; set; }

    [JsonConverter(typeof(StringOrStringListConverter))]
    public List<string>? AcceptedAnswers { get; set; }

    public string? AcceptedAnswersJson { get; set; }

    [JsonConverter(typeof(StringOrStringListConverter))]
    public List<string>? RequiredKeywords { get; set; }

    public List<ImportManifestOption>? Options { get; set; }
    public List<ImportManifestCriterion>? Criteria { get; set; }
}

public sealed class ImportManifestOption
{
    public string? Label { get; set; }
    public string? Text { get; set; }
    public bool IsCorrect { get; set; }
    public string? Rationale { get; set; }
}

public sealed class ImportManifestCriterion
{
    public string? Code { get; set; }
}

public sealed class StringOrStringListConverter : JsonConverter<List<string>?>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return [];
            }

            var trimmed = str.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(trimmed);
                    if (list != null)
                    {
                        return list.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
                    }
                }
                catch
                {
                }
            }

            return trimmed.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return list;
                }

                if (reader.TokenType == JsonTokenType.String)
                {
                    var item = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        list.Add(item.Trim());
                    }
                }
                else if (reader.TokenType is JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False)
                {
                    using var doc = JsonDocument.ParseValue(ref reader);
                    var text = doc.RootElement.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        list.Add(text.Trim());
                    }
                }
            }

            return list;
        }

        throw new JsonException($"Expected string or array of strings, got {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}
