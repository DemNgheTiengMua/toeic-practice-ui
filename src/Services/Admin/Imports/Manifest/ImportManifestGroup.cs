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

    /// <summary>
    /// #189: the level of the stimulus these children share (L_s in D0.3). It is the stimulus's own
    /// level, not a child's: the whole point of the envelope is that the passage has a level
    /// independent of the items asked about it. Absent, the first child's level is adopted.
    /// </summary>
    public string? CefrLevel { get; set; }

    /// <summary>#189: workplace, academic or general. Register only; it never changes what is measured.</summary>
    public string? DomainContext { get; set; }

    /// <summary>#189: a productive task's instruction copy, where it is not the passage.</summary>
    public string? PromptCopy { get; set; }

    public List<ImportManifestQuestion>? Questions { get; set; }
}
