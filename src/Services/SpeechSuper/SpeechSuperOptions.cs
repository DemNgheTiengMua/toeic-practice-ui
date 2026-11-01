namespace Services;

public sealed class SpeechSuperOptions
{
    public const string SectionName = "SpeechSuper";

    public string AppKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.speechsuper.com";

    /// <summary>
    /// #152 finding 8, decided: keep the unscripted endpoint for scripted questions too.
    /// <para>
    /// The provider's own feature table is the reason. Scripted analysis returns word, sentence and
    /// fluency/completeness scores but <em>no IELTS Speaking band scores</em>; unscripted analysis is
    /// the only product that returns them. This ticket's whole subject is that a certificate's own
    /// scale must decide the marks, and the certificate rubric is fed from <c>overall</c>,
    /// <c>pronunciation</c>, <c>fluency_coherence</c>, <c>grammar</c> and <c>lexical_resource</c> —
    /// fields only the unscripted endpoint produces. Routing Read Aloud to
    /// <c>sent.eval.promax</c> would replace every criterion with "not measured", which this ticket
    /// made a Failed grading.
    /// </para>
    /// <para>
    /// What the scripted endpoint would add is word-level accuracy against the script. That is a
    /// better pronunciation signal for Read Aloud and it is deliberately deferred: it needs a second
    /// mapping table (word/completeness to a rubric), a second response shape, and a certificate
    /// rubric that names those criteria, so it belongs in its own ticket rather than in a fix for
    /// wrong scales. Recorded as D18 in docs/STATE.md.
    /// </para>
    /// </summary>
    public string CoreType { get; set; } = "speak.eval.pro";
}
