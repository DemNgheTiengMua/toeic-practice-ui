using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #243 / D3: the weakness page's data. Two axes, and the topic one is the product's own
/// (<c>topic × level</c>); the section axis survives because a mock exam is still sat by section.
/// <para>
/// <b><see cref="WeakestTopic"/> is the ranked answer and is null when nothing is rankable.</b> A page
/// that fell back to "the first cell" or "the least-bad cell" would name a weakness from no evidence,
/// so nothing is the honest value and the view says so in words.
/// </para>
/// </summary>
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
    SectionAccuracy? PriorityPracticeSection,
    IReadOnlyList<TopicAccuracy> Topics,
    TopicAccuracy? WeakestTopic);
