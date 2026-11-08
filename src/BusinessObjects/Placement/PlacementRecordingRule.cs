using System;

namespace BusinessObjects;

/// <summary>What a reload during a placement speaking recording may do next (PLC-15, #216).</summary>
public enum PlacementRecordingOutcome
{
    /// <summary>The first interruption: one retry, timer reset, partial stream discarded.</summary>
    Retry,

    /// <summary>Finalise what was captured and stop. No further retries.</summary>
    Finalise,

    /// <summary>Swap the task for an unseen sibling and stop. No further retries.</summary>
    ReplaceWithSibling,
}

/// <summary>
/// One task's retry state after an interruption. <see cref="Interruptions"/> is the stored count
/// <i>after</i> this one, so the caller can print "retry 1 of 1" honestly.
/// </summary>
public sealed record PlacementRecordingDecision(
    int Interruptions,
    PlacementRecordingOutcome Outcome,
    int? ReplacementQuestionId);

/// <summary>
/// #216 / PLC-15: <b>exactly one recording retry per task, ever.</b>
/// <para>
/// <b>Why the rule is a pure function.</b> The count and the captured duration are both durable
/// server facts (<c>FreeResponses.RecordingInterruptions</c>, <c>DurationMs</c>), so the decision is
/// derived rather than stored. That is what makes a reload during an interruption a read: re-asking
/// the same question with the same stored facts returns the same answer, and nothing can grant a
/// second retry by loading the page again.
/// </para>
/// <para>
/// The input that would pass a weaker rule while being wrong is a counter reset when the sitting is
/// re-entered: the first reload would then grant "one retry" forever. Here <paramref name="interruptionsBefore"/>
/// only ever grows, and the second interruption is the branch that ends retries.
/// </para>
/// </summary>
public static class PlacementRecordingRule
{
    /// <summary>PLC-15: below this capture the partial stream is not worth grading.</summary>
    public const int FinaliseThresholdMs = 15_000;

    /// <summary>PLC-15: one retry, so a task can be interrupted twice in total.</summary>
    public const int MaxInterruptions = 2;

    /// <param name="interruptionsBefore">The stored count before this interruption is recorded.</param>
    /// <param name="capturedMs">How much audio the task holds right now (0 when none).</param>
    /// <param name="siblingQuestionId">
    /// An unseen sibling in the same cell, when the paper has one. Null when it does not: the rule
    /// then finalises rather than inventing a replacement task, because a paper with no sibling can
    /// only either keep the capture or loop forever.
    /// </param>
    /// <remarks>
    /// The returned count <b>saturates at <see cref="MaxInterruptions"/></b>. A third interruption is
    /// still an interruption, but storing a 3 would be storing a third retry the product must never
    /// grant - and the column's CHECK refuses it, so a rule that kept counting would throw instead of
    /// telling the page "no further retries".
    /// </remarks>
    public static PlacementRecordingDecision Decide(
        int interruptionsBefore,
        int capturedMs,
        int? siblingQuestionId)
    {
        if (interruptionsBefore <= 0)
        {
            return new PlacementRecordingDecision(1, PlacementRecordingOutcome.Retry, null);
        }

        var interruptions = Math.Min(interruptionsBefore + 1, MaxInterruptions);
        if (capturedMs >= FinaliseThresholdMs)
        {
            return new PlacementRecordingDecision(interruptions, PlacementRecordingOutcome.Finalise, null);
        }

        return siblingQuestionId is int sibling
            ? new PlacementRecordingDecision(interruptions, PlacementRecordingOutcome.ReplaceWithSibling, sibling)
            : new PlacementRecordingDecision(interruptions, PlacementRecordingOutcome.Finalise, null);
    }
}
