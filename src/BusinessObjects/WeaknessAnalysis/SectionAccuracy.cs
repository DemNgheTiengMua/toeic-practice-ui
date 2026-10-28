using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SectionAccuracy(
    int SectionId,
    string Code,
    string Name,
    int Answered,
    int Correct,
    int AccuracyPercentage,
    bool IsWeakest,
    bool IsStrongest);
