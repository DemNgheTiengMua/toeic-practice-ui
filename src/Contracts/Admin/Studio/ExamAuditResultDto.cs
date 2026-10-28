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
    IReadOnlyList<string> MissingItems);
