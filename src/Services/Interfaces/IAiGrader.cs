using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

public interface IAiGrader
{
    Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default);
}
