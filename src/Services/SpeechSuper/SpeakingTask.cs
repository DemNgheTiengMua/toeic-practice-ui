using System;

namespace Services;

/// <summary>
/// #152: how a spoken free response is graded, decided from the question's own authored data — never
/// from the certificate's name.
/// <para>
/// <b>Scripted</b>: the question carries the words to read aloud in <c>SampleAnswerOrImageDesc</c> and
/// shows no picture. Those words travel as <c>refText</c>, so pronunciation is measured against a
/// reference that actually exists (and the take page shows the learner the same words).
/// </para>
/// <para>
/// <b>Unscripted</b>: <c>refText</c> is empty and the prompt travels as <c>questionPrompt</c> for topic
/// context. A picture description, when one exists, joins that prompt because it is the only reference
/// relevance can be judged against; with no reference at all, relevance is left out entirely.
/// </para>
/// A question that declares nothing is unscripted. A cue card is a prompt, not a script: treating a
/// prompt as the reference would measure pronunciation against words nobody was meant to read.
/// </summary>
public static class SpeakingTask
{
    /// <summary>
    /// The reference this question declares. An authored description on a question that shows a picture
    /// describes that picture; the same column on a question with no picture is the script to read.
    /// </summary>
    public static (string? Text, bool DescribesImage) ResolveReference(
        string? sampleAnswerOrImageDesc, bool questionHasImage) =>
        string.IsNullOrWhiteSpace(sampleAnswerOrImageDesc)
            ? (null, false)
            : (sampleAnswerOrImageDesc.Trim(), questionHasImage);

    public static bool IsScripted(string? referenceText, bool referenceDescribesImage) =>
        !string.IsNullOrWhiteSpace(referenceText) && !referenceDescribesImage;

    /// <summary>The reference text to read aloud, or empty for free speech.</summary>
    public static string PronunciationReference(string? referenceText, bool referenceDescribesImage) =>
        IsScripted(referenceText, referenceDescribesImage) ? referenceText!.Trim() : string.Empty;

    /// <summary>The prompt sent to the provider: the stem, and the picture description when there is one.</summary>
    public static string QuestionPrompt(string? stem, string? referenceText, bool referenceDescribesImage)
    {
        var prompt = stem ?? string.Empty;
        return !string.IsNullOrWhiteSpace(referenceText) && referenceDescribesImage
            ? $"{prompt}\n{referenceText.Trim()}"
            : prompt;
    }

    /// <summary>Relevance needs something to be relevant to. No reference, no relevance score.</summary>
    public static bool CanScoreRelevance(string? referenceText) => !string.IsNullOrWhiteSpace(referenceText);
}
