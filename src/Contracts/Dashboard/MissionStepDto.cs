
namespace Contracts;

public sealed record MissionStepDto(
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
