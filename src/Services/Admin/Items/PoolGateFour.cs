using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #197: gate four against the real pool, for the two rules that need rows a bundle does not carry:
/// which items are siblings (they share a <c>StimulusId</c>) and which of them is the gist question.
/// <para>
/// <see cref="GateFour.ReviewSpans"/> and <see cref="GateFour.ReviewAudio"/> stay pure functions; this
/// only assembles their arguments from the pool. Every item's span comes from its <em>latest</em>
/// version, because an item re-versioned to cite different evidence is the span that will be served -
/// the same reading <c>PoolGateOne</c> takes of a sibling's key.
/// </para>
/// <para>
/// A bundle's spans must all be settled before the rule can judge them, and an item with no span is
/// not a zero-length span: CNT-11 requires every child to store one, so the absence is reported rather
/// than read as "starts nowhere, overlaps nothing".
/// </para>
/// </summary>
public static class PoolGateFour
{
    /// <summary>
    /// Judges the evidence spans of one stimulus's children, and - for a listening stimulus - CNT-10's
    /// order and 8.0 s gap.
    /// </summary>
    public static async Task<IReadOnlyList<GateFourRefusal>> ReviewBundleAsync(
        CompanionDbContext db,
        int stimulusId,
        CancellationToken cancellationToken = default)
    {
        var stimulus = await db.Stimuli.AsNoTracking()
            .Where(s => s.StimulusId == stimulusId)
            .Select(s => new { s.StimulusId, s.Passage, s.Transcript, s.AudioPath })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new StimulusNotFoundException(stimulusId);

        // The newest span per item. Two columns and a group-by-in-memory: the same shape PoolGateOne
        // uses, because a correlated first-per-group is translated differently on every provider.
        var versions = await db.ItemVersions.AsNoTracking()
            .Where(v => v.Item.StimulusId == stimulusId)
            .Select(v => new
            {
                v.ItemId,
                v.VersionNumber,
                v.EvidenceStart,
                v.EvidenceEnd,
                v.EvidenceUnit,
                v.Item.Topic.Code,
                v.Item.Modality,
                v.Item.ItemType,
            })
            .ToListAsync(cancellationToken);

        var children = versions
            .GroupBy(v => v.ItemId)
            .Select(siblings => siblings.OrderByDescending(v => v.VersionNumber).First())
            .OrderBy(v => v.ItemId)
            .ToList();

        var refusals = new List<GateFourRefusal>();

        var missing = children
            .Where(c => c.EvidenceStart is null || c.EvidenceEnd is null || string.IsNullOrWhiteSpace(c.EvidenceUnit))
            .ToList();
        if (missing.Count > 0)
        {
            refusals.Add(new GateFourRefusal(
                GateFourRules.SpanShape,
                $"item(s) {string.Join(", ", missing.Select(m => m.ItemId))} on this stimulus store no "
                + "evidence span, so neither their overlap nor their place in the audio can be judged. "
                + "CNT-11 requires every child to carry (start, end, unit); an absent span is not a "
                + "zero-length one."));
            return refusals;
        }

        var spans = children
            .Select(c => new GateFourSpan(c.ItemId, c.Code, c.EvidenceStart!.Value, c.EvidenceEnd!.Value, c.EvidenceUnit!))
            .ToList();

        var listening = children.Any(c => string.Equals(c.Modality, "listening", StringComparison.Ordinal));

        if (listening)
        {
            // CNT-10 governs an audio bundle: order plus the 8.0 s gap. CNT-12's overlap rule is about
            // character spans on a shared passage, and its exception is a *whole-passage* gist item -
            // a quantity a second-based span does not have, because Stimuli stores no audio duration.
            //
            // The gate can only check the gap here. The pool stores no serving order for a bundle -
            // Items and ItemVersions have no sequence index - so the children are placed in the order
            // their evidence is heard and the ordering rule is satisfied by construction. That is a
            // real limit, not a passing check: an authored order that disagrees with the audio cannot
            // be expressed as a row today, so there is nothing to refuse.
            var ordered = children
                .OrderBy(c => c.EvidenceStart!.Value)
                .ThenBy(c => c.ItemId)
                .Select((c, index) => new GateFourAudioChild(
                    index + 1, c.Code, c.EvidenceStart!.Value, c.EvidenceEnd!.Value, c.EvidenceUnit!))
                .ToList();

            refusals.AddRange(GateFour.ReviewAudio(ordered));
            return refusals;
        }

        refusals.AddRange(GateFour.ReviewSpans(spans, stimulus.Passage?.Length));
        return refusals;
    }
}
