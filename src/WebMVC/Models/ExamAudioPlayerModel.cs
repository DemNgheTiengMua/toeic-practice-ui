namespace WebMVC.Models;

/// <summary>
/// Model for rendering the exam audio player adhering to the "Bảng hiệu phòng thi" design system.
/// Seeking is strictly disabled across all modes. In formal exam/placement modes (AllowReplay = false),
/// the audio is played strictly once and permanently locked upon completion.
/// </summary>
public sealed record ExamAudioPlayerModel(
    string AudioPath,
    bool AllowReplay = false,
    bool AutoPlay = false,
    string? SectionTitle = null,
    string? BadgeText = null,
    string? PlayerId = null
);
