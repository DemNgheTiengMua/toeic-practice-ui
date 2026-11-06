namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<AdminCoverageMatrixDto> GetAdminCoverageAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminCoverageMatrixDto>("api/admin/coverage", bearerToken, cancellationToken);

    public Task<AdminCoverageMatrixDto> GetAdminCoverageAsync(CancellationToken cancellationToken = default) =>
        GetAdminCoverageAsync(string.Empty, cancellationToken);
}
