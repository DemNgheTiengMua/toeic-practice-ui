using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record InsightSectionAccuracy(
    int SectionId,
    string SectionCode,
    string SectionName,
    int Answered,
    int Correct,
    int AccuracyPercentage
);
