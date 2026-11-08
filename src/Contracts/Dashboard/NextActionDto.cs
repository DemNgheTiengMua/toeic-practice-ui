namespace Contracts;

/// <summary>#159: the one thing the learner should do next, decided by the service. The dashboard
/// hero renders this and nothing else, so a greeting can no longer stand where an action belongs.
/// <para>
/// #241: <see cref="CascadeStep"/> is which step of the fixed cascade produced this action - one of
/// <c>NextActionCascade.Order</c>. The page prints it as a plain-language placard, and the tests use
/// it to prove the order decided rather than "the first non-empty list".
/// </para></summary>
public sealed record NextActionDto(
    string Eyebrow,
    string Title,
    string Description,
    string ActionLabel,
    string ActionUrl,
    string Severity,
    string CascadeStep,
    string? MetaLabel = null);
