using System.Threading;
using System.Threading.Tasks;

namespace Contracts;

/// <summary>#202 / S-01: the coverage matrix, read-only.</summary>
public interface IAdminCoverageClient
{
    Task<AdminCoverageMatrixDto> GetAdminCoverageAsync(CancellationToken cancellationToken = default);
    Task<AdminCoverageMatrixDto> GetAdminCoverageAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminCoverageAsync(cancellationToken);
}
