using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SaveExamSectionAudioRequest(
    int SectionId,
    string AudioPath,
    int DurationSec,
    long SizeBytes);
