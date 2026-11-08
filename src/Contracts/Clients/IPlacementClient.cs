namespace Contracts;

public interface IPlacementClient
{
    Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(CancellationToken cancellationToken = default);
    Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPlacementCatalogAsync(cancellationToken);

    /// <summary>#213 / SCR-PL-01: what will be measured, what may be carried over, and the cooldown.</summary>
    Task<PlacementPreflightViewDto> GetPlacementGateAsync(CancellationToken cancellationToken = default);
    Task<PlacementPreflightViewDto> GetPlacementGateAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPlacementGateAsync(cancellationToken);
    Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, CancellationToken cancellationToken = default);
    Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) => StartPlacementTestAsync(request, cancellationToken);
    Task<PreflightStatusDto> GetPlacementPreflightStatusAsync(int placementId, CancellationToken cancellationToken = default);
    Task<PreflightStatusDto> GetPlacementPreflightStatusAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementPreflightStatusAsync(placementId, cancellationToken);
    Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default);
    Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementTakeAsync(placementId, questionDisplayOrder, cancellationToken);
    /// <summary>#216 / PLC-14: the returning learner's summary, before choosing resume or abandon.</summary>
    Task<PlacementResumeViewDto> GetPlacementResumeAsync(int placementId, CancellationToken cancellationToken = default);
    Task<PlacementResumeViewDto> GetPlacementResumeAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementResumeAsync(placementId, cancellationToken);
    /// <summary>#216 / PLC-14: explicit abandon, the lifecycle act the resume screen offers.</summary>
    Task AbandonPlacementAsync(int placementId, CancellationToken cancellationToken = default);
    Task AbandonPlacementAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => AbandonPlacementAsync(placementId, cancellationToken);
    /// <summary>#216 / PLC-15: one interrupted recording. Idempotent per session token.</summary>
    Task<RecordingDecisionDto> RecordPlacementRecordingInterruptionAsync(int placementId, RecordingInterruptionRequest request, CancellationToken cancellationToken = default);
    Task<RecordingDecisionDto> RecordPlacementRecordingInterruptionAsync(int placementId, RecordingInterruptionRequest request, string bearerToken, CancellationToken cancellationToken = default) => RecordPlacementRecordingInterruptionAsync(placementId, request, cancellationToken);
    Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default);
    Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) => SavePlacementAnswerAsync(placementId, questionId, request, cancellationToken);
    Task<SavePlacementDraftResponseDto> SavePlacementDraftAsync(int placementId, SavePlacementDraftRequestDto request, CancellationToken cancellationToken = default);
    Task<SavePlacementDraftResponseDto> SavePlacementDraftAsync(int placementId, SavePlacementDraftRequestDto request, string bearerToken, CancellationToken cancellationToken = default) => SavePlacementDraftAsync(placementId, request, cancellationToken);
    Task<PlacementGradingStatusDto> GetPlacementGradingStatusAsync(int placementId, CancellationToken cancellationToken = default);
    Task<PlacementGradingStatusDto> GetPlacementGradingStatusAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementGradingStatusAsync(placementId, cancellationToken);

    /// <summary>#217 / PLC-35: re-queue the failed gradings. Free, and no ledger row is written.</summary>
    Task<PlacementRetryResultDto> RetryPlacementGradingAsync(int placementId, CancellationToken cancellationToken = default);
    Task<PlacementRetryResultDto> RetryPlacementGradingAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => RetryPlacementGradingAsync(placementId, cancellationToken);
    Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, CancellationToken cancellationToken = default);
    Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) => SubmitPlacementTestAsync(placementId, request, cancellationToken);
    Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, CancellationToken cancellationToken = default);
    Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementResultAsync(placementId, cancellationToken);
    Task<TargetBandViewDto> GetTargetBandViewAsync(CancellationToken cancellationToken = default);
    Task<TargetBandViewDto> GetTargetBandViewAsync(string bearerToken, CancellationToken cancellationToken = default) => GetTargetBandViewAsync(cancellationToken);
    Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, CancellationToken cancellationToken = default);
    Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, string bearerToken, CancellationToken cancellationToken = default) => SetTargetBandAsync(request, cancellationToken);

    /// <summary>#153: save the whole goal — current band, target band, exam date and weekly pace.</summary>
    Task<SetGoalResultDto> SetGoalAsync(SetGoalRequest request, CancellationToken cancellationToken = default);
    Task<SetGoalResultDto> SetGoalAsync(SetGoalRequest request, string bearerToken, CancellationToken cancellationToken = default) => SetGoalAsync(request, cancellationToken);

    /// <summary>#156: whether practice has diverged from the placement band, and the proposal if so.</summary>
    Task<CalibrationViewDto> GetCalibrationAsync(CancellationToken cancellationToken = default);
    Task<CalibrationViewDto> GetCalibrationAsync(string bearerToken, CancellationToken cancellationToken = default) => GetCalibrationAsync(cancellationToken);

    /// <summary>#156: the learner's answer. True lowers the path's starting band; false keeps it.</summary>
    Task<CalibrationDecisionResultDto> DecideCalibrationAsync(CalibrationDecisionRequest request, CancellationToken cancellationToken = default);
    Task<CalibrationDecisionResultDto> DecideCalibrationAsync(CalibrationDecisionRequest request, string bearerToken, CancellationToken cancellationToken = default) => DecideCalibrationAsync(request, cancellationToken);
}
