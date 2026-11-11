using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record AiInsightGenerationRequest(
    int UserId,
    int CertId,
    string CertName,
    AiInsightScope Scope,
    int? PracticeSessionId,
    int? AttemptId,
    string ActivityTitle,
    int TotalQuestions,
    int AnsweredCount,
    int CorrectCount,
    int? HintUsedCount,
    int? CorrectWithHintCount,
    string? TargetBandCode,
    string? TargetBandName,
    int? ScaledScore,
    string? CurrentBandCode,
    IReadOnlyList<InsightQuestionDetail> Questions,
    IReadOnlyList<InsightSectionAccuracy> SectionAccuracies,
    /// <summary>#271: the learner's accuracy cells, weakest first, as <c>vw_UserTopicAccuracy</c>
    /// reported them. The retired category axis is gone, and with it any figure a screen could compute
    /// differently from the weakness page.</summary>
    IReadOnlyList<InsightTopicAccuracy> TopicAccuracies,
    int AttemptCount = 1,
    bool SimulateFailure = false,
    PreviousInsightSummary? PreviousInsight = null,
    int? LearnerPathId = null,
    IReadOnlyList<ErrorFingerprint>? ErrorFingerprints = null,
    IReadOnlyList<WritingEvidence>? WritingEvidences = null,
    InsightFacts? Facts = null
);
