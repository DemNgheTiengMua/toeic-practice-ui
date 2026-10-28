using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SectionScoreInputDto(
    int? SectionId,
    string? SectionCode,
    int? SkillId,
    decimal RawScore,
    decimal ScoreWeight = 1.0m,
    int? MaxQuestions = null);
