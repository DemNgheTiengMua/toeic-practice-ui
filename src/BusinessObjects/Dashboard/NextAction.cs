namespace BusinessObjects;

/// <summary>#159: the single thing the learner should do next. One rule lives here so the page
/// cannot invent a second idea of "next"; the dashboard renders this and nothing else.</summary>
public sealed record NextAction(
    string Eyebrow,
    string Title,
    string Description,
    string ActionLabel,
    string ActionUrl,
    string Severity,
    string? MetaLabel = null);
