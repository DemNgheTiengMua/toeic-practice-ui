using System;
using System.Collections.Generic;
using Contracts;

namespace Services;

/// <summary>
/// #206: the wire shape of the stratified audit. A thin adapter over
/// <see cref="StratifiedAuditService"/>, which owns every decision; this exists so the endpoints and
/// the MVC client speak DTOs rather than domain records, and so the "no key in a list" rule is applied
/// in one place.
/// </summary>
public interface IAdminAuditService
{
    Task<AuditQueueDto> GetQueueAsync(int batchId, CancellationToken cancellationToken = default);
    Task<AuditQueueDto> DrawAsync(int batchId, int drawnBy, CancellationToken cancellationToken = default);
    Task RecordSolveAsync(int sampleItemId, string label, int solvedBy, CancellationToken cancellationToken = default);
    Task<AuditKeyRevealDto> RevealKeyAsync(int sampleItemId, CancellationToken cancellationToken = default);
    Task RecordVerdictAsync(int sampleItemId, string verdict, int reviewedBy, CancellationToken cancellationToken = default);
    Task<AuditDecisionDto> DecideAsync(int stratumId, int decidedBy, CancellationToken cancellationToken = default);
}

/// <summary>#206: the adapter. <see cref="StratifiedAuditService"/> owns the rules and the arithmetic.</summary>
public sealed class AdminAuditService(StratifiedAuditService audit) : IAdminAuditService
{
    public async Task<AuditQueueDto> GetQueueAsync(int batchId, CancellationToken cancellationToken = default) =>
        ToDto(await audit.GetQueueAsync(batchId, cancellationToken));

    public async Task<AuditQueueDto> DrawAsync(int batchId, int drawnBy, CancellationToken cancellationToken = default) =>
        ToDto(await audit.DrawAsync(batchId, drawnBy, cancellationToken));

    public Task RecordSolveAsync(
        int sampleItemId, string label, int solvedBy, CancellationToken cancellationToken = default) =>
        audit.RecordSolveAsync(sampleItemId, label, solvedBy, cancellationToken);

    public async Task<AuditKeyRevealDto> RevealKeyAsync(int sampleItemId, CancellationToken cancellationToken = default)
    {
        var reveal = await audit.RevealKeyAsync(sampleItemId, cancellationToken);
        return new AuditKeyRevealDto(
            reveal.SampleItemId, reveal.ItemId, reveal.KeyLabel, reveal.Explanation,
            reveal.SolvedLabel, reveal.RevealedAt);
    }

    public Task RecordVerdictAsync(
        int sampleItemId, string verdict, int reviewedBy, CancellationToken cancellationToken = default) =>
        audit.RecordVerdictAsync(sampleItemId, ParseVerdict(verdict), reviewedBy, cancellationToken);

    public async Task<AuditDecisionDto> DecideAsync(
        int stratumId, int decidedBy, CancellationToken cancellationToken = default)
    {
        var decision = await audit.DecideAsync(stratumId, decidedBy, cancellationToken);
        return new AuditDecisionDto(
            decision.StratumId, decision.Status, decision.QuarantineReason,
            decision.SampledPromoted, decision.UnsampledPromoted, decision.Quarantined);
    }

    /// <summary>
    /// The three verdict codes, parsed here so an endpoint cannot pass an enum name through a string
    /// comparison that silently defaults to "non-key defect".
    /// </summary>
    private static AuditVerdict ParseVerdict(string verdict) => verdict?.Trim().ToLowerInvariant() switch
    {
        "correct" => AuditVerdict.Correct,
        "key_error" => AuditVerdict.KeyError,
        "non_key_defect" => AuditVerdict.NonKeyDefect,
        _ => throw new AdminItemException(
            ApiError.Codes.Validation,
            $"'{verdict}' is not a verdict. Use correct, key_error or non_key_defect.", 400),
    };

    private static AuditQueueDto ToDto(AuditQueue queue)
    {
        var strata = new List<AuditStratumDto>(queue.Strata.Count);
        foreach (var stratum in queue.Strata)
        {
            strata.Add(new AuditStratumDto(
                stratum.StratumId, stratum.BatchId, stratum.TopicCode, stratum.CefrLevel, stratum.Modality,
                stratum.ItemCount, stratum.SampleSize, stratum.Sampled, stratum.Unsampled,
                stratum.Status, stratum.QuarantineReason, stratum.DecidedAt,
                Samples(stratum.Items)));
        }

        return new AuditQueueDto(
            queue.BatchId, queue.SourceName, queue.Status, strata,
            queue.SampledTotal, queue.UnsampledTotal);
    }

    private static List<AuditSampleDto> Samples(IReadOnlyList<AuditSampleRow> items)
    {
        var rows = new List<AuditSampleDto>(items.Count);
        foreach (var item in items)
        {
            rows.Add(new AuditSampleDto(
                item.SampleItemId, item.ItemId, item.Stem,
                item.OptionA, item.OptionB, item.OptionC, item.OptionD,
                item.SolvedLabel, item.SolvedAt, item.KeyRevealedAt, item.KeyRevealed, item.Verdict));
        }

        return rows;
    }
}
