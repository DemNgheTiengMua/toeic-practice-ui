using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #205: one batch's summary. <see cref="UploadedByEmail"/> and <see cref="CreatedAt"/> are criterion
/// 4 - "a later review can find who submitted what" - and <see cref="Accepted"/> is the count the list
/// shows so an author knows whether to open it.
/// </summary>
public sealed record IngestionBatchSummaryDto(
    int BatchId,
    string SourceName,
    string Status,
    int UploadedBy,
    string UploadedByEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int Records,
    int Accepted);

public sealed record IngestionBatchListDto(IReadOnlyList<IngestionBatchSummaryDto> Batches);

/// <summary>#205: one record's verdict. <see cref="Rule"/> is null for an accepted record.</summary>
public sealed record IngestionRecordDto(
    int Line,
    int Attempt,
    bool Accepted,
    int? ItemId,
    IReadOnlyList<IngestionRefusalDto> Refusals,
    string? RecordJson)
{
    /// <summary>The sentence the list shows: the first rule that failed, or empty when accepted.</summary>
    public string Message => Refusals.Count == 0 ? string.Empty : Refusals[0].Message;

    /// <summary>The machine code of the first refusal, which the page shows beside the sentence.</summary>
    public string? Rule => Refusals.Count == 0 ? null : Refusals[0].Rule;
}

public sealed record IngestionRefusalDto(string Rule, string Message);

/// <summary>#205: an item the batch staged, and the state it is actually in.</summary>
public sealed record IngestionStagedItemDto(
    int ItemId,
    string TopicCode,
    string CefrLevel,
    string Modality,
    string LifecycleStatus,
    int VersionNumber);

/// <summary>
/// #205: one batch with one row per record and the items those records became.
/// <para>
/// <see cref="Records"/> is keyed by line rather than by verdict, so a re-judged record appears once.
/// </para>
/// </summary>
public sealed record IngestionBatchDetailDto(
    IngestionBatchSummaryDto Batch,
    IReadOnlyList<IngestionRecordDto> Records,
    IReadOnlyList<IngestionStagedItemDto> Staged);

/// <summary>#205: what an upload produced, as the redirect to the batch needs it.</summary>
public sealed record IngestionUploadResultDto(
    int BatchId,
    string SourceName,
    string Skill,
    string Status,
    int Accepted,
    int Rejected,
    int ItemsWritten);
