using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminExamSectionAudioDto(
    int ExamId,
    int SectionId,
    string SectionCode,
    string SectionName,
    string AudioPath,
    int DurationSec,
    long SizeBytes,
    DateTimeOffset UploadedAt);
