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
    /// <summary>#271: "frequently missed", on the topic axis - the same cells as <see cref="Topics"/>,
    /// ordered lowest accuracy first. It was a list of categories on the retired axis.</summary>
    IReadOnlyList<TopicAccuracyDto> FrequentlyMissedTopics,
    IReadOnlyList<RecommendationItemDto> Recommendations,
    SectionAccuracyDto? PriorityPracticeSection,
    /// <summary>#243 / D3: accuracy per <c>(topic, level)</c>, rankable cells first and weakest first.</summary>
    IReadOnlyList<TopicAccuracyDto> Topics,
    /// <summary>#243: the one cell that may be called the weakest, or null when nothing is rankable.</summary>
    TopicAccuracyDto? WeakestTopic);
