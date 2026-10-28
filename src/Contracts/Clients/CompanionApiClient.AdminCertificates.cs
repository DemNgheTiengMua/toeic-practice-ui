using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<AdminCertificateSummaryDto>>("api/admin/certificates", bearerToken, cancellationToken);

    public Task<IReadOnlyList<CertificateBlueprintPresetDto>> GetPresetsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<CertificateBlueprintPresetDto>>("api/admin/certificates/presets", bearerToken, cancellationToken);

    public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<CertificateBlueprintDetailDto?>($"api/admin/certificates/{certId}/blueprint", bearerToken, cancellationToken);

    public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintByCodeAsync(string code, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<CertificateBlueprintDetailDto?>($"api/admin/certificates/by-code/{Uri.EscapeDataString(code)}/blueprint", bearerToken, cancellationToken);

    public Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SaveCertificateWizardRequest, SaveCertificateWizardResultDto>("api/admin/certificates/wizard", request, bearerToken, cancellationToken);

    public Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(CancellationToken cancellationToken = default) =>
        GetAdminCertificatesAsync(string.Empty, cancellationToken);

    public Task<IReadOnlyList<CertificateBlueprintPresetDto>> GetPresetsAsync(CancellationToken cancellationToken = default) =>
        GetPresetsAsync(string.Empty, cancellationToken);

    public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, CancellationToken cancellationToken = default) =>
        GetCertificateBlueprintAsync(certId, string.Empty, cancellationToken);

    public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        GetCertificateBlueprintByCodeAsync(code, string.Empty, cancellationToken);

    public Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, CancellationToken cancellationToken = default) =>
        SaveCertificateWizardAsync(request, string.Empty, cancellationToken);
}
