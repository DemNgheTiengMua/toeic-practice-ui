using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>
/// #152: CK_Questions_ReferenceKind — what a spoken question's sample answer text is. The author
/// declares it, so a scripted question is not mistaken for a picture caption merely because its
/// group carries an image.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferenceKindDto>))]
public enum ReferenceKindDto
{
    [JsonStringEnumMemberName("script")] Script,
    [JsonStringEnumMemberName("image_desc")] ImageDesc,
}
