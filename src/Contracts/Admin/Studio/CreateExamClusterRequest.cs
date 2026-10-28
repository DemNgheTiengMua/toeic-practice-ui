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
    int UnitNumber = 1);
