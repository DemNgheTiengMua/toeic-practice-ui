using Contracts;

namespace Services;

/// <summary>
/// #202 / S-01: reads the coverage matrix. There is no write side: a quota row is content budget a
/// later content ticket populates (D0.5), and no staff action on this screen moves stock.
/// </summary>
public interface IAdminCoverageService
{
    Task<AdminCoverageMatrixDto> GetMatrixAsync(CancellationToken cancellationToken = default);
}
