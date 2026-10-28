using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SectionScoreInput(
    int? SectionId,
    string? SectionCode,
    int? SkillId,
    decimal RawScore,
    decimal ScoreWeight = 1.0m,
    int? MaxQuestions = null);
