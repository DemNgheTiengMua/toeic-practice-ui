using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record PreviousInsightSummary(
    int InsightId,
    string? Narrative,
    AiInsightTrend? Trend,
    DateTimeOffset CreatedAt
);
