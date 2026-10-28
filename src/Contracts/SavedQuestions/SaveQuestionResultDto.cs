using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SaveQuestionResultDto(
    bool IsSaved,
    SavedQuestionSourceDto? Source,
    DateTimeOffset? SavedAt);
