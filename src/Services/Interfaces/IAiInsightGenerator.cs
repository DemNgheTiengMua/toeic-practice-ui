using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public interface IAiInsightGenerator
{
    Task<AiInsightResult> GenerateInsightAsync(AiInsightGenerationRequest request, CancellationToken cancellationToken = default);
}
