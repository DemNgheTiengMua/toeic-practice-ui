using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record CreateQuestionGroupRequest(
    int SectionId,
    string? Passage = null,
    string? AudioPath = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    string? ImagePath = null,
    string? ImageAlt = null,
    string? Transcript = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    int DisplayOrder = 0,
    int UnitNumber = 1);
