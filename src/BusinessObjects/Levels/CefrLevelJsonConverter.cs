using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessObjects;

/// <summary>
/// #228 / D1: a level crosses JSON as its code ("B1"), never as its rank and never as a number.
/// Without this, System.Text.Json writes the record's own shape — <c>{"rank":4,"code":"B2",…}</c> —
/// which hands the wire an integer a reader could rebuild a level from, i.e. the second scale #182
/// exists to prevent. Registered on the type itself, so no caller has to remember it.
/// <para>
/// It reads with <see cref="CefrLevel.TryParse"/>, so the reportable floor is accepted here: this is
/// the transport, not a content gate. Every authoring boundary still calls
/// <see cref="CefrLevel.TryParseAuthored"/> itself, because only that boundary knows whether the
/// value it just read is being written or merely reported.
/// </para>
/// </summary>
public sealed class CefrLevelJsonConverter : JsonConverter<CefrLevel>
{
    public override CefrLevel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var code = reader.GetString();

        return CefrLevel.TryParse(code, out var level)
            ? level
            : throw new JsonException(
                $"'{code}' is not a CEFR level. A level crosses the wire as one of "
                + $"{string.Join(", ", CefrLevel.Reportable)}.");
    }

    public override void Write(Utf8JsonWriter writer, CefrLevel value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Code);
}
