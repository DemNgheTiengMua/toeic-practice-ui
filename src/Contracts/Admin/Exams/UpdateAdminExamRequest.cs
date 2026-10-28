using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record UpdateAdminExamRequest(
    int CertId,
    string Name,
    int DurationMinutes,
    ExamPurposeDto Purpose,
    ExamStatusDto Status,
    string? Description = null,
    string? SourceName = null,
    string? InternalNote = null,
    int? QuestionCount = null);
