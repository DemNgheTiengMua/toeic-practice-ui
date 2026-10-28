using System;
using System.Collections.Generic;

namespace Contracts;

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
