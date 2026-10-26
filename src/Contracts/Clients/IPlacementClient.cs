namespace Contracts;

public interface IPlacementClient
{
    Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(CancellationToken cancellationToken = default);
    Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPlacementCatalogAsync(cancellationToken);
    Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, CancellationToken cancellationToken = default);
    Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) => StartPlacementTestAsync(request, cancellationToken);
    Task<PreflightStatusDto> GetPlacementPreflightStatusAsync(int placementId, CancellationToken cancellationToken = default);
    Task<PreflightStatusDto> GetPlacementPreflightStatusAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementPreflightStatusAsync(placementId, cancellationToken);
    Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default);
    Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementTakeAsync(placementId, questionDisplayOrder, cancellationToken);
    Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default);
    Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) => SavePlacementAnswerAsync(placementId, questionId, request, cancellationToken);
    Task<SavePlacementDraftResponseDto> SavePlacementDraftAsync(int placementId, SavePlacementDraftRequestDto request, CancellationToken cancellationToken = default);
    Task<SavePlacementDraftResponseDto> SavePlacementDraftAsync(int placementId, SavePlacementDraftRequestDto request, string bearerToken, CancellationToken cancellationToken = default) => SavePlacementDraftAsync(placementId, request, cancellationToken);
    Task<PlacementGradingStatusDto> GetPlacementGradingStatusAsync(int placementId, CancellationToken cancellationToken = default);
    Task<PlacementGradingStatusDto> GetPlacementGradingStatusAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementGradingStatusAsync(placementId, cancellationToken);
    Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, CancellationToken cancellationToken = default);
    Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) => SubmitPlacementTestAsync(placementId, request, cancellationToken);
    Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, CancellationToken cancellationToken = default);
    Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementResultAsync(placementId, cancellationToken);
    Task<TargetBandViewDto> GetTargetBandViewAsync(CancellationToken cancellationToken = default);
    Task<TargetBandViewDto> GetTargetBandViewAsync(string bearerToken, CancellationToken cancellationToken = default) => GetTargetBandViewAsync(cancellationToken);
    Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, CancellationToken cancellationToken = default);
    Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, string bearerToken, CancellationToken cancellationToken = default) => SetTargetBandAsync(request, cancellationToken);
}
