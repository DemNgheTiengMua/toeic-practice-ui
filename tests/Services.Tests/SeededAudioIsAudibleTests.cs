using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.SqlClient;

namespace Services.Tests;

/// <summary>
/// A learner pressing Play must hear sound for the time the player claims.
/// <para>
/// <b>What shipped instead, measured on 2026-11-15:</b> every seeded listening file was a
/// byte-identical 20,900-byte MP3 holding 1.3 seconds of digital silence (<c>ffmpeg</c> reported
/// <c>max_volume: -91.0 dB</c>), while the stimulus declared segments up to 150 seconds. The player
/// showed <c>00:00 / 00:30</c>, emitted nothing, and claimed "Playback complete" about a second after
/// Play. The audio was a placeholder that nobody had listened to.
/// </para>
/// <para>
/// <b>Why this asserts on the bytes and not on a file size:</b> a "big enough" file passes while
/// holding silence, which is exactly the defect. Digital silence compresses to a handful of distinct
/// byte values (measured 10 across the whole placeholder file); real audio uses most of the byte range
/// (measured 256). The check states that difference rather than a magic size, so re-encoding a real
/// recording cannot fail it and re-shipping a silent one cannot pass.
/// </para>
/// <para>
/// <b>Why the length is measured from the MP3 frames rather than with an audio library:</b> the test
/// project carries no media dependency, and adding one to read a duration would be a heavier change
/// than the check warrants. A CBR MPEG-1 Layer III stream states its duration in its own frame headers,
/// which is enough to answer "is this at least as long as the segment the stimulus declares".
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SeededAudioIsAudibleTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Distinct byte values across a whole file. Digital silence collapses to a few; real audio spreads
    /// across the range. The placeholder measured 10, every replacement measured 256.
    /// </summary>
    private const int MinimumDistinctBytes = 32;

    [Fact]
    public async Task Every_seeded_listening_stimulus_serves_audible_audio_long_enough_for_its_segment()
    {
        var dbName = $"EE364Audio_{Guid.NewGuid():N}"[..26];
        try
        {
            await fixture.CreateFreshDatabaseAsync(dbName);
            await fixture.SqlCmdFileToDatabaseAsync(
                dbName, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));

            var connStr = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = dbName }.ConnectionString;
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Every distinct (audio path, declared segment end) the seed actually asks a learner to hear.
            var segments = new List<(string Path, int? EndMs)>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT DISTINCT s.AudioPath, s.AudioEndMs
                    FROM Stimuli s
                    WHERE s.AudioPath IS NOT NULL
                    ORDER BY s.AudioPath, s.AudioEndMs
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    segments.Add((
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetInt32(1)));
                }
            }

            Assert.NotEmpty(segments);

            var repoRoot = DatabaseFixture.RepoRootPath;
            var failures = new List<string>();

            foreach (var (path, endMs) in segments)
            {
                // "/audio/toeic/x.mp3" is served from the SPA's public directory.
                var relative = path.TrimStart('/');
                if (relative.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                {
                    relative = relative["audio/".Length..];
                }
                var file = Path.Combine(repoRoot, "src", "web", "public", "audio", relative.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(file))
                {
                    failures.Add($"{path} (segment end {endMs?.ToString() ?? "none"}): file not found at {file}");
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(file);
                var distinct = bytes.Distinct().Count();
                if (distinct < MinimumDistinctBytes)
                {
                    failures.Add(
                        $"{path}: {bytes.Length} bytes but only {distinct} distinct byte values — this is silence, not audio");
                    continue;
                }

                if (endMs is int declaredMs && declaredMs > 0)
                {
                    var seconds = Mp3DurationSeconds(bytes);
                    if (seconds is null)
                    {
                        failures.Add($"{path}: could not read an MPEG audio duration from the file");
                        continue;
                    }

                    var declaredSeconds = declaredMs / 1000.0;
                    if (seconds.Value + 0.5 < declaredSeconds)
                    {
                        failures.Add(
                            $"{path}: {seconds.Value:0.0}s of audio but the stimulus declares a {declaredSeconds:0.0}s segment");
                    }
                }
            }

            Assert.True(failures.Count == 0,
                "Seeded listening audio is not audible, or is shorter than the segment it declares:"
                + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
        finally
        {
            await fixture.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// Duration of an MPEG Layer III stream, counted frame by frame from the audio payload.
    /// <para>
    /// <b>Why frames and not bytes:</b> dividing the file's length by one frame's bitrate assumes a
    /// constant bitrate and no leading tag. An ID3v2 tag at the front inflates the byte count, and a
    /// variable-bitrate file has no single bitrate to divide by — both make the measurement longer than
    /// the audio really is, which is the direction that lets a too-short file pass. Walking the frames
    /// answers "how many samples are there" directly: frames × samples-per-frame ÷ sample rate.
    /// </para>
    /// <para>
    /// Handles MPEG-1, MPEG-2 and MPEG-2.5, because the encoder picks the version from the sample rate
    /// (22 050 Hz is MPEG-2, not MPEG-1). Returns null when no frame header can be confirmed.
    /// </para>
    /// </summary>
    private static double? Mp3DurationSeconds(byte[] bytes)
    {
        // Bitrate tables in kbps, Layer III only, indexed by the header's bitrate field.
        int[] mpeg1Layer3 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
        int[] mpeg2Layer3 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];

        // The first confirmed frame fixes the stream's version and sample rate for the whole walk.
        var first = FindFirstFrame(bytes, isMpeg1Peek: false);
        if (first is not { } start) return null;

        var isMpeg1 = (bytes[start + 1] >> 3 & 0x03) == 3;
        var sampleRateIndex = (bytes[start + 2] >> 2) & 0x03;
        var sampleRate = (isMpeg1 ? new[] { 44100, 48000, 32000 } : new[] { 22050, 24000, 16000 })[sampleRateIndex];
        var samplesPerFrame = isMpeg1 ? 1152 : 576;
        var bitrateTable = isMpeg1 ? mpeg1Layer3 : mpeg2Layer3;

        var frames = 0;
        var offset = start;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF || (bytes[offset + 1] & 0xE0) != 0xE0) break;

            var bitrateIndex = (bytes[offset + 2] >> 4) & 0x0F;
            if (bitrateIndex is 0 or 15) break;

            var bitrate = bitrateTable[bitrateIndex] * 1000;
            if (bitrate == 0) break;

            var padding = (bytes[offset + 2] >> 1) & 0x01;
            var frameLength = (isMpeg1 ? 144 : 72) * bitrate / sampleRate + padding;

            // A partial trailing frame is not a frame; a wrong header shows up as an impossible length.
            if (frameLength <= 4 || offset + frameLength > bytes.Length) break;

            frames++;
            offset += frameLength;
        }

        return frames == 0 ? null : frames * (double)samplesPerFrame / sampleRate;
    }

    /// <summary>
    /// Offset of the first frame header that the frame after it agrees with. Skips an ID3v2 tag at the
    /// front, which otherwise puts the walk inside cover art.
    /// </summary>
    private static int? FindFirstFrame(byte[] bytes, bool isMpeg1Peek)
    {
        for (var i = 0; i + 4 <= bytes.Length; i++)
        {
            if (bytes[i] != 0xFF || (bytes[i + 1] & 0xE0) != 0xE0) continue;

            var versionBits = (bytes[i + 1] >> 3) & 0x03; // 3 = MPEG-1, 2 = MPEG-2, 0 = MPEG-2.5
            var layerBits = (bytes[i + 1] >> 1) & 0x03;   // 1 = Layer III
            if (layerBits != 1 || versionBits == 1) continue;

            var bitrateIndex = (bytes[i + 2] >> 4) & 0x0F;
            var sampleRateIndex = (bytes[i + 2] >> 2) & 0x03;
            if (bitrateIndex is 0 or 15 || sampleRateIndex == 3) continue;

            return i;
        }

        return null;
    }
}
