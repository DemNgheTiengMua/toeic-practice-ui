namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PlacementCatalogViewDto>("api/learner/placement", bearerToken, cancellationToken);

    public Task<PlacementPreflightViewDto> GetPlacementGateAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PlacementPreflightViewDto>("api/learner/placement/preflight", bearerToken, cancellationToken);

    public Task<PlacementPreflightViewDto> GetPlacementGateAsync(CancellationToken cancellationToken = default) =>
        GetPlacementGateAsync(string.Empty, cancellationToken);

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

    // #217 / PLC-35: a free retry of the failed gradings - no ledger row, one re-queued pass.
    public Task<PlacementRetryResultDto> RetryPlacementGradingAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<PlacementRetryResultDto>($"api/learner/placement/result/{placementId}/retry", bearerToken, cancellationToken);

    public Task<PlacementRetryResultDto> RetryPlacementGradingAsync(int placementId, CancellationToken cancellationToken = default) =>
        RetryPlacementGradingAsync(placementId, string.Empty, cancellationToken);

    public Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SubmitPlacementTestRequest, PlacementResultViewDto>($"api/learner/placement/take/{placementId}/submit", request, bearerToken, cancellationToken);

    public Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PlacementResultViewDto>($"api/learner/placement/result/{placementId}", bearerToken, cancellationToken);

    public Task<TargetBandViewDto> GetTargetBandViewAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<TargetBandViewDto>("api/learner/target-band", bearerToken, cancellationToken);

    public Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SetTargetBandRequest, SetTargetBandResultDto>("api/learner/target-band", request, bearerToken, cancellationToken);

    public Task<SetGoalResultDto> SetGoalAsync(SetGoalRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SetGoalRequest, SetGoalResultDto>("api/learner/goal", request, bearerToken, cancellationToken);

    public Task<SetGoalResultDto> SetGoalAsync(SetGoalRequest request, CancellationToken cancellationToken = default) =>
        SetGoalAsync(request, string.Empty, cancellationToken);

    public Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(CancellationToken cancellationToken = default) =>
        GetPlacementCatalogAsync(string.Empty, cancellationToken);

    public Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, CancellationToken cancellationToken = default) =>
        StartPlacementTestAsync(request, string.Empty, cancellationToken);

    public Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default) =>
        GetPlacementTakeAsync(placementId, questionDisplayOrder, string.Empty, cancellationToken);

    // #216 / PLC-14, PLC-15.
    public Task<PlacementResumeViewDto> GetPlacementResumeAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PlacementResumeViewDto>($"api/learner/placement/take/{placementId}/resume", bearerToken, cancellationToken);

    public Task<PlacementResumeViewDto> GetPlacementResumeAsync(int placementId, CancellationToken cancellationToken = default) =>
        GetPlacementResumeAsync(placementId, string.Empty, cancellationToken);

    public Task AbandonPlacementAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedNoContentAsync($"api/learner/placement/take/{placementId}/abandon", bearerToken, cancellationToken);

    public Task AbandonPlacementAsync(int placementId, CancellationToken cancellationToken = default) =>
        AbandonPlacementAsync(placementId, string.Empty, cancellationToken);

    public Task<RecordingDecisionDto> RecordPlacementRecordingInterruptionAsync(int placementId, RecordingInterruptionRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<RecordingInterruptionRequest, RecordingDecisionDto>(
            $"api/learner/placement/take/{placementId}/recording-interruptions", request, bearerToken, cancellationToken);

    public Task<RecordingDecisionDto> RecordPlacementRecordingInterruptionAsync(int placementId, RecordingInterruptionRequest request, CancellationToken cancellationToken = default) =>
        RecordPlacementRecordingInterruptionAsync(placementId, request, string.Empty, cancellationToken);

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

    public Task<CalibrationViewDto> GetCalibrationAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<CalibrationViewDto>("api/learner/calibration", bearerToken, cancellationToken);

    public Task<CalibrationViewDto> GetCalibrationAsync(CancellationToken cancellationToken = default) =>
        GetCalibrationAsync(string.Empty, cancellationToken);

    public Task<CalibrationDecisionResultDto> DecideCalibrationAsync(CalibrationDecisionRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<CalibrationDecisionRequest, CalibrationDecisionResultDto>(
            "api/learner/calibration/decision", request, bearerToken, cancellationToken);

    public Task<CalibrationDecisionResultDto> DecideCalibrationAsync(CalibrationDecisionRequest request, CancellationToken cancellationToken = default) =>
        DecideCalibrationAsync(request, string.Empty, cancellationToken);
}
