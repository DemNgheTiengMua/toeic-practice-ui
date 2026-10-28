using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record QuickIngestParsedOptionDto(
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale = null);
