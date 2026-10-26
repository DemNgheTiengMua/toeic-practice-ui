using System.Collections.Generic;

namespace Contracts;

public sealed record SittingSyncRequestDto(
    IReadOnlyList<BatchAnswerItemDto>? Answers = null,
    int? LastAudioOffsetMs = null,
    string? CandidateNotesJson = null,
    int? LastQuestionId = null);
