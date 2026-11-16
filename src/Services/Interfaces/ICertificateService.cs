using System.Text;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface ICertificateService
{
    Task<IReadOnlyList<CertificateCatalog>> GetCatalogAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<CertificateBlueprintPresetDto> GetOfficialPresets();

    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, CancellationToken cancellationToken = default);

    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(CancellationToken cancellationToken = default);

    Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a certificate from the catalogue. A certificate that has been studied is never destroyed:
    /// its attempts, ledger rows and placements are a learner's history, so it is deactivated instead and
    /// a learner already on it is moved to another active certificate first.
    /// </summary>
    Task<CertificateRemovalResultDto> RemoveCertificateAsync(int certId, CancellationToken cancellationToken = default);
}
