using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SectionAccuracyDto(
    int SectionId,
    string Code,
    string Name,
    int Answered,
    int Correct,
    int AccuracyPercentage,
    bool IsWeakest,
    bool IsStrongest);

public sealed record CategoryAccuracyDto(
    int CategoryId,
    string Code,
    string Name,
    int Answered,
    int Correct,
    int AccuracyPercentage);

public sealed record RecommendationItemDto(
    int RecId,
    string Text,
    RecommendationSeverityDto Severity,
    string? TargetBandCode,
    string? SkillName,
    DateTimeOffset CreatedAt,
    bool IsResolved);

public sealed record WeaknessAnalysisDto(
    CertificateOptionDto Certificate,
    bool HasData,
    int GradedExamCount,
    int CompletedPracticeCount,
    int TotalAnswered,
    int TotalCorrect,
    int OverallAccuracyPercentage,
    IReadOnlyList<SectionAccuracyDto> Sections,
    IReadOnlyList<CategoryAccuracyDto> FrequentlyMissedCategories,
    IReadOnlyList<RecommendationItemDto> Recommendations,
    SectionAccuracyDto? PriorityPracticeSection);

public sealed record ResolveRecommendationResultDto(
    int RecId,
    bool IsResolved);
