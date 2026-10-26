namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PlacementCatalogViewDto>("api/learner/placement", bearerToken, cancellationToken);

    public Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<StartPlacementTestRequest, PlacementTestInfoDto>("api/learner/placement/start", request, bearerToken, cancellationToken);

    public Task<PreflightStatusDto> GetPlacementPreflightStatusAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PreflightStatusDto>($"api/learner/placement/take/{placementId}/preflight", bearerToken, cancellationToken);

    public Task<PreflightStatusDto> GetPlacementPreflightStatusAsync(int placementId, CancellationToken cancellationToken = default) =>
        GetPlacementPreflightStatusAsync(placementId, string.Empty, cancellationToken);

    public Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, string bearerToken, CancellationToken cancellationToken = default)
    {
        var url = questionDisplayOrder.HasValue
            ? $"api/learner/placement/take/{placementId}?q={questionDisplayOrder.Value}"
            : $"api/learner/placement/take/{placementId}";
        return GetAuthorizedAsync<PlacementTakeViewDto>(url, bearerToken, cancellationToken);
    }

    public Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SaveAnswerRequest, SaveAnswerResultDto>($"api/learner/placement/take/{placementId}/answers/{questionId}", request, bearerToken, cancellationToken);

    public Task<SavePlacementDraftResponseDto> SavePlacementDraftAsync(int placementId, SavePlacementDraftRequestDto request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SavePlacementDraftRequestDto, SavePlacementDraftResponseDto>($"api/learner/placement/take/{placementId}/draft", request, bearerToken, cancellationToken);

    public Task<PlacementGradingStatusDto> GetPlacementGradingStatusAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PlacementGradingStatusDto>($"api/learner/placement/result/{placementId}/status", bearerToken, cancellationToken);

    public Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SubmitPlacementTestRequest, PlacementResultViewDto>($"api/learner/placement/take/{placementId}/submit", request, bearerToken, cancellationToken);

    public Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PlacementResultViewDto>($"api/learner/placement/result/{placementId}", bearerToken, cancellationToken);

    public Task<TargetBandViewDto> GetTargetBandViewAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<TargetBandViewDto>("api/learner/target-band", bearerToken, cancellationToken);

    public Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SetTargetBandRequest, SetTargetBandResultDto>("api/learner/target-band", request, bearerToken, cancellationToken);

    public Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(CancellationToken cancellationToken = default) =>
        GetPlacementCatalogAsync(string.Empty, cancellationToken);

    public Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, CancellationToken cancellationToken = default) =>
        StartPlacementTestAsync(request, string.Empty, cancellationToken);

    public Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default) =>
        GetPlacementTakeAsync(placementId, questionDisplayOrder, string.Empty, cancellationToken);

    public Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default) =>
        SavePlacementAnswerAsync(placementId, questionId, request, string.Empty, cancellationToken);

    public Task<SavePlacementDraftResponseDto> SavePlacementDraftAsync(int placementId, SavePlacementDraftRequestDto request, CancellationToken cancellationToken = default) =>
        SavePlacementDraftAsync(placementId, request, string.Empty, cancellationToken);

    public Task<PlacementGradingStatusDto> GetPlacementGradingStatusAsync(int placementId, CancellationToken cancellationToken = default) =>
        GetPlacementGradingStatusAsync(placementId, string.Empty, cancellationToken);

    public Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, CancellationToken cancellationToken = default) =>
        SubmitPlacementTestAsync(placementId, request, string.Empty, cancellationToken);

    public Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, CancellationToken cancellationToken = default) =>
        GetPlacementResultAsync(placementId, string.Empty, cancellationToken);

    public Task<TargetBandViewDto> GetTargetBandViewAsync(CancellationToken cancellationToken = default) =>
        GetTargetBandViewAsync(string.Empty, cancellationToken);

    public Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, CancellationToken cancellationToken = default) =>
        SetTargetBandAsync(request, string.Empty, cancellationToken);
}
