using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ExamAuditIssueDto(
    string Code,
    string Severity, // "error" | "warning"
    string Message,
    int? SectionId = null,
    string? SectionCode = null,
    int? GroupId = null,
    int? QuestionId = null,
    string? ElementTarget = null);
