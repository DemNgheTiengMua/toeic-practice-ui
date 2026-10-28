using System;
using System.Collections.Generic;

namespace Contracts;

public record UploadSpeakingAudioResponse(
    int FreeResponseId,
    int QuestionId,
    string AudioPath,
    int? DurationMs,
    DateTimeOffset SavedAt
);

