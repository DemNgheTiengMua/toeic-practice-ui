using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record CreateAdminExamRequest(
    int CertId,
    string Name,
    int DurationMinutes,
    ExamPurposeDto Purpose,
    // #157: no Status. Transition to published is POST /{id}/publish and nothing else, so the
    // server never reads a status off a create request; the exam is always a draft.
    string? Description = null,
    string? SourceName = null,
    string? InternalNote = null,
    int? QuestionCount = null);
