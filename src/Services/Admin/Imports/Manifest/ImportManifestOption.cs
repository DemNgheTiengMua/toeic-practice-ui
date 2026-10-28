using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services;

public sealed class ImportManifestOption
{
    public string? Label { get; set; }
    public string? Text { get; set; }
    public bool IsCorrect { get; set; }
    public string? Rationale { get; set; }
}
