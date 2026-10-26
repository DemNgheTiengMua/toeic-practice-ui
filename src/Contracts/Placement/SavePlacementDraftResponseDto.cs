using System;

namespace Contracts;

public sealed record SavePlacementDraftResponseDto(
    int FreeResponseId,
    int QuestionId,
    DateTimeOffset SavedAt,
    int WordCount);
