using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record CategoryAccuracyDto(
    int CategoryId,
    string Code,
    string Name,
    int Answered,
    int Correct,
    int AccuracyPercentage);
