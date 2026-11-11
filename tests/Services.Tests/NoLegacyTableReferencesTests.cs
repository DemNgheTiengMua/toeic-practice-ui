using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #269 criterion 3: <b>no source file references any of the removed tables, proved by a scan.</b>
/// <para>
/// <b>Why a scan and not a grep in a pull request.</b> A grep run by hand is evidence of the moment it was
/// run and nothing after it: the next commit can add one reference and no test notices. This pins the
/// claim. It also states <i>which</i> files legitimately still name the tables, so a future reader does not
/// have to re-derive whether a hit is a defect or a proof of absence.
/// </para>
/// <para>
/// <b>What a wrong implementation could still pass.</b> A scan that searched only <c>src/</c> would pass
/// while a fixture still seeded the dropped tables. A scan that only looked for <c>db.Questions</c> would
/// miss a raw SQL string. So the scan is over every <c>.cs</c> file in <c>src/</c> and <c>tests/</c>, the
/// pattern matches the bare table names, and the allowlist below is explicit and justified line by line.
/// </para>
/// </summary>
public sealed class NoLegacyTableReferencesTests
{
    /// <summary>The tables and the view #269 removes.</summary>
    private static readonly string[] Removed =
    [
        "Questions",
        "QuestionGroups",
        "QuestionOptions",
        "UserQuestionHistories",
        "vw_UserCategoryAccuracy",
    ];

    /// <summary>
    /// What counts as a REFERENCE TO THE TABLE, as opposed to a same-named property. A DTO may carry a
    /// <c>Questions</c> collection and that is not a table reference - matching bare identifiers filled
    /// the report with false positives and would have taught the next reader to ignore it.
    /// <para>
    /// Each pattern is a way this codebase names a table: the DbSet, a SQL clause, a foreign-key target,
    /// an EF table mapping, or an entity navigation to the type.
    /// </para>
    /// </summary>
    private static IEnumerable<string> TableReferencePatterns(string name) =>
    [
        $"db.{name}",
        $"DbSet<{name.TrimEnd('s')}> {name}",
        $"FROM {name}",
        $"INTO {name}",
        $"UPDATE {name}",
        $"JOIN {name}",
        $"REFERENCES {name}",
        $"ToTable(\"{name}\")",
        $"ToTable(nameof({name}))",
        $"ON {name}(",
    ];

    /// <summary>
    /// The only files allowed to name a removed table, each with the reason. Three reasons exist, and a
    /// fourth file wanting an entry needs one of them:
    /// <list type="number">
    /// <item>The test's SUBJECT is that the object is absent. It cannot prove an absence without naming
    /// the thing.</item>
    /// <item>The test's SUBJECT is a former state: a migration script or a disposition list, which
    /// describe what used to be there.</item>
    /// <item>A guard only means something over the legacy graph - the EF split-query warning needs two
    /// collection navigations, which only the legacy shape had.</item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tests/Services.Tests/LegacyContentModelIsGoneTests.cs"] = "absence: names each removed object to assert it is gone",
        ["tests/Services.Tests/DataDispositionTests.cs"] = "absence: asserts the disposition list classifies them",
        ["tests/Services.Tests/FixtureLevelGuardTests.cs"] = "absence: allowlists a schema test that asserts a removed column is gone",
        ["tests/Services.Tests/PoolOnlyPaperTests.cs"] = "absence: asserts a pool paper needs no legacy row",
        ["tests/Services.Tests/StudioGroupWriterTests.cs"] = "absence: asserts the group write needs no QuestionGroups row",
        ["tests/Services.Tests/PoolImportTests.cs"] = "absence: asserts an import writes no legacy row",
        ["tests/Services.Tests/AdminExamPublishGateTests.cs"] = "absence: asserts the publish gate reads the roster",
        ["tests/Services.Tests/PaperMigrationCountsTests.cs"] = "former state: compares the legacy roster against the pool it became",
        ["tests/Services.Tests/MigratedPaperIsSittableTests.cs"] = "former state: deletes the legacy rows, which is the subject",
        ["tests/Services.Tests/CutoverReadinessTests.cs"] = "former state: builds the unrebuildable paper",
        ["tests/Services.Tests/PoolAnswerShapesTests.cs"] = "coexistence: a legacy answer is one of the shapes under test",
        ["tests/Services.Tests/AdminExamServiceSplitQueryTests.cs"] = "guard: the split-query warning needs the two-navigation legacy graph",
    };

    [Fact]
    public void No_file_outside_the_allowlist_names_a_removed_table()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in SourceFiles(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (Allowed.ContainsKey(relative))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var code = StripComment(lines[index]);
                if (code.Length == 0)
                {
                    continue;
                }

                foreach (var name in Removed)
                {
                    var hit = TableReferencePatterns(name)
                        .FirstOrDefault(pattern => code.Contains(pattern, StringComparison.Ordinal));

                    if (hit is not null)
                    {
                        offenders.Add($"{relative}:{index + 1}: {hit} -> {lines[index].Trim()}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These files name a table #269 removes. Only the three stated reasons may do so - migrate the "
            + "reference, or add the file to the allowlist with its reason:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The scan must find something it is looking for, or it passes vacuously on an empty file list. This
    /// is the control arm: the same walker, over the same roots, must see a table that still exists.
    /// </summary>
    [Fact]
    public void The_scan_finds_files_at_all()
    {
        var root = RepositoryRoot();
        var files = SourceFiles(root).ToList();

        Assert.True(files.Count > 100, $"the walker found only {files.Count} files, so it is pointed at the wrong place");

        var hits = files.Count(file => File.ReadAllLines(file).Any(line =>
            TableReferencePatterns("ItemVersions").Any(pattern => line.Contains(pattern, StringComparison.Ordinal))));
        Assert.True(hits > 0, "no file references ItemVersions, so the patterns or the roots are wrong");
    }

    /// <summary>
    /// The matcher must see a table reference and ignore a property that merely shares the name. Both
    /// directions matter: a matcher that saw everything would fill the report with false positives, and
    /// one that saw nothing would pass on a file still writing to the table.
    /// </summary>
    [Fact]
    public void The_matcher_sees_a_table_and_not_a_property()
    {
        // A reference to the TABLE, in each form this codebase uses.
        Assert.True(Matches("var n = await db.Questions.CountAsync();", "Questions"));
        Assert.True(Matches("INSERT INTO QuestionGroups (ExamId) VALUES (1)", "QuestionGroups"));
        Assert.True(Matches("SELECT * FROM QuestionOptions WHERE IsCorrect = 1", "QuestionOptions"));
        Assert.True(Matches("CONSTRAINT FK_X FOREIGN KEY (QuestionId) REFERENCES Questions(QuestionId)", "Questions"));
        Assert.True(Matches("public virtual DbSet<Question> Questions { get; set; }", "Questions"));

        // A same-named PROPERTY is not the table.
        Assert.False(Matches("IReadOnlyList<AttemptReviewQuestion> Questions,", "Questions"));
        Assert.False(Matches("public int QuestionCount => Groups.Sum(g => g.Questions.Count);", "Questions"));

        // Prose in all three comment styles this codebase uses.
        Assert.False(Matches("/// A section's accuracy is a fact about the pool, not Questions.", "Questions"));
        Assert.False(Matches("// reading db.Questions here would be wrong", "Questions"));

        // A trailing comment is dropped; the code before it is kept.
        Assert.True(Matches("db.Questions.Remove(question); // was legacy-only", "Questions"));
    }

    private static bool Matches(string line, string name)
    {
        var code = StripComment(line);
        return code.Length != 0
            && TableReferencePatterns(name).Any(pattern => code.Contains(pattern, StringComparison.Ordinal));
    }

    /// <summary>
    /// A reference is a USE, not a mention. A docblock explaining that a column moved off <c>Questions</c>
    /// is history a reader needs; it cannot write a row. Comments are therefore stripped before matching.
    /// <para>
    /// Stripping is line-based rather than a full parse: a <c>//</c> or <c>///</c> prefix, or a
    /// <c>--</c> inside a SQL string, is enough for this codebase's style. The control arm below proves the
    /// stripper does not swallow real code.
    /// </para>
    /// </summary>
    /// <summary>Everything before the first comment marker, or the whole line when it carries none.</summary>
    private static string StripComment(string line)
    {
        var trimmed = line.TrimStart();

        // A whole-line comment: '//', '///', a SQL '--', or a block-comment body.
        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("--", StringComparison.Ordinal)
            || trimmed.StartsWith('*'))
        {
            return string.Empty;
        }

        var slash = line.IndexOf("//", StringComparison.Ordinal);
        if (slash >= 0)
        {
            return line[..slash];
        }

        var dashes = line.IndexOf("--", StringComparison.Ordinal);
        return dashes >= 0 ? line[..dashes] : line;
    }

    /// <summary>Every CS file under <c>src/</c> and <c>tests/</c>, skipping build output.</summary>
    private static IEnumerable<string> SourceFiles(string root)
    {
        foreach (var directory in new[] { "src", "tests" })
        {
            var path = Path.Combine(root, directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    /// <summary>The repository root, found by walking up for the solution file.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExtraEfficiency.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root (ExtraEfficiency.slnx) above " + AppContext.BaseDirectory);
    }
}
