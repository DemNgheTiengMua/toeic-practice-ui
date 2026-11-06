using System.Text.RegularExpressions;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #230 batch three: no fixture, seed or test writes a difficulty any more. #231 dropped the column,
/// so this scan now points the other way: the only surviving mentions are the guards that assert the
/// scale is <b>absent</b>, and the allowlist below is exactly those files.
/// <para>
/// It still earns its place after the deletion. A re-run that resurrected the column would be caught
/// by domain33-contract-retirement.sql, but a new fixture that started <b>writing</b> a difficulty is
/// caught here, before the column exists to write into — which is the shape the failure took twice in
/// this rebuild (a fixture inventing a default, and a service writing 3 "for compatibility").
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
    /// Files still allowed to name it, and why. Each one asserts the difficulty is <b>gone</b> - from
    /// the entity, from the model, from the export wire, from the schema. Removing one would remove a
    /// guard, not a defect.
    /// </summary>
    private static readonly Dictionary<string, string> Guards = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tests/Services.Tests/CefrLevelMigrationTests.cs"] =
            "#231: asserts the difficulty member is absent from both entities and from the EF model, so "
            + "a type that grew a second scale back is refused.",
        ["tests/Services.Tests/AuthoringLevelTests.cs"] =
            "#231: asserts the exported manifest names no difficulty field at all.",
        ["docs/schema-tests/domain33-contract-retirement.sql"] =
            "#231: asserts the column and its 1-5 CHECK are gone from Questions and from the dropped "
            + "PathModuleItems table.",
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
                // A comment that explains what the scale used to be cannot reinstate it. The guard is
                // about code, fixtures and seeds that would read or write the value; the retired
                // behaviour is described in prose in more than one place and that prose is the
                // record of why it went.
                if (IsComment(text))
                {
                    continue;
                }

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

    /// <summary>The seeded and asserted content: schema fixtures, both seeds, the samples, and every
    /// source file. `src` is included because the guard's claim is that the 1-5 scale is gone, and a
    /// new <c>DifficultyLevel</c> reference in production code would reinstate it just as surely as one
    /// in a fixture -- the original scope left exactly that door open.</summary>
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

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(path))
            {
                continue;
            }

            yield return Relative(root, path);
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(path))
            {
                continue;
            }

            yield return Relative(root, path);
        }
    }

    /// <summary>Build output is not a fixture, and an obj/ copy of a source file would double-report.</summary>
    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>A line whose content is entirely comment: <c>//</c>, <c>///</c>, <c>--</c>, <c>/*</c> or <c>*</c>.</summary>
    private static bool IsComment(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("--", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal);
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
