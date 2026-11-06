using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record CreateExamClusterRequest(
    int SectionId,
    string? Passage = null,
    string? Transcript = null,
    string? AudioPath = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    int QuestionCount = 3,
    int UnitNumber = 1,
    // #229 / D1: the level every question in the new cluster is authored at. Required by the service -
    // a cluster is content, and content with no level is the defect this ticket removes. It is a code
    // rather than an enum so a bad value is a sentence, not a deserialisation failure.
    string? CefrLevelCode = null);
