
namespace Contracts;

public interface IAdminCertificatesClient
{
    Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminCertificatesAsync(cancellationToken);

    Task<IReadOnlyList<CertificateBlueprintPresetDto>> GetPresetsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CertificateBlueprintPresetDto>> GetPresetsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPresetsAsync(cancellationToken);

    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, CancellationToken cancellationToken = default);
    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, string bearerToken, CancellationToken cancellationToken = default) => GetCertificateBlueprintAsync(certId, cancellationToken);

    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintByCodeAsync(string code, string bearerToken, CancellationToken cancellationToken = default) => GetCertificateBlueprintByCodeAsync(code, cancellationToken);

    Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, CancellationToken cancellationToken = default);
    Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveCertificateWizardAsync(request, cancellationToken);
}
