namespace BusinessObjects;

/// <summary>#159: the single thing the learner should do next. One rule lives here so the page
/// cannot invent a second idea of "next"; the dashboard renders this and nothing else.
/// <para>
/// #241: <see cref="CascadeStep"/> is which step of <see cref="NextActionCascade.Order"/> produced
/// this action. It is required rather than defaulted, because a default would let a new branch omit
/// it and still compile - and a cascade step nobody can name is a branch nobody can test.
/// </para></summary>
public sealed record NextAction(
    string Eyebrow,
    string Title,
    string Description,
    string ActionLabel,
    string ActionUrl,
    string Severity,
    string CascadeStep,
    string? MetaLabel = null);
