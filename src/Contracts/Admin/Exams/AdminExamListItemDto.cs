using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminExamListItemDto(
    int ExamId,
    int CertId,
    string CertCode,
    string CertName,
    string Name,
    ExamStatusDto Status,
    ExamPurposeDto Purpose,
    int DurationMinutes,
    int? QuestionCount,
    int ActualQuestionCount,
    string AudioStatus,
    int FinishedAttemptCount,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset CreatedAt,
    string? SourceName = null);
