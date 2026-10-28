using Contracts;

namespace WebMVC.Models;

public sealed class AdminCertificateListViewModel
{
    public IReadOnlyList<AdminCertificateSummaryDto> Certificates { get; init; } = [];
}
