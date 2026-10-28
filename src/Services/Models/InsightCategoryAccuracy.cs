using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record InsightCategoryAccuracy(
    int CategoryId,
    string CategoryCode,
    string CategoryName,
    int Answered,
    int Correct,
    int AccuracyPercentage
);
