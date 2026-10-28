using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SkillScoreInput(
    int? SkillId,
    string? SkillCode,
    decimal? RawScore,
    IReadOnlyList<SectionScoreInput>? Sections = null);
