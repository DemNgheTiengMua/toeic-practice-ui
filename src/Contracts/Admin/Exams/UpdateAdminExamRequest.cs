using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record UpdateAdminExamRequest(
    int CertId,
    string Name,
    int DurationMinutes,
    ExamPurposeDto Purpose,
    // #157: no Status. A metadata update cannot publish, hide or unpublish; that is POST /{id}/publish.
    string? Description = null,
    string? SourceName = null,
    string? InternalNote = null,
    int? QuestionCount = null);
