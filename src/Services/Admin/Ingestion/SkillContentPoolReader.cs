using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Services;

/// <summary>One item as the file wrote it, with the line its object opens on.</summary>
internal sealed record SkillContentPoolRecord(int Line, SkillContentPoolItem Item);

/// <summary>One stimulus group: the shared text plus its children, each with its own line.</summary>
internal sealed record SkillContentPoolBundle(
    int Line,
    SkillContentPoolGroup Group,
    IReadOnlyList<SkillContentPoolRecord> Items);

/// <summary>The whole file, read without judging any of it.</summary>
internal sealed record SkillContentPoolFile(
    string? SchemaVersion,
    string? Skill,
    IReadOnlyList<SkillContentPoolBundle> Bundles,
    IReadOnlyList<int> ProductiveTaskLines)
{
    /// <summary>Every <em>record</em>'s opening line, in document order - what a refused file reports against.</summary>
    public IReadOnlyList<int> RecordLines
    {
        get
        {
            var lines = new List<int>();
            foreach (var bundle in Bundles)
            {
                foreach (var record in bundle.Items)
                {
                    lines.Add(record.Line);
                }
            }

            return lines;
        }
    }

    /// <summary>
    /// The batch tag the file declares. CNT-03 gives every record one, and the first group's is the
    /// file's: a file whose groups disagree is refused by the pool's own tag column being one value.
    /// </summary>
    public string? BatchTag
    {
        get
        {
            foreach (var bundle in Bundles)
            {
                if (!string.IsNullOrWhiteSpace(bundle.Group.BatchTag))
                {
                    return bundle.Group.BatchTag!.Trim();
                }
            }

            return null;
        }
    }
}

/// <summary>
/// #198: reads the 2.0.0 document and remembers where every record starts.
/// <para>
/// <b>Why a hand-written reader.</b> <see cref="JsonDocument"/> and
/// <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions)"/> both discard byte
/// offsets, so "record 247 is invalid" could not be reported as "line 812". The ticket's whole point
/// is that the author is told which line failed, so the offsets matter as much as the values. The
/// reader therefore streams and deserialises one record at a time, which also keeps a 300-record file
/// from being held twice.
/// </para>
/// <para>
/// Unknown members are skipped, never rejected: the format carries <c>metrics</c>,
/// <c>time_limit_sec</c> and <c>verification_audit.verifier_agreement</c>, and a reader that refused
/// what it does not use would refuse every real file.
/// </para>
/// </summary>
internal static class SkillContentPoolReader
{
    public static SkillContentPoolFile Read(byte[] utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        var scanner = new LineScanner(utf8);
        var options = SkillContentPoolDocument.Json;

        string? schemaVersion = null;
        string? skill = null;
        var bundles = new List<SkillContentPoolBundle>();
        var taskLines = new List<int>();

        Expect(ref reader, JsonTokenType.StartObject);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "schema_version":
                    schemaVersion = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                case "skill":
                    skill = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                case "stimulus_groups" when reader.TokenType == JsonTokenType.StartArray:
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        var line = scanner.LineAt(reader.TokenStartIndex);
                        var (group, items) = ReadGroup(ref reader, scanner, options);
                        bundles.Add(new SkillContentPoolBundle(line, group, items));
                    }

                    break;
                case "productive_tasks" when reader.TokenType == JsonTokenType.StartArray:
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        taskLines.Add(scanner.LineAt(reader.TokenStartIndex));
                        reader.Skip();
                    }

                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new SkillContentPoolFile(schemaVersion, skill, bundles, taskLines);
    }

    /// <summary>
    /// Reads one <c>stimulus_groups[i]</c>. The group's own members are read by hand because
    /// <c>items</c> has to be descended into for its per-record lines; everything the seam does not use
    /// is skipped.
    /// </summary>
    private static (SkillContentPoolGroup Group, IReadOnlyList<SkillContentPoolRecord> Items) ReadGroup(
        ref Utf8JsonReader reader,
        LineScanner scanner,
        JsonSerializerOptions options)
    {
        var group = new SkillContentPoolGroup();
        var items = new List<SkillContentPoolRecord>();

        // Positioned on the group's StartObject by the caller: reading again here would skip its first
        // member and lose stimulus_id.
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "stimulus_id":
                    group.StimulusId = StringOrNull(ref reader);
                    break;
                case "content_tier":
                    group.ContentTier = StringOrNull(ref reader);
                    break;
                case "stimulus_level":
                    group.StimulusLevel = StringOrNull(ref reader);
                    break;
                case "domain_context":
                    group.DomainContext = StringOrNull(ref reader);
                    break;
                case "batch_tag":
                    group.BatchTag = StringOrNull(ref reader);
                    break;
                case "content" when reader.TokenType == JsonTokenType.StartObject:
                    group.Content = ReadContent(ref reader, options);
                    break;
                case "items" when reader.TokenType == JsonTokenType.StartArray:
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        var line = scanner.LineAt(reader.TokenStartIndex);
                        var item = JsonSerializer.Deserialize<SkillContentPoolItem>(ref reader, options)
                            ?? new SkillContentPoolItem();
                        items.Add(new SkillContentPoolRecord(line, item));
                    }

                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return (group, items);
    }

    private static SkillContentPoolContent ReadContent(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var content = new SkillContentPoolContent();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "text_or_transcript":
                    content.TextOrTranscript = StringOrNull(ref reader);
                    break;
                case "image_uri":
                    content.ImageUri = StringOrNull(ref reader);
                    break;
                case "image_alt":
                    content.ImageAlt = StringOrNull(ref reader);
                    break;
                case "audio_asset" when reader.TokenType == JsonTokenType.StartObject:
                    content.AudioAsset = JsonSerializer.Deserialize<SkillContentPoolAudio>(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return content;
    }

    private static string? StringOrNull(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString() : SkipAndNull(ref reader);

    /// <summary>An explicit <c>null</c> or a value of the wrong shape reads as absent, which the seam then refuses by name.</summary>
    private static string? SkipAndNull(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }

    private static void Expect(ref Utf8JsonReader reader, JsonTokenType expected)
    {
        if (!reader.Read() || reader.TokenType != expected)
        {
            throw new JsonException($"expected {expected} but found {reader.TokenType}");
        }
    }

    /// <summary>
    /// Turns a byte offset into a 1-based line number. Offsets arrive in document order, so the scan is
    /// one pass over the file rather than one pass per record.
    /// </summary>
    private sealed class LineScanner(byte[] utf8)
    {
        private int _offset;
        private int _line = 1;

        public int LineAt(long tokenStartIndex)
        {
            var target = tokenStartIndex > int.MaxValue ? utf8.Length : (int)tokenStartIndex;
            while (_offset < target && _offset < utf8.Length)
            {
                if (utf8[_offset] == (byte)'\n')
                {
                    _line++;
                }

                _offset++;
            }

            return _line;
        }
    }
}
