using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services;

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
