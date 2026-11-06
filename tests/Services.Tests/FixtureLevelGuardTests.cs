using System.Text.RegularExpressions;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #230 batch three: no fixture, seed or test asserts or assigns a difficulty any more.
/// <para>
/// This is the check that makes #231's deletion safe. It scans every file that seeds or asserts
/// content and fails on the identifier, so a new fixture that reaches for the old column is refused
/// at test time rather than discovered when the column is dropped. The allowlist is deliberately
/// tiny and every entry is a guard that exists <b>because</b> the difficulty is dormant; #231 shrinks
/// it to nothing when the column goes.
/// </para>
/// <para>
/// The scope is files, not a grep: docs/adr, docs/specs and docs/research discuss the retired scale on
/// purpose and are not fixtures. Only the seeded, sampled and asserted content is covered.
/// </para>
/// </summary>
public sealed class FixtureLevelGuardTests
{
    /// <summary>The identifier in either case, which is what a fixture would actually type.</summary>
    private static readonly Regex Difficulty = new(@"\bdifficulty_?level\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// Files still allowed to name it, and why. Each one asserts the difficulty is <b>not</b> written
    /// or <b>not</b> derived from a level, so removing it would remove the guard, not the defect.
    /// </summary>
    private static readonly Dictionary<string, string> Guards = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tests/Services.Tests/CefrLevelMigrationTests.cs"] =
            "#228: proves a level and a difficulty never derive from one another, and that the column "
            + "still compiles beside the level until #231 drops it.",
        ["tests/Services.Tests/AuthoringLevelTests.cs"] =
            "#229: reads the column through raw SQL to assert it is NULL on every row an authoring path "
            + "writes today, and plants a difficulty in a manifest to prove it is not used as a level.",
        ["tests/Services.Tests/PoolImportTests.cs"] =
            "#229: asserts the legacy paper row a pool import also writes carries no difficulty.",
    };

    [Fact]
    public void No_fixture_seed_or_test_names_a_difficulty_outside_the_guards_that_prove_it_dormant()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var relative in FixtureFiles(root))
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            scanned++;
            if (Guards.ContainsKey(relative))
            {
                continue;
            }

            foreach (var (line, text) in File.ReadLines(path).Select((text, i) => (i + 1, text)))
            {
                if (Difficulty.IsMatch(text))
                {
                    offenders.Add($"{relative}:{line}: {text.Trim()}");
                }
            }
        }

        // A scan that walked no files proves nothing, and would pass on a wrong root.
        Assert.True(scanned > 100, $"the fixture scan reached only {scanned} files; it looked in the wrong place");
        Assert.Empty(offenders);
    }

    /// <summary>
    /// Every allowlisted guard must still exist. A stale entry silently widens the hole: a deleted
    /// guard would keep its exemption and let a real reference through under its name.
    /// </summary>
    [Fact]
    public void Every_guard_exemption_still_has_a_file_and_still_names_the_difficulty()
    {
        var root = RepoRoot();

        foreach (var (relative, reason) in Guards)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{relative} is exempted ({reason}) but does not exist");
            Assert.True(
                File.ReadLines(path).Any(line => Difficulty.IsMatch(line)),
                $"{relative} is exempted but no longer names the difficulty; drop the exemption");
        }
    }

    /// <summary>The seeded and asserted content: schema fixtures, both seeds, the samples, the tests.</summary>
    private static IEnumerable<string> FixtureFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "docs", "schema-tests"), "*.sql"))
        {
            yield return Relative(root, path);
        }

        foreach (var name in new[] { "seed-demo.sql", "seed-diagnostic-benchmark.sql" })
        {
            yield return $"docs/{name}";
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "docs", "samples"), "*.json"))
        {
            yield return Relative(root, path);
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories))
        {
            // Build output is not a fixture, and an obj/ copy of a source file would double-report.
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return Relative(root, path);
        }
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExtraEfficiency.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
