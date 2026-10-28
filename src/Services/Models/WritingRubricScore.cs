using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record WritingRubricScore(
    string CriterionName,
    decimal Score,
    decimal MaxScore,
    string? Comment
);
