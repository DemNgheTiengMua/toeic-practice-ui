using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record QuickIngestCommitRequest(
    int SectionId,
    string Mode, // "append" | "replace"
    IReadOnlyList<QuickIngestParsedQuestionDto> Questions);
