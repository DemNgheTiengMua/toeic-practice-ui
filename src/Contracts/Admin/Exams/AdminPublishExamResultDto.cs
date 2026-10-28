using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminPublishExamResultDto(
    bool Success,
    string Message,
    IReadOnlyList<string> MissingItems);
