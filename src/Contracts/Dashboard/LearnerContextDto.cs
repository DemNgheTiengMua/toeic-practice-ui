
namespace Contracts;

/// <summary>Everything the learner shell renders on every page: the active certificate, the ones
/// the switcher offers and the credit balance. Dates render through DateTimeFormatter, which
/// loads the time zone itself (issue #6).</summary>
public sealed record LearnerContextDto(
    CertificateOptionDto Active,
    IReadOnlyList<CertificateOptionDto> Certificates,
    int CreditBalance);
