using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SectionScorePreviewResult(
    int? SectionId,
    string? SectionCode,
    decimal RawScore,
    decimal ScoreWeight,
    decimal ScaledScore);
