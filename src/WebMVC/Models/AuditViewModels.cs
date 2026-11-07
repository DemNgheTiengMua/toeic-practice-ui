using System;
using System.Collections.Generic;
using System.Globalization;
using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #206 / S-03: the audit queue for one batch.
/// <para>
/// <b><see cref="PromotionSentence"/> is a property, not a line in the Razor view.</b> The spec calls out
/// that an approved stratum promotes the reviewed <em>and</em> the unsampled remainder, and that an
/// implementer who promotes only what was reviewed stalls the pipeline. A sentence assembled in the view
/// is a sentence that can drift from the counts above it; this one is built from the same numbers the
/// table renders, so the two cannot disagree.
/// </para>
/// </summary>
public sealed record AuditQueueViewModel(
    int BatchId,
    string SourceName,
    string StatusLabel,
    IReadOnlyList<AuditStratumCard> Strata,
    string SampledTotal,
    string UnsampledTotal,
    string PromotionSentence,
    bool CanDecide,
    string? Message,
    string? ErrorMessage);

/// <summary>#206: one stratum, its sample, and the two counts that make the promotion scope visible.</summary>
public sealed record AuditStratumCard(
    int StratumId,
    string TopicCode,
    string CefrLevel,
    string Modality,
    string ItemCount,
    string SampleSize,
    string Sampled,
    string Unsampled,
    string Status,
    string StatusLabel,
    string? QuarantineReasonLabel,
    string? DecidedAt,
    IReadOnlyList<AuditSampleCard> Items,
    bool IsDecidable,
    bool IsPending)
{
    /// <summary>
    /// What the approve button does, in words, beside the button. The trap the ticket names is a button
    /// that reads "approve sample"; this one cannot, because the label names both halves.
    /// </summary>
    public string ApproveLabel =>
        $"Approve all {ItemCount} items in {TopicCode} {CefrLevel} ({Sampled} reviewed, {Unsampled} unreviewed)";
}

/// <summary>
/// #206: one drawn item as the queue renders it before the key is read.
/// <para>
/// No authored key. <see cref="SolvedLabel"/> and <see cref="Verdict"/> are the reviewer's own facts;
/// <see cref="KeyRevealed"/> says whether they have already seen the key, which is what makes the order
/// visible on the screen rather than implied by it.
/// </para>
/// </summary>
public sealed record AuditSampleCard(
    int SampleItemId,
    int ItemId,
    string Stem,
    string OptionA,
    string OptionB,
    string OptionC,
    string OptionD,
    string? SolvedLabel,
    string? Verdict,
    bool KeyRevealed,
    bool IsSolved,
    bool IsJudged,
    string StateLabel);

/// <summary>
/// #206: the reveal response, rendered on the stratum page. It exists so the authored key arrives in one
/// response with the solve that justified it, rather than being a property the page had all along.
/// </summary>
public sealed record AuditKeyRevealViewModel(
    int BatchId,
    int StratumId,
    int SampleItemId,
    int ItemId,
    string KeyLabel,
    string? Explanation,
    string SolvedLabel,
    string RevealedAt,
    string? Message,
    string? ErrorMessage);

/// <summary>
/// #206: DTO to view mapping. Nothing here decides a verdict or a state - it turns a code into a label
/// and a count into a string, because two definitions of "quarantined" would eventually disagree.
/// </summary>
public static class AuditPresentation
{
    public static AuditQueueViewModel FromQueue(
        AuditQueueDto dto, bool canDecide, string? message, string? error)
    {
        var strata = new List<AuditStratumCard>(dto.Strata.Count);
        foreach (var stratum in dto.Strata)
        {
            var items = new List<AuditSampleCard>(stratum.Items.Count);
            foreach (var item in stratum.Items)
            {
                items.Add(ToCard(item));
            }

            var pending = string.Equals(stratum.Status, "pending", StringComparison.Ordinal);
            strata.Add(new AuditStratumCard(
                stratum.StratumId,
                stratum.TopicCode,
                stratum.CefrLevel,
                stratum.Modality,
                Count(stratum.ItemCount),
                Count(stratum.SampleSize),
                Count(stratum.Sampled),
                Count(stratum.Unsampled),
                stratum.Status,
                StatusLabel(stratum.Status),
                ReasonLabel(stratum.QuarantineReason),
                stratum.DecidedAt is { } decided
                    ? decided.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)
                    : null,
                items,
                // A stratum is decidable when it is pending and every drawn item has a verdict. The
                // service refuses anything else, so this only decides whether the button is offered.
                pending && items.Count > 0 && items.TrueForAll(i => i.IsJudged),
                pending));
        }

        return new AuditQueueViewModel(
            dto.BatchId,
            dto.SourceName,
            StatusLabel(dto.Status),
            strata,
            Count(dto.SampledTotal),
            Count(dto.UnsampledTotal),
            PromotionSentence(dto.SampledTotal, dto.UnsampledTotal),
            canDecide,
            message,
            error);
    }

    public static AuditKeyRevealViewModel FromReveal(
        int batchId, int stratumId, AuditKeyRevealDto dto, string? message, string? error) => new(
        batchId,
        stratumId,
        dto.SampleItemId,
        dto.ItemId,
        dto.KeyLabel,
        dto.Explanation,
        dto.SolvedLabel ?? string.Empty,
        dto.RevealedAt.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture),
        message,
        error);

    /// <summary>
    /// The sentence the spec says is most likely to be skipped: approving moves the reviewed sample
    /// <b>and</b> the unsampled remainder together. Stated with both numbers, because "approve the
    /// stratum" without them reads like "approve the sample".
    /// </summary>
    public static string PromotionSentence(int sampled, int unsampled)
    {
        var total = sampled + unsampled;
        if (total == 0)
        {
            return "This batch has no staged stock to audit.";
        }

        if (unsampled == 0)
        {
            return $"Approving a stratum publishes every item in it: all {Count(total)} here were "
                + "reviewed, so there is no unreviewed remainder.";
        }

        return $"Approving a stratum publishes the {Count(sampled)} reviewed items AND the {Count(unsampled)} "
            + $"unsampled items together - {Count(total)} in all. The unreviewed majority is promoted on the "
            + "strength of the sample, which is why the sample is stratified rather than random.";
    }

    private static AuditSampleCard ToCard(AuditSampleDto item) => new(
        item.SampleItemId,
        item.ItemId,
        item.Stem,
        item.OptionA,
        item.OptionB,
        item.OptionC,
        item.OptionD,
        item.SolvedLabel,
        item.Verdict,
        item.KeyRevealed,
        item.SolvedLabel is not null,
        item.Verdict is not null,
        SampleStateLabel(item));

    /// <summary>
    /// The three states a drawn item is in, in words. "Key read" is stated rather than implied: a
    /// reviewer scrolling the queue needs to see which items they can no longer solve blind.
    /// </summary>
    private static string SampleStateLabel(AuditSampleDto item) => (item.SolvedLabel, item.Verdict, item.KeyRevealed) switch
    {
        (null, _, _) => "Not solved",
        (_, not null, _) => "Judged",
        (_, null, false) => "Solved, key not read",
        _ => "Key revealed, awaiting verdict",
    };

    private static string StatusLabel(string status) => status switch
    {
        "pending" => "Awaiting decision",
        "approved" => "Approved, promoted",
        "quarantined" => "Quarantined",
        "done" => "Judged",
        "failed" => "Nothing accepted",
        _ => status,
    };

    private static string? ReasonLabel(string? reason) => reason switch
    {
        null => null,
        "key_error" => "A confirmed key error. One key error quarantines the whole stratum.",
        "non_key_defect_rate" => "More than 5% of the reviewed sample had a non-key defect.",
        _ => reason,
    };

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
