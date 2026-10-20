namespace WebMVC.Models;

/// <summary>
/// Model for rendering the shared exam/study audio player. Mode takes precedence over
/// AllowReplay; when omitted, AllowReplay preserves the previous mode selection behavior.
/// </summary>
public sealed record ExamAudioPlayerModel(
    string AudioPath,
    bool AllowReplay = false,
    bool AutoPlay = false,
    string? SectionTitle = null,
    string? BadgeText = null,
    string? PlayerId = null,
    int? StartMs = null,
    int? EndMs = null,
    string? Mode = null,
    bool TemplateOnly = false
);
