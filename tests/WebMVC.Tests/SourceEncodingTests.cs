using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Guards the source tree against Windows-1252 bytes that a legacy editor can slip in and that
/// corrupt the rendered UI (replacement characters in labels, mangled aria-labels). A non-UTF8
/// file also confuses the Razor compiler, which assumes UTF-8 for .cshtml.
/// </summary>
public sealed class SourceEncodingTests
{
    private static readonly string[] SourceExtensions = [".cs", ".cshtml", ".js", ".css", ".json", ".sql"];

    [Fact]
    public void Every_source_file_is_valid_UTF8_without_a_byte_order_mark()
    {
        var root = Path.Combine(TestPathHelper.FindRepoRoot(), "src");
        Assert.True(Directory.Exists(root), $"Source root not found: {root}");

        var problems = new List<string>();
        foreach (var path in SourceFiles(root))
        {
            var bytes = File.ReadAllBytes(path);
            var relative = Path.GetRelativePath(TestPathHelper.FindRepoRoot(), path).Replace('\\', '/');

            if (bytes is [0xEF, 0xBB, 0xBF, ..])
            {
                problems.Add($"{relative}: UTF-8 byte order mark");
                continue;
            }

            // A strict decode reports the exact offset of the first stray byte, which is what a
            // maintainer needs to reopen the file at the right place.
            var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            try
            {
                decoder.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                problems.Add($"{relative}: {ex.Message}");
            }
        }

        Assert.True(problems.Count == 0,
            $"{problems.Count} source file(s) are not clean UTF-8:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    /// <summary>Placement draft status labels were the regression that motivated this guard, so
    /// pin the exact typography the browser must receive.</summary>
    [Fact]
    public void Placement_take_script_uses_a_real_ellipsis_in_its_save_labels()
    {
        var path = Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "js", "placement", "take.js");
        var text = File.ReadAllText(path, Encoding.UTF8);

        Assert.Contains("\"Saving draft…\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Saving…\"", text, StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        var extensions = SourceExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(path => !IsBuildOutput(path))
            // Fonts, audio and icons live under wwwroot but are matched by extension above, so a
            // stray .json in build output is the only realistic false positive to skip.
            .Order(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("bin") || parts.Contains("obj");
    }
}
