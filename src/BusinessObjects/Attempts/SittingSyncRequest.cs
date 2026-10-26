using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SittingSyncRequest(
    IReadOnlyList<BatchAnswerItem>? Answers = null,
    int? LastAudioOffsetMs = null,
    string? CandidateNotesJson = null,
    int? LastQuestionId = null);
