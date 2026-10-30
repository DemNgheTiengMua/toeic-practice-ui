using Contracts;

namespace WebMVC.Models;

public sealed class AdminCertificateListViewModel
{
    public IReadOnlyList<AdminCertificateSummaryDto> Certificates { get; init; } = [];

    /// <summary>Set when the certificate list could not be loaded, so the page says why instead of
    /// showing the "no certificates yet" state that would read as an empty database.</summary>
    public string? ErrorMessage { get; init; }
}
