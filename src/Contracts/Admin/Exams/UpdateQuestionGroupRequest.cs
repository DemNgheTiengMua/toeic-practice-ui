using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record UpdateQuestionGroupRequest(
    [property: Range(1, int.MaxValue, ErrorMessage = "Section is required.")] int SectionId,
    string? Passage = null,
    string? AudioPath = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    string? ImagePath = null,
    string? ImageAlt = null,
    string? Transcript = null,
    [property: Range(0, 3600, ErrorMessage = "Prep seconds must be between 0 and 3,600.")] int? PrepSeconds = null,
    [property: Range(1, 3600, ErrorMessage = "Response seconds must be between 1 and 3,600.")] int? ResponseSeconds = null,
    [property: Range(0, int.MaxValue, ErrorMessage = "Display order must be 0 or greater.")] int DisplayOrder = 0,
    [property: Range(1, int.MaxValue, ErrorMessage = "Unit number must be at least 1.")] int UnitNumber = 1);
