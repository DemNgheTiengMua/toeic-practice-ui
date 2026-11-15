using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record UpdateAdminExamRequest(
    [property: Range(1, int.MaxValue, ErrorMessage = "Certificate is required.")] int CertId,
    [property: Required(AllowEmptyStrings = false, ErrorMessage = "Exam name is required."), StringLength(160, MinimumLength = 2, ErrorMessage = "Exam name must be between 2 and 160 characters.")] string Name,
    [property: Range(1, 600, ErrorMessage = "Duration must be between 1 and 600 minutes.")] int DurationMinutes,
    ExamPurposeDto Purpose,
    // #157: no Status. A metadata update cannot publish, hide or unpublish; that is POST /{id}/publish.
    string? Description = null,
    string? SourceName = null,
    string? InternalNote = null,
    [property: Range(1, 500, ErrorMessage = "Question count must be between 1 and 500.")] int? QuestionCount = null);
