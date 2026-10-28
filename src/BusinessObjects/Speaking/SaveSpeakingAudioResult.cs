using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SaveSpeakingAudioResult(
    int FreeResponseId,
    int QuestionId,
    string AudioPath,
    int? DurationMs,
    DateTimeOffset SavedAt
);
