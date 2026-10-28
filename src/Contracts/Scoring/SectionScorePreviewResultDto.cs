using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SectionScorePreviewResultDto(
    int? SectionId,
    string? SectionCode,
    decimal RawScore,
    decimal ScoreWeight,
    decimal ScaledScore);
