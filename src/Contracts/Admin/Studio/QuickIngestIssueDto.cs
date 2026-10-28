using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record QuickIngestIssueDto(
    int LineNumber,
    string Severity, // "error" | "warning"
    string Message,
    int? QuestionIndex = null);
