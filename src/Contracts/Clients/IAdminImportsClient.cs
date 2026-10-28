
namespace Contracts;

public interface IAdminImportsClient
{
    Task<AdminImportBatchesDto> GetAdminImportBatchesAsync(int? certId, CancellationToken cancellationToken = default);
    Task<AdminImportBatchesDto> GetAdminImportBatchesAsync(int? certId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminImportBatchesAsync(certId, cancellationToken);
    Task<AdminImportBatchDetailDto> GetAdminImportBatchAsync(int batchId, CancellationToken cancellationToken = default);
    Task<AdminImportBatchDetailDto> GetAdminImportBatchAsync(int batchId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminImportBatchAsync(batchId, cancellationToken);
    Task<AdminImportResultDto> UploadAdminImportAsync(int certId, Stream content, string fileName, CancellationToken cancellationToken = default);
    Task<AdminImportResultDto> UploadAdminImportAsync(int certId, Stream content, string fileName, string bearerToken, CancellationToken cancellationToken = default) => UploadAdminImportAsync(certId, content, fileName, cancellationToken);
}
