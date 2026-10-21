using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record PlacementCatalogItemDto(
    int ExamId,
    int CertId,
    string CertCode,
    string Name,
    string? Description,
    int DurationMinutes,
    int QuestionCount,
    bool IsRecommended,
    int? ActivePlacementId);

public sealed record PlacementCatalogViewDto(
    CertificateOptionDto ActiveCert,
    string? CurrentBandCode,
    string? CurrentBandName,
    int? CurrentTotalScore,
    List<PlacementCatalogItemDto> Exams,
    int? ActivePlacementId,
    int? ActiveAttemptId);

public sealed record StartPlacementTestRequest(int ExamId);

public sealed record PlacementTestInfoDto(
    int PlacementId,
    int AttemptId,
    int ExamId,
    PlacementStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted);

public sealed record PlacementTakeViewDto(
    int PlacementId,
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    PlacementStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int TotalQuestions,
    int AnsweredCount,
    int FlaggedCount,
    AttemptQuestionDto CurrentQuestion,
    IReadOnlyList<NavigatorItemDto> Navigator,
    bool IsAutoSubmitted,
    IReadOnlyList<AttemptQuestionDto>? Questions = null,
    IReadOnlyList<AttemptPhaseDto>? Phases = null);

public sealed record SavePlacementDraftRequestDto(
    int QuestionId,
    string? ResponseText);

public sealed record SavePlacementDraftResponseDto(
    int FreeResponseId,
    int QuestionId,
    DateTimeOffset SavedAt,
    int WordCount);

public sealed record SubmitPlacementTestRequest(
    bool IsAutoSubmit = false);

public sealed record PlacementSkillScoreInfoDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    int RawScore,
    int TotalQuestions,
    int ScaledScore,
    string? BandCode);

public sealed record CefrBandInfoDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    bool IsReached,
    bool IsCurrent,
    bool IsTarget = false);

public sealed record PlacementResultViewDto(
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
    List<CefrBandInfoDto> CefrBands,
    List<PlacementSkillScoreInfoDto> SkillScores,
    string? WeakerSkillName,
    string? BandNote,
    bool IsAutoSubmitted = false,
    int FreeResponseCount = 0,
    int GradedFreeResponseCount = 0,
    int FailedFreeResponseCount = 0,
    bool IsGraded = true);

public sealed record PlacementGradingStatusDto(
    int PlacementId,
    int AttemptId,
    bool IsGraded,
    int FreeResponseCount,
    int GradedFreeResponseCount,
    int FailedFreeResponseCount,
    int? TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<PlacementSkillScoreInfoDto> SkillScores,
    string StageDescriptor);

public sealed record TargetBandOptionDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    string? Description,
    bool IsCurrent,
    bool IsTarget);

public sealed record TargetBandViewDto(
    CertificateOptionDto ActiveCert,
    string? CurrentBandCode,
    int? CurrentTotalScore,
    string? CurrentBandName,
    string? TargetBandCode,
    List<CefrBandInfoDto> CefrBands,
    List<TargetBandOptionDto> AvailableBands);

public sealed record SetTargetBandRequest(
    string TargetBandCode);

public sealed record SetTargetBandResultDto(
    int UserId,
    int CertId,
    string TargetBandCode,
    DateTimeOffset SetAt);
