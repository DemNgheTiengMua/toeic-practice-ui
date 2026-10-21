using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record PlacementCatalogItem(
    int ExamId,
    int CertId,
    string CertCode,
    string Name,
    string? Description,
    int DurationMinutes,
    int QuestionCount,
    bool IsRecommended,
    int? ActivePlacementId);

public sealed record PlacementCatalogView(
    CertificateOption ActiveCert,
    string? CurrentBandCode,
    string? CurrentBandName,
    int? CurrentTotalScore,
    List<PlacementCatalogItem> Exams,
    int? ActivePlacementId,
    int? ActiveAttemptId);

public sealed record PlacementTestInfo(
    int PlacementId,
    int AttemptId,
    int ExamId,
    PlacementStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted);

public sealed record PlacementTakeView(
    int PlacementId,
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    PlacementStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int TotalQuestions,
    int AnsweredCount,
    int FlaggedCount,
    AttemptQuestionInfo CurrentQuestion,
    List<NavigatorItem> Navigator,
    bool IsAutoSubmitted,
    IReadOnlyList<AttemptQuestionInfo>? Questions = null,
    IReadOnlyList<AttemptPhaseInfo>? Phases = null);

public sealed record PlacementSkillScoreInfo(
    int SkillId,
    string SkillCode,
    string SkillName,
    int RawScore,
    int TotalQuestions,
    int ScaledScore,
    string? BandCode);

public sealed record CefrBandInfo(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    bool IsReached,
    bool IsCurrent,
    bool IsTarget = false);

public sealed record PlacementResultView(
    int PlacementId,
    int AttemptId,
    int CertId,
    string CertCode,
    string ExamName,
    DateTimeOffset TakenAt,
    int DurationMinutes,
    int TotalQuestions,
    int TotalScore,
    string ResultBandCode,
    string ResultBandName,
    List<CefrBandInfo> CefrBands,
    List<PlacementSkillScoreInfo> SkillScores,
    string? WeakerSkillName,
    string? BandNote,
    bool IsAutoSubmitted = false,
    int FreeResponseCount = 0,
    int GradedFreeResponseCount = 0,
    int FailedFreeResponseCount = 0,
    bool IsGraded = true);

public sealed record PlacementGradingStatusView(
    int PlacementId,
    int AttemptId,
    bool IsGraded,
    int FreeResponseCount,
    int GradedFreeResponseCount,
    int FailedFreeResponseCount,
    int? TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<PlacementSkillScoreInfo> SkillScores,
    string StageDescriptor);

public sealed record TargetBandOption(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    string? Description,
    bool IsCurrent,
    bool IsTarget);

public sealed record TargetBandView(
    CertificateOption ActiveCert,
    string? CurrentBandCode,
    int? CurrentTotalScore,
    string? CurrentBandName,
    string? TargetBandCode,
    List<CefrBandInfo> CefrBands,
    List<TargetBandOption> AvailableBands);

public sealed record SaveTargetBandResult(
    int UserId,
    int CertId,
    string TargetBandCode,
    DateTimeOffset SetAt);
