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

    /// <summary>
    /// #213 / story 52: the learner's answer to the preflight's one question — carry over the skills
    /// whose stored result is still valid, or measure everything again. The choice is theirs, not the
    /// system's, so it is a parameter rather than a policy.
    /// </summary>
    Task<PlacementTestInfo> StartPlacementTestAsync(
        int userId, int examId, PlacementReuseChoice reuse, CancellationToken cancellationToken = default);

    /// <summary>
    /// #213 / SCR-PL-01: what the placement will measure, what may be carried over, and whether a
    /// retake is allowed yet. Reads no credit balance — placement is free.
    /// </summary>
    Task<PlacementPreflightView> GetPreflightAsync(int userId, CancellationToken cancellationToken = default);

    Task<PreflightStatusView> GetPlacementPreflightStatusAsync(int userId, int placementId, CancellationToken cancellationToken = default);

    Task<PlacementTakeView> GetPlacementTakeAsync(int userId, int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default);

    /// <summary>
    /// #216 / PLC-14: what a returning learner sees - the start date, which skills are done, the active
    /// stage and how many answers the server holds - before choosing resume or abandon. A read.
    /// </summary>
    Task<PlacementResumeView> GetResumeAsync(int userId, int placementId, CancellationToken cancellationToken = default);

    /// <summary>#216 / PLC-14: explicit abandon. Moves the placement and its attempt to the existing
    /// abandoned status; derives no result.</summary>
    Task AbandonAsync(int userId, int placementId, CancellationToken cancellationToken = default);

    /// <summary>#216 / PLC-15: record one interrupted recording. Idempotent per session token, so a
    /// reload cannot grant a second retry.</summary>
    Task<PlacementRecordingDecision> RecordRecordingInterruptionAsync(
        int userId,
        int placementId,
        int questionId,
        string sessionToken,
        int capturedMs,
        CancellationToken cancellationToken = default);

    Task<SaveAttemptAnswerResult> SaveAnswerAsync(int userId, int placementId, int questionId, int? selectedOptionId, bool isFlagged, CancellationToken cancellationToken = default, string? responseText = null, string? selectedOptionIdsJson = null);

    Task<SaveAttemptDraftResult> SaveDraftAsync(int userId, int placementId, int questionId, string? responseText, CancellationToken cancellationToken = default);

    Task<PlacementGradingStatusView> GetGradingStatusAsync(int userId, int placementId, CancellationToken cancellationToken = default);

    Task<PlacementResultView> SubmitPlacementTestAsync(int userId, int placementId, bool isAutoSubmit, CancellationToken cancellationToken = default);

    Task<PlacementResultView> GetPlacementResultAsync(int userId, int placementId, CancellationToken cancellationToken = default);

    Task<TargetBandView> GetTargetBandViewAsync(int userId, CancellationToken cancellationToken = default);

    Task<SaveTargetBandResult> SetTargetBandAsync(int userId, string targetBandCode, CancellationToken cancellationToken = default);

    /// <summary>#153: a goal with a deadline. <paramref name="currentBandCode"/> null means "use the
    /// band the learner was last assessed at"; <paramref name="targetDate"/> null means "no deadline",
    /// which stores no verdict at all.</summary>
    Task<SaveGoalResult> SetGoalAsync(
        int userId,
        string? currentBandCode,
        string targetBandCode,
        DateOnly? targetDate,
        decimal? weeklyStudyHours,
        CancellationToken cancellationToken = default);

    /// <summary>#153: the stored goal and its verdict, re-derived from the bands so a re-bandered
    /// certificate never shows a stale total.</summary>
    Task<GoalStatus> GetGoalStatusAsync(int userId, CancellationToken cancellationToken = default);
}
