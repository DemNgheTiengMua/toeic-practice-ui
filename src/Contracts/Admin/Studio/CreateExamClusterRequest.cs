using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record CreateExamClusterRequest(
    [property: Range(1, int.MaxValue, ErrorMessage = "Section is required.")] int SectionId,
    string? Passage = null,
    string? Transcript = null,
    string? AudioPath = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    [property: Range(1, 50, ErrorMessage = "Question count must be between 1 and 50.")] int QuestionCount = 3,
    [property: Range(1, int.MaxValue, ErrorMessage = "Unit number must be at least 1.")] int UnitNumber = 1,
    // #229 / D1: the level every question in the new cluster is authored at. Required by the service -
    // a cluster is content, and content with no level is the defect this ticket removes. It is a code
    // rather than an enum so a bad value is a sentence, not a deserialisation failure.
    [property: Required(AllowEmptyStrings = false, ErrorMessage = "CEFR level is required.")] string? CefrLevelCode = null);
