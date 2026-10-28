using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record CategoryAccuracy(
    int CategoryId,
    string Code,
    string Name,
    int Answered,
    int Correct,
    int AccuracyPercentage);
