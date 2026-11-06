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
/// #195: the two things gate two cannot answer from an item row alone.
/// <list type="number">
/// <item><b>Which text to measure.</b> D0.1 and D0.2 profile a <em>stimulus</em>: a reading item's
/// passage or a listening item's transcript. The item's own stem is the question, not the input, and
/// measuring a 12-word stem against a 220-word B1 envelope refuses every item in the pool. The
/// stimulus is a parent row, so only the database can supply it.</item>
/// <item><b>Which modality column to apply.</b> The <c>Modality</c> column carries four skills and
/// D0.2 has an envelope for two, so the mapping from skill to column is a decision this class makes
/// once rather than at each call site.</item>
/// </list>
/// <para>
/// <see cref="GateTwo.Review"/> stays a pure function. Only the transcription is passed <em>in</em>:
/// the ASR call is an <see cref="IGateTwoTranscriber"/>, because D17's rule is about an independent
/// model and this assembly has no model. A caller that cannot supply one gets
/// <see cref="GateTwoAsr.Unavailable"/> and therefore a refusal, never a pass - which is the one
/// choice in this seam that cannot be left to a default.
/// </para>
/// </summary>
public static class PoolGateTwo
{
    /// <summary>
    /// Judges a stored item's stimulus against its band's linguistic profile, and - for a listening
    /// item with audio - against an independent transcription of that audio.
    /// </summary>
    public static async Task<GateTwoVerdict> ReviewAsync(
        CompanionDbContext db,
        int itemId,
        IGateTwoTranscriber? transcriber = null,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Items.AsNoTracking()
            .Where(i => i.ItemId == itemId)
            .Select(i => new
            {
                i.ItemId,
                i.Modality,
                i.ItemType,
                i.CefrLevel,
                Passage = i.Stimulus == null ? null : i.Stimulus.Passage,
                Transcript = i.Stimulus == null ? null : i.Stimulus.Transcript,
                PromptCopy = i.Stimulus == null ? null : i.Stimulus.PromptCopy,
                AudioPath = i.Stimulus == null ? null : i.Stimulus.AudioPath,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            throw new ItemNotFoundException(itemId);
        }

        return await ReviewContentAsync(
            row.Modality,
            row.ItemType,
            row.CefrLevel,
            row.Passage,
            row.Transcript,
            row.PromptCopy,
            row.AudioPath,
            transcriber,
            cancellationToken);
    }

    /// <summary>
    /// Judges content that is not a row yet, which is what #198's ingestion seam needs: D17 requires the
    /// gates to run <em>before</em> the first write, so a bundle that would be refused leaves no row to
    /// read back. The stored-item overload above loads the row and calls this, so the two cannot
    /// disagree about which text is measured or which modality column applies.
    /// </summary>
    public static async Task<GateTwoVerdict> ReviewContentAsync(
        string modality,
        string itemType,
        CefrLevel band,
        string? passage,
        string? transcript,
        string? promptCopy,
        string? audioPath,
        IGateTwoTranscriber? transcriber = null,
        CancellationToken cancellationToken = default)
    {
        var column = ModalityOf(modality);
        var kind = KindOf(itemType);

        // D0.2 profiles authored text. A listening item is profiled on its transcript, a reading item
        // on its passage, and a productive task on its prompt - which is not profiled at all, so the
        // prompt is passed through only so the verdict can say so.
        var text = column switch
        {
            GateTwoModality.Listening => transcript,
            GateTwoModality.Reading => passage,
            _ => promptCopy,
        };

        var asr = column == GateTwoModality.Listening && !string.IsNullOrWhiteSpace(audioPath)
            ? await TranscribeAsync(transcriber, audioPath!, transcript, cancellationToken)
            : null;

        return GateTwo.Review(new GateTwoItem(text, band, column, kind, asr));
    }

    /// <summary>
    /// The transcription, or the reason there is none. A listening stimulus with <em>no</em> audio
    /// file is not sent to an ASR service: there is nothing to transcribe, and
    /// <see cref="GateTwoRules.MissingScript"/> already refuses an item with no script to check.
    /// </summary>
    private static async Task<GateTwoAsr> TranscribeAsync(
        IGateTwoTranscriber? transcriber,
        string audioPath,
        string? transcript,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return new GateTwoAsr.Unavailable(
                "the stimulus has audio but no written script, so there is nothing to compare the recording against");
        }

        if (transcriber is null)
        {
            return new GateTwoAsr.Unavailable(
                "no independent transcriber is configured, so the recording was not checked against its script");
        }

        try
        {
            // An empty transcription is *silence heard*, not a failure: it must reach the word-error
            // comparison so it is refused as 100%, not reported as "not checked".
            var text = await transcriber.TranscribeAsync(audioPath, cancellationToken);
            return new GateTwoAsr.Transcribed(text ?? string.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A service that is down is "not verified", not "verified". The distinction is the whole
            // reason 0.0% WER is a rule: a verifier that did not run cannot certify anything.
            return new GateTwoAsr.Unavailable($"the transcriber failed: {exception.Message}");
        }
    }

    /// <summary>
    /// D0.2's two columns. Speaking and writing are productive: their column is a learner-output
    /// demand, not an authored-text property, so they are not profiled.
    /// </summary>
    private static GateTwoModality ModalityOf(string modality) => modality switch
    {
        "listening" => GateTwoModality.Listening,
        "reading" => GateTwoModality.Reading,
        "speaking" or "writing" => GateTwoModality.Productive,
        _ => throw new ArgumentException($"'{modality}' is not a skill.", nameof(modality)),
    };

    /// <summary>
    /// D0.4's one standalone type is <c>sentence_cloze</c>: it is bounded at 15-45 words by D0.4
    /// rather than by D0.2's stimulus envelope, and its band's MLTU bound cannot apply to a single
    /// sentence. Every other type carries a stimulus with a D0.2 envelope.
    /// </summary>
    private static GateTwoTextKind KindOf(string itemType) =>
        string.Equals(itemType, "sentence_cloze", StringComparison.Ordinal)
            ? GateTwoTextKind.SentenceSlot
            : GateTwoTextKind.Stimulus;

    /// <summary>
    /// D17's independent transcription. The rule is deliberately not satisfied by
    /// <c>ISpeechSuperClient</c>: SpeechSuper scores a <em>learner's</em> pronunciation against a
    /// reference, which is gate four's subject and would make the script its own judge. A separate
    /// interface is what keeps that distinction visible, and a fake implements it in tests.
    /// </summary>
    public interface IGateTwoTranscriber
    {
        /// <summary>
        /// The words heard in <paramref name="audioPath"/>. An empty string means the recording was
        /// silent, which is a transcription (and fails the comparison), not a failure to check.
        /// </summary>
        Task<string> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default);
    }
}
