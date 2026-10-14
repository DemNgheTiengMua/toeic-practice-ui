using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SectionAccuracy(
    int SectionId,
    string Code,
    string Name,
    int Answered,
    int Correct,
    int AccuracyPercentage,
    bool IsWeakest,
    bool IsStrongest);

public sealed record CategoryAccuracy(
    int CategoryId,
    string Code,
    string Name,
    int Answered,
    int Correct,
    int AccuracyPercentage);

public sealed record RecommendationItem(
    int RecId,
    string Text,
    string Severity,
    string? TargetBandCode,
    string? SkillName,
    DateTimeOffset CreatedAt,
    bool IsResolved);

public sealed record WeaknessAnalysisView(
    CertificateOption Certificate,
    bool HasData,
    int GradedExamCount,
    int CompletedPracticeCount,
    int TotalAnswered,
    int TotalCorrect,
    int OverallAccuracyPercentage,
    IReadOnlyList<SectionAccuracy> Sections,
    IReadOnlyList<CategoryAccuracy> FrequentlyMissedCategories,
    IReadOnlyList<RecommendationItem> Recommendations,
    SectionAccuracy? PriorityPracticeSection);
