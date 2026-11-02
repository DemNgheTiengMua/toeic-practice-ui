namespace Contracts;

/// <summary>#159: the one thing the learner should do next, decided by the service. The dashboard
/// hero renders this and nothing else, so a greeting can no longer stand where an action belongs.</summary>
public sealed record NextActionDto(
    string Eyebrow,
    string Title,
    string Description,
    string ActionLabel,
    string ActionUrl,
    string Severity,
    string? MetaLabel = null);
