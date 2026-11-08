using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ExamAuditResultDto(
    bool CanPublish,
    int TotalQuestions,
    int ExpectedQuestions,
    int BlockingErrorCount,
    int WarningCount,
    IReadOnlyList<ExamAuditIssueDto> Issues,
    IReadOnlyList<string> MissingItems,
    // #226 / ADM-12: the six invariants as six rows, so the rail can name the one that failed instead
    // of showing a single "cannot publish". Defaulted so every existing construction still compiles.
    IReadOnlyList<ExamInvariantDto>? Invariants = null);
