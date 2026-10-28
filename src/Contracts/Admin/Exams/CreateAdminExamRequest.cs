using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record CreateAdminExamRequest(
    int CertId,
    string Name,
    int DurationMinutes,
    ExamPurposeDto Purpose,
    ExamStatusDto Status = ExamStatusDto.Draft,
    string? Description = null,
    string? SourceName = null,
    string? InternalNote = null,
    int? QuestionCount = null);
