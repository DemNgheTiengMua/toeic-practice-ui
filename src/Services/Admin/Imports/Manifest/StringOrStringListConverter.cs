using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services;

public sealed class StringOrStringListConverter : JsonConverter<List<string>?>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return [];
            }

            var trimmed = str.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(trimmed);
                    if (list != null)
                    {
                        return list.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
                    }
                }
                catch
                {
                }
            }

            return trimmed.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return list;
                }

                if (reader.TokenType == JsonTokenType.String)
                {
                    var item = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        list.Add(item.Trim());
                    }
                }
                else if (reader.TokenType is JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False)
                {
                    using var doc = JsonDocument.ParseValue(ref reader);
                    var text = doc.RootElement.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        list.Add(text.Trim());
                    }
                }
            }

            return list;
        }

        throw new JsonException($"Expected string or array of strings, got {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}
