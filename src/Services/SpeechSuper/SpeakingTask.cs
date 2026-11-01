using BusinessObjects;

namespace Services;

/// <summary>
/// #152: how a spoken free response is graded, decided from the question's own authored data — never
/// from the certificate's name and never from whether its group happens to carry a picture.
/// <para>
/// <b>Script</b>: the question declares <c>ReferenceKind = 'script'</c>, and its
/// <c>SampleAnswerOrImageDesc</c> is the text to read aloud. It travels as <c>refText</c>, so
/// pronunciation is measured against words the learner was actually asked to say (and the take page
/// shows the same words).
/// </para>
/// <para>
/// <b>ImageDesc</b>: the question declares <c>ReferenceKind = 'image_desc'</c>, and the text
/// describes the picture the question shows. It is context for relevance — never a pronunciation
/// reference.
/// </para>
/// <para>
/// <b>Unscripted</b>: no declared reference. <c>refText</c> is empty and the prompt travels as
/// <c>questionPrompt</c> for topic context only. A cue card is a prompt, not a script, and a
/// question whose kind nobody recorded is unscripted: guessing turns a logo into an instruction.
/// </para>
/// </summary>
public static class SpeakingTask
{
    /// <summary>
    /// The reference this question declares, and what it is. The kind is a question-level fact, so
    /// a scripted question whose group also shows a chart keeps its script; text with no declared
    /// kind is not used at all, because nothing says whether it is a script or a caption.
    /// </summary>
    public static (string? Text, bool DescribesImage) ResolveReference(string? text, string? referenceKind)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, false);

        var kind = EnumCodes.TryParse<ReferenceKind>(referenceKind, out var k) ? k : ReferenceKind.ImageDesc;

        return (text.Trim(), kind == ReferenceKind.ImageDesc);
    }

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
