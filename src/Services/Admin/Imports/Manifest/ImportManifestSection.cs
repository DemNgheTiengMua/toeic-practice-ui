using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services;

public sealed class ImportManifestSection
{
    public string? SectionCode { get; set; }
    public List<ImportManifestGroup>? Groups { get; set; }
}
