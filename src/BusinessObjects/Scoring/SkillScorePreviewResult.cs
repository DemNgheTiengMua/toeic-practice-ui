using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SkillScorePreviewResult(
    int? SkillId,
    string? SkillCode,
    decimal RawScore,
    int ScaledScore,
    IReadOnlyList<SectionScorePreviewResult>? Sections = null);
