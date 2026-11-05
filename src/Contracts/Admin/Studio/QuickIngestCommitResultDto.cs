using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #189: what one commit did. <see cref="PoolItemsWritten"/> and <see cref="PoolStimuliWritten"/>
/// are the pool half: the legacy rows a paper renders, plus the pool stock the same content became.
/// A commit whose content the pool cannot hold still succeeds and reports zero here, with the reason
/// in <see cref="Message"/> - the legacy import is not allowed to break because the pool is narrower.
/// </summary>
public sealed record QuickIngestCommitResultDto(
    bool Success,
    int QuestionsCommitted,
    int GroupsCreated,
    string Message,
    int PoolItemsWritten = 0,
    int PoolStimuliWritten = 0,
    int PoolItemsBound = 0);
