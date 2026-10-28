using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record QuickIngestParseResultDto(
    bool Success,
    int TotalQuestions,
    int ValidQuestions,
    int ErrorCount,
    IReadOnlyList<QuickIngestParsedQuestionDto> Questions,
    IReadOnlyList<QuickIngestIssueDto> GlobalIssues);
