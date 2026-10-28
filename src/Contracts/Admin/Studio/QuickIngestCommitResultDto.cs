using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record QuickIngestCommitResultDto(
    bool Success,
    int QuestionsCommitted,
    int GroupsCreated,
    string Message);
