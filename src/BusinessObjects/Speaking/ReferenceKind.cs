using System.Text.Json.Serialization;

namespace BusinessObjects;

/// <summary>
/// #152: CK_Questions_ReferenceKind — what a spoken question's sample answer text is, declared by
/// the author rather than guessed from the group's picture.
/// <para>
/// <b>Script</b>: the text to read aloud. It travels as <c>refText</c>, so pronunciation is measured
/// against something the learner was asked to say.
/// </para>
/// <para>
/// <b>ImageDesc</b>: a description of the picture shown with the question. It is context for a
/// relevance score, never a pronunciation reference.
/// </para>
/// </summary>
public enum ReferenceKind
{
    Script,
    ImageDesc,
}
