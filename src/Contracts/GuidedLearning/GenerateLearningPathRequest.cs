using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record GenerateLearningPathRequest(
    string? TargetBandCode = null,
    bool ArchiveExisting = false);
