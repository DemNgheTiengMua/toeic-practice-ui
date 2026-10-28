using System;
using System.Collections.Generic;

namespace BusinessObjects;

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
