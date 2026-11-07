using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #206 / S-03: one audited cell of a batch.
/// <para>
/// <b>No authored key appears anywhere in this DTO.</b> The queue is a list every reviewer can read at
/// any moment, so a key here would reveal the whole sample at once - the anchoring story 30 forbids,
/// arriving through a projection rather than through a screen. The key leaves the API only through
/// <see cref="IAdminAuditClient.RevealSampleKeyAsync"/>, which refuses until a solve exists.
/// </para>
/// </summary>
public sealed record AuditStratumDto(
    int StratumId,
    int BatchId,
    string TopicCode,
    string CefrLevel,
    string Modality,
    int ItemCount,
    int SampleSize,
    int Sampled,
    int Unsampled,
    string Status,
    string? QuarantineReason,
    DateTimeOffset? DecidedAt,
    IReadOnlyList<AuditSampleDto> Items);

/// <summary>#206: one drawn item, without its key.</summary>
public sealed record AuditSampleDto(
    int SampleItemId,
    int ItemId,
    string Stem,
    string OptionA,
    string OptionB,
    string OptionC,
    string OptionD,
    string? SolvedLabel,
    DateTimeOffset? SolvedAt,
    DateTimeOffset? KeyRevealedAt,
    bool KeyRevealed,
    string? Verdict);

/// <summary>#206: the authored key, returned only after a solve is recorded.</summary>
public sealed record AuditKeyRevealDto(
    int SampleItemId,
    int ItemId,
    string KeyLabel,
    string? Explanation,
    string? SolvedLabel,
    DateTimeOffset RevealedAt);

/// <summary>
/// #206: the queue for one batch. <see cref="SampledTotal"/> and <see cref="UnsampledTotal"/> are both
/// here because the promotion sentence states both numbers.
/// </summary>
public sealed record AuditQueueDto(
    int BatchId,
    string SourceName,
    string Status,
    IReadOnlyList<AuditStratumDto> Strata,
    int SampledTotal,
    int UnsampledTotal);

/// <summary>
/// #206: what a decision moved. <see cref="SampledPromoted"/> beside <see cref="UnsampledPromoted"/> is
/// how a caller sees that a 2-item sample moved 20 items.
/// </summary>
public sealed record AuditDecisionDto(
    int StratumId,
    string Status,
    string? QuarantineReason,
    int SampledPromoted,
    int UnsampledPromoted,
    int Quarantined);

/// <summary>#206: a reviewer's solve, posted before the key is readable.</summary>
public sealed record AuditSolveRequest(string? Label);

/// <summary>#206: a reviewer's verdict. <c>correct</c>, <c>key_error</c> or <c>non_key_defect</c>.</summary>
public sealed record AuditVerdictRequest(string? Verdict);
