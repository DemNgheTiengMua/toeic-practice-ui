using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ResolveRecommendationResultDto(
    int RecId,
    bool IsResolved);
