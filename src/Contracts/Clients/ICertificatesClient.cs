
namespace Contracts;

public interface ICertificatesClient
{
    Task<IReadOnlyList<CertificateDto>> GetCertificatesAsync(CancellationToken cancellationToken = default);
}
