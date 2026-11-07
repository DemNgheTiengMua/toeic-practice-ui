using System;
using System.Collections.Generic;
using System.Globalization;

namespace BusinessObjects;

/// <summary>
/// #197: gate four's rule names, in the same shape as gates one to three. The level rules are here
/// because they judge a classification that has already been made; the call itself is
/// <c>Services.GateFourIngestion</c>, so nothing in this file reaches a provider.
/// </summary>
public static class GateFourRules
{
    /// <summary>D17: an independent classifier predicted a band other than the authored one.</summary>
    public const string LevelDisagreement = "gate4.level.disagreement";

    /// <summary>D17: no classification was obtained, so the rule about it was never applied.</summary>
    public const string ClassifierUnavailable = "gate4.level.unavailable";

    /// <summary>D17: the three-family rule - the classifier is the family that wrote the item.</summary>
    public const string ClassifierNotIndependent = "gate4.level.independent";

    /// <summary>CNT-12: two siblings' evidence spans intersect over 0.15 of their union.</summary>
    public const string SpanOverlap = "gate4.span.overlap";

    /// <summary>
    /// CNT-12's whole-passage exception could not be settled, because the stimulus length is unknown.
    /// Not a refusal of the bundle's content: a refusal because the check did not run.
    /// </summary>
    public const string GistExemptionUnsettled = "gate4.span.gist_unsettled";

    /// <summary>Two spans are measured in different units, so their overlap is not a number.</summary>
    public const string SpanUnit = "gate4.span.unit";

    /// <summary>A span whose end is not after its start measures nothing.</summary>
    public const string SpanShape = "gate4.span.shape";

    /// <summary>CNT-10: a child's evidence begins before its predecessor's evidence ends.</summary>
    public const string AudioOrder = "gate4.audio.order";

    /// <summary>CNT-10: adjacent detail or inference spans are closer than 8.0 seconds.</summary>
    public const string AudioGap = "gate4.audio.gap";

    /// <summary>A listening bundle's spans are seconds. Character offsets cannot be read as one.</summary>
    public const string AudioUnit = "gate4.audio.unit";

    /// <summary>The bundle's sequence indices are not 1..n, so its adjacency is not a fact.</summary>
    public const string AudioSequence = "gate4.audio.sequence";

    /// <summary>D17: a ladder segment does not rise.</summary>
    public const string LadderMonotonic = "gate4.ladder.monotonic";

    /// <summary>D17: a ladder segment rises by less than the ladder's own minimum separation.</summary>
    public const string LadderSeparation = "gate4.ladder.separation";

    /// <summary>D17: the verbose trap scored above the ladder's own ceiling.</summary>
    public const string LadderTrapCeiling = "gate4.ladder.trap";

    /// <summary>The grader did not return a usable score, so nothing was calibrated.</summary>
    public const string LadderNotPlotted = "gate4.ladder.not_plotted";

    /// <summary>The seven-response ladder was not supplied in full, so it was not run.</summary>
    public const string LadderIncomplete = "gate4.ladder.incomplete";
}

/// <summary>#197: one rule broken, named by code and by sentence.</summary>
public sealed record GateFourRefusal(string Rule, string Message);

/// <summary>
/// #197: what an independent classifier said about a text. <b>Unavailable is a case, not a null</b> -
/// D17's rule is that the classifier predicts the authored band, and a classifier that never ran
/// cannot satisfy it. Modelling "we did not check" as a nullable would let a caller default to passing.
/// </summary>
public abstract record GateFourClassification
{
    private GateFourClassification()
    {
    }

    /// <summary>
    /// The band the classifier chose. <paramref name="ModelFamily"/> is the declaration the
    /// independence check reads; it is a claim by the operator, not something the wire provider
    /// proves - the same limit gate three states about its verifiers.
    /// </summary>
    public sealed record Predicted(string ModelFamily, string ModelName, CefrLevel Level) : GateFourClassification;

    /// <summary>Why no prediction was obtained - a missing credential, an unreadable answer.</summary>
    public sealed record Unavailable(string Reason) : GateFourClassification;
}

/// <summary>
/// #197: one child's evidence span, as CNT-11 stores it. <see cref="Id"/> is only used to name the
/// pair in a refusal; a caller judging an unsaved bundle may leave it null and the refusal falls back
/// to the position in the bundle.
/// </summary>
public sealed record GateFourSpan(int? Id, string Topic, int Start, int End, string Unit = "char");

/// <summary>
/// #197: one child of an audio bundle. <see cref="Sequence"/> is CNT-10's <c>sequence_index</c>, the
/// order the items are served in, which is not necessarily the order of their evidence.
/// </summary>
public sealed record GateFourAudioChild(int Sequence, string Topic, int Start, int End, string Unit = "sec");

/// <summary>
/// #197: gate four - level consistency, sibling span independence, and the audio bundle's order and
/// gap (CNT-10, CNT-12, D17). A pure function: no database, no clock, no network, no model.
/// <para>
/// <b>Three of gate four's four rules.</b> The fourth - the seven-response ladder - is
/// <see cref="Services.RubricCalibrationLadder"/> (#193) and is <em>called</em> by
/// <c>Services.GateFourIngestion</c>, never re-implemented: a second copy of that arithmetic would be
/// a second answer to one question, and the copy that drifted would be the one nothing asserts.
/// The level rule here judges a classification already made, for the same reason - the AI call lives
/// behind the seam and the threshold lives here.
/// </para>
/// <para>
/// <b>What the overlap rule measures, and why.</b> CNT-12's fraction is intersection over
/// <em>union</em>, not over the passage. Dividing by the passage makes a pair of identical ten-word
/// spans on a 600-word passage <c>0.4%</c>, which passes - and those two items measure one sentence
/// twice, which is the inflation the rule exists to prevent. The union is of the two spans, so the
/// fraction is only small when the spans themselves are.
/// </para>
/// </summary>
public static class GateFour
{
    /// <summary>CNT-12 and D0.10: <c>intersection-over-union &lt; 0.15</c>, strictly.</summary>
    public const decimal MaximumSpanOverlap = 0.15m;

    /// <summary>CNT-10 and D0.10: adjacent detail or inference spans are at least 8.0 seconds apart.</summary>
    public const decimal MinimumAudioGapSeconds = 8.0m;

    // ----- level consistency -------------------------------------------------------------------

    /// <summary>
    /// Judges one item's level against a classification of its text.
    /// <para>
    /// Independence is checked first and alone, as gate three checks families first: when the
    /// classifier is the authoring family, every comparison below is one model agreeing with itself,
    /// and reporting a disagreement as well would bury the one fact the author has to act on.
    /// </para>
    /// </summary>
    public static IReadOnlyList<GateFourRefusal> ReviewLevel(
        string authorFamily,
        CefrLevel authoredLevel,
        GateFourClassification classification)
    {
        ArgumentNullException.ThrowIfNull(classification);

        var refusals = new List<GateFourRefusal>();

        if (classification is GateFourClassification.Unavailable unavailable)
        {
            refusals.Add(new GateFourRefusal(
                GateFourRules.ClassifierUnavailable,
                $"the item's level was not checked ({unavailable.Reason}). D17 requires an independent "
                + "classifier to predict the authored band, and a check that did not run cannot pass."));
            return refusals;
        }

        var predicted = (GateFourClassification.Predicted)classification;
        var family = predicted.ModelFamily?.Trim().ToLowerInvariant() ?? string.Empty;
        var author = authorFamily?.Trim().ToLowerInvariant() ?? string.Empty;

        if (family.Length == 0 || (author.Length > 0 && family == author))
        {
            refusals.Add(new GateFourRefusal(
                GateFourRules.ClassifierNotIndependent,
                family.Length == 0
                    ? "the classifier declares no model family, so it cannot be shown to be independent of the author (D17)."
                    : $"the classifier is '{predicted.ModelFamily}', the family that authored the item. Re-reading its own item shares its own blind spots, so its prediction cannot check the level (D17)."));
            return refusals;
        }

        if (predicted.Level != authoredLevel)
        {
            refusals.Add(new GateFourRefusal(
                GateFourRules.LevelDisagreement,
                $"the item is authored at {authoredLevel.Code} and an independent classifier "
                + $"({predicted.ModelFamily}/{predicted.ModelName}) predicts {predicted.Level.Code}. "
                + "Either the text is not at the level it claims or the item asks nothing that level "
                + "can measure; the authored band is the one to change or defend (D17)."));
        }

        return refusals;
    }

    // ----- sibling span independence -----------------------------------------------------------

    /// <summary>
    /// Judges every pair of siblings' evidence spans. <paramref name="stimulusLength"/> is the passage
    /// (or transcript) length in the spans' own unit; it is needed only for CNT-12's whole-passage gist
    /// exception, so a bundle with no gist item is judged without it.
    /// <para>
    /// <b>Every pair, not adjacent pairs.</b> Local independence is a statement about the bundle: the
    /// first and third children measuring one sentence is the same inflation as two neighbours doing
    /// it, and a loop over sorted neighbours never sees that pair.
    /// </para>
    /// </summary>
    /// <summary>
    /// The pair's overlap, as CNT-12 defines it: intersection over the two spans' union.
    /// <para>
    /// Public because #203's studio shows the figure while the author drags a span, and a screen that
    /// computed its own IoU would eventually disagree with the rule that refuses the save. Units that
    /// differ, or a span whose end is not after its start, are <c>null</c>: there is no number, and
    /// returning <c>0</c> would paint "independent" over a pair nobody could measure.
    /// </para>
    /// </summary>
    public static decimal? SpanOverlap(GateFourSpan first, GateFourSpan second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (!Same(first.Unit, second.Unit)
            || first.End <= first.Start
            || second.End <= second.Start)
        {
            return null;
        }

        var intersection = Math.Min(first.End, second.End) - Math.Max(first.Start, second.Start);
        if (intersection <= 0)
        {
            return 0m;
        }

        var union = (first.End - first.Start) + (second.End - second.Start) - intersection;
        return (decimal)intersection / union;
    }

    /// <summary>
    /// CNT-10's gap: the seconds between the end of the earlier item's evidence and the start of the
    /// later one - the time the learner has before the next cue is spoken. Public for the same reason
    /// as <see cref="SpanOverlap"/>: the studio's gap badge and the rule that refuses a short gap must
    /// be one number.
    /// </summary>
    public static decimal GapSeconds(GateFourAudioChild earlier, GateFourAudioChild later)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(later);
        return later.Start - earlier.End;
    }

    public static IReadOnlyList<GateFourRefusal> ReviewSpans(
        IReadOnlyList<GateFourSpan> spans,
        int? stimulusLength)
    {
        ArgumentNullException.ThrowIfNull(spans);

        var refusals = new List<GateFourRefusal>();

        if (spans.Count < 2)
        {
            return refusals;
        }

        for (var i = 0; i < spans.Count; i++)
        {
            for (var j = i + 1; j < spans.Count; j++)
            {
                var first = spans[i];
                var second = spans[j];

                if (!Same(first.Unit, second.Unit))
                {
                    refusals.Add(new GateFourRefusal(
                        GateFourRules.SpanUnit,
                        $"{Name(i, first)} is measured in '{first.Unit}' and {Name(j, second)} in "
                        + $"'{second.Unit}', so their overlap is not a fraction of anything. CNT-11 makes "
                        + "the unit part of the span; comparing the integers anyway would report a "
                        + "meaningless number (CNT-12)."));
                    continue;
                }

                if (first.End <= first.Start || second.End <= second.Start)
                {
                    refusals.Add(new GateFourRefusal(
                        GateFourRules.SpanShape,
                        $"{Name(i, first)} spans {first.Start}-{first.End} and {Name(j, second)} spans "
                        + $"{second.Start}-{second.End}; a span whose end is not after its start measures "
                        + "nothing, so no overlap can be computed for it (CNT-11)."));
                    continue;
                }

                var intersection = Math.Min(first.End, second.End) - Math.Max(first.Start, second.Start);
                if (intersection <= 0)
                {
                    continue;
                }

                var overlap = SpanOverlap(first, second)!.Value;
                if (overlap < MaximumSpanOverlap)
                {
                    continue;
                }

                // CNT-12's exception names R-GIST and a *passage*: "whole-passage" is a property of the
                // span measured against the stimulus's length, so it can only be settled for spans
                // measured in text. A listening bundle's spans are seconds and the schema stores no
                // audio duration, so the exception does not reach them - CNT-10's L-GIST relief is
                // about serving order, not about overlap.
                if (IsTextUnit(first.Unit))
                {
                    if (WholePassageGist(first, stimulusLength) || WholePassageGist(second, stimulusLength))
                    {
                        continue;
                    }

                    // A gist topic whose span is not demonstrably the whole passage is refused by the
                    // rule rather than exempted by its label, and an unknown passage length leaves that
                    // undecided rather than decided in the author's favour.
                    if (stimulusLength is null && (IsGist(first.Topic) || IsGist(second.Topic)))
                    {
                        refusals.Add(new GateFourRefusal(
                            GateFourRules.GistExemptionUnsettled,
                            $"{Name(i, first)} and {Name(j, second)} overlap by {Format(overlap)} and one "
                            + "of them is a gist item, but the passage length is unknown, so whether its "
                            + "span covers the whole passage cannot be decided. CNT-12's exception needs "
                            + "that length; skipping the pair would report an unmeasured bundle as "
                            + "independent."));
                        continue;
                    }
                }

                refusals.Add(new GateFourRefusal(
                    GateFourRules.SpanOverlap,
                    $"{Name(i, first)} and {Name(j, second)} cite evidence spans that overlap by "
                    + $"{Format(overlap)}, at or above CNT-12's {Format(MaximumSpanOverlap)} ceiling. "
                    + "Overlap is intersection over union, measured on the spans and not on the passage. "
                    + "Two items on one stretch of text measure one proficiency twice and inflate the "
                    + "score, which is why this is refused at ingestion rather than hoped for."));
            }
        }

        return refusals;
    }

    // ----- the audio bundle --------------------------------------------------------------------

    /// <summary>
    /// Judges one audio bundle against CNT-10: the children are served in the order their evidence is
    /// heard, the <c>L-GIST</c> child at <c>sequence_index = 1</c> is the sole exception, and adjacent
    /// detail or inference items are at least 8.0 seconds apart.
    /// <para>
    /// <b>The gap is end-to-start.</b> Two spans of thirty seconds that begin thirty-five seconds
    /// apart leave the learner five seconds to answer before the next cue is spoken, so start-to-start
    /// distance is not the quantity CNT-10 bounds.
    /// </para>
    /// </summary>
    public static IReadOnlyList<GateFourRefusal> ReviewAudio(IReadOnlyList<GateFourAudioChild> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        var refusals = new List<GateFourRefusal>();

        if (children.Count < 2)
        {
            return refusals;
        }

        foreach (var child in children)
        {
            if (!Same(child.Unit, "sec") && !Same(child.Unit, "s"))
            {
                refusals.Add(new GateFourRefusal(
                    GateFourRules.AudioUnit,
                    $"item {child.Sequence} ({child.Topic}) stores its evidence span in '{child.Unit}'; a "
                    + "listening bundle's spans are seconds (CNT-11), and character offsets read as "
                    + "seconds would report a gap of hundreds of seconds for a bundle nobody timed."));
                return refusals;
            }
        }

        for (var index = 0; index < children.Count; index++)
        {
            if (children[index].Sequence != index + 1)
            {
                refusals.Add(new GateFourRefusal(
                    GateFourRules.AudioSequence,
                    $"the bundle's sequence indices are not 1-{children.Count} in order "
                    + $"(position {index + 1} carries {children[index].Sequence}). Adjacency is what the "
                    + "gap and the ordering are measured between, and a duplicated or skipped index "
                    + "makes it arbitrary (CNT-10)."));
                return refusals;
            }
        }

        for (var index = 0; index < children.Count - 1; index++)
        {
            var earlier = children[index];
            var later = children[index + 1];

            // CNT-10's one exception: the gist child sits first and summarises the whole recording, so
            // its evidence is not "before" the item that follows it. Only at sequence 1 - a gist item
            // parked later is out of order like any other.
            if (earlier.Sequence == 1 && IsGist(earlier.Topic))
            {
                continue;
            }

            if (later.Start < earlier.End)
            {
                refusals.Add(new GateFourRefusal(
                    GateFourRules.AudioOrder,
                    $"item {later.Sequence} ({later.Topic}) cites {later.Start}-{later.End} s, which "
                    + $"begins before item {earlier.Sequence} ({earlier.Topic}) ends at {earlier.End} s. "
                    + "Children are served in the order their evidence is heard, and audio plays "
                    + "linearly: an item whose evidence has already passed cannot be answered (CNT-10)."));
                continue;
            }

            if (!IsGapped(earlier.Topic) || !IsGapped(later.Topic))
            {
                continue;
            }

            var gap = GapSeconds(earlier, later);
            if (gap < MinimumAudioGapSeconds)
            {
                refusals.Add(new GateFourRefusal(
                    GateFourRules.AudioGap,
                    $"items {earlier.Sequence} ({earlier.Topic}) and {later.Sequence} ({later.Topic}) are "
                    + $"{Format(gap)} s apart, below CNT-10's {Format(MinimumAudioGapSeconds)} s floor. "
                    + "The gap is measured from the end of the earlier evidence to the start of the "
                    + "later, which is the time the learner has to finish answering before the next cue "
                    + "is spoken."));
            }
        }

        return refusals;
    }

    // ----- helpers -----------------------------------------------------------------------------

    /// <summary>
    /// D0.4's gist topics are <c>L-GIST</c> and <c>R-GIST</c>; the suffix is the topic code's own
    /// shape, so a certificate variant cannot quietly fall outside it.
    /// </summary>
    private static bool IsGist(string? topic) =>
        topic?.Trim().ToUpperInvariant().EndsWith("-GIST", StringComparison.Ordinal) == true;

    /// <summary>CNT-10 names "detail or inference" items, so the gap is scoped to those two topics.</summary>
    private static bool IsGapped(string? topic)
    {
        var code = topic?.Trim().ToUpperInvariant();
        return code?.EndsWith("-DET", StringComparison.Ordinal) == true
            || code?.EndsWith("-INF", StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// CNT-12's exception is for a <em>whole-passage</em> gist question: the span must be the gist
    /// topic <b>and</b> cover the stimulus. Exempting on the topic alone accepts a ten-word "gist"
    /// fragment that measures the same sentence its sibling measures.
    /// </summary>
    private static bool WholePassageGist(GateFourSpan span, int? stimulusLength) =>
        stimulusLength is int length
        && IsGist(span.Topic)
        && span.Start <= 0
        && span.End >= length;

    /// <summary>CNT-11's text unit. Anything else is a time, where "whole passage" has no meaning.</summary>
    private static bool IsTextUnit(string? unit) =>
        string.Equals(unit?.Trim(), "char", StringComparison.OrdinalIgnoreCase);

    private static string Name(int index, GateFourSpan span) =>
        span.Id is int id
            ? $"item {id}"
            : $"item {index + 1}";

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Format(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
