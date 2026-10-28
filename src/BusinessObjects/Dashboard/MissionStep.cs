
namespace BusinessObjects;

public sealed record MissionStep(
    int StepNumber,
    string BadgeLabel,
    string Severity,
    string Eyebrow,
    string Title,
    string Description,
    string MetaLabel,
    string ActionLabel,
    string ActionUrl,
    bool IsCleared);
