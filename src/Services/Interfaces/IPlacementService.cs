using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public interface IPlacementService
{
    Task<PlacementCatalogView> GetPlacementCatalogAsync(int userId, CancellationToken cancellationToken = default);

    Task<PlacementTestInfo> StartPlacementTestAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<PreflightStatusView> GetPlacementPreflightStatusAsync(int userId, int placementId, CancellationToken cancellationToken = default);

    Task<PlacementTakeView> GetPlacementTakeAsync(int userId, int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default);

    Task<SaveAttemptAnswerResult> SaveAnswerAsync(int userId, int placementId, int questionId, int? selectedOptionId, bool isFlagged, CancellationToken cancellationToken = default, string? responseText = null, string? selectedOptionIdsJson = null);

    Task<SaveAttemptDraftResult> SaveDraftAsync(int userId, int placementId, int questionId, string? responseText, CancellationToken cancellationToken = default);

    Task<PlacementGradingStatusView> GetGradingStatusAsync(int userId, int placementId, CancellationToken cancellationToken = default);

    Task<PlacementResultView> SubmitPlacementTestAsync(int userId, int placementId, bool isAutoSubmit, CancellationToken cancellationToken = default);

    Task<PlacementResultView> GetPlacementResultAsync(int userId, int placementId, CancellationToken cancellationToken = default);

    Task<TargetBandView> GetTargetBandViewAsync(int userId, CancellationToken cancellationToken = default);

    Task<SaveTargetBandResult> SetTargetBandAsync(int userId, string targetBandCode, CancellationToken cancellationToken = default);
}
