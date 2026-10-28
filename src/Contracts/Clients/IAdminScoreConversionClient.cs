
namespace Contracts;

public interface IAdminScoreConversionClient
{
    Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, CancellationToken cancellationToken = default);
    Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminScoreConversionAsync(certCode, examId, cancellationToken);
    Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, CancellationToken cancellationToken = default);
    Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateAdminScoreConversionAsync(certId, request, cancellationToken);

    Task<ScorePreviewResultDto> CalculateScorePreviewAsync(ScorePreviewRequestDto request, CancellationToken cancellationToken = default);
    Task<ScorePreviewResultDto> CalculateScorePreviewAsync(ScorePreviewRequestDto request, string bearerToken, CancellationToken cancellationToken = default) => CalculateScorePreviewAsync(request, cancellationToken);
}
