using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SkillScoreInputDto(
    int? SkillId,
    string? SkillCode,
    decimal? RawScore,
    IReadOnlyList<SectionScoreInputDto>? Sections = null);
