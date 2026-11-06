using System;

namespace Contracts;

public sealed record SaveQuestionResultDto(
    bool IsSaved,
    SavedQuestionSourceDto? Source,
    DateTimeOffset? SavedAt,
    int? SavedQuestionId = null);
