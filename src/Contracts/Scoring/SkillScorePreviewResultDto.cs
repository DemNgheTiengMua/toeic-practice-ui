using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SkillScorePreviewResultDto(
    int? SkillId,
    string? SkillCode,
    decimal RawScore,
    int ScaledScore,
    IReadOnlyList<SectionScorePreviewResultDto>? Sections = null);
