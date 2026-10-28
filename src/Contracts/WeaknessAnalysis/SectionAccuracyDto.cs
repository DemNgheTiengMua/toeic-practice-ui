using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SectionAccuracyDto(
    int SectionId,
    string Code,
    string Name,
    int Answered,
    int Correct,
    int AccuracyPercentage,
    bool IsWeakest,
    bool IsStrongest);
