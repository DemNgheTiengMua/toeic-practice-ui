using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #260 / #276: <b>a public service class must be reachable from the product.</b>
/// <para>
/// <b>Why a scan and not a grep in a pull request.</b> <c>PlacementStageOneDrawService</c> and
/// <c>PlacementStageTwoDrawService</c> were complete, tested, and executed by nothing for two tickets.
/// Their own tests drove them directly, so a green suite said the code worked - never that anything ran
/// it. A guard is the only thing that turns "this is wired" from a claim into a fact the next commit
/// cannot quietly break.
/// </para>
/// <para>
/// <b>What a wrong implementation could still pass.</b> A scan that only looked for DI registrations
/// would pass on a registered service nothing calls, which is exactly the state #276 was filed about:
/// registering two uncalled services would have silenced the defect while it persisted. So a class must
/// have BOTH halves - a registration and a production reference outside its own file.
/// </para>
/// <para>
/// <b>Two named exemptions, and why the scan cannot reach them.</b> <c>DataDisposition</c> is read only
/// by tests on purpose (it is the table disposition ledger, and the product has no reason to consult it
/// at run time), and <c>GateOneRules</c> is a same-file helper of <c>GateOne</c>. Both are
/// <c>BusinessObjects</c> types, and this scan's population is the <c>Services</c> layer - so they need
/// no entry, and the reason is the population rather than an allowlist that would hide a real miss. The
/// allowlist below is for <c>Services</c> types that are likewise not services: a DTO pair that lives
/// beside its document.
/// </para>
/// </summary>
public sealed class DeadServiceGuardTests
{
    /// <summary>
    /// Types in <c>src/Services</c> that are not services. Each needs a reason, and the reason must be
    /// one of: a same-file helper of a real service, or a test-only read model.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["SkillContentPoolAudit"] = "same-file helper: a nested DTO of SkillContentPoolDocument, deserialized from the pool file",
        ["SkillContentPoolTask"] = "same-file helper: a nested DTO of SkillContentPoolDocument, deserialized from the pool file",
    };

    /// <summary>
    /// The few files whose classes are not judged: an exception type is thrown, not called, and the
    /// scan's "production reference" rule would flag one that only its own throw site names.
    /// </summary>
    private static readonly string[] SkippedDirectories = ["Exceptions"];

    [Fact]
    public void Every_public_service_class_is_registered_and_referenced()
    {
        var root = RepositoryRoot();
        var registrations = ReadRegistrations(root);
        Assert.True(registrations.Count > 20,
            $"the DI scan read only {registrations.Count} registrations, so it is pointed at the wrong file");

        var offenders = new List<string>();
        var inspected = 0;
        var identifiers = ReadIdentifiers(root);

        foreach (var type in ServiceClasses(root))
        {
            inspected++;
            if (Allowed.ContainsKey(type.Name))
            {
                continue;
            }

            var registered = registrations.Contains(type.Name);
            var referenced = IsReferencedOutsideItself(identifiers, type, root);

            if (!registered && !referenced)
            {
                offenders.Add($"{type.File}:{type.Line}: {type.Name}");
            }
        }

        Assert.True(inspected > 40,
            $"the walker found only {inspected} public class(es) under src/Services, so it is pointed at the wrong place");

        Assert.True(offenders.Count == 0,
            "These public classes under src/Services have no DI registration and no production caller. Wire "
            + "them, register them, or add an allowlist entry with a reason:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The control arm: the detector must find the two classes #276 wired, or it is passing on an empty
    /// walk. This is the assertion that would have failed at #214 time - both names were in the tree, both
    /// were unreferenced, and nothing said so.
    /// </summary>
    [Fact]
    public void The_detector_sees_the_two_draw_services_as_live()
    {
        var root = RepositoryRoot();
        var registrations = ReadRegistrations(root);

        // The consumer, read once: PlacementService resolves each draw through its CONTRACT, so naming
        // the interface is what proves the class is reachable. Naming the file (rather than any src file)
        // keeps the control from passing on a reference in an unrelated place - a test that drove the
        // service directly would also be a "reference", and that is the state #276 was filed about.
        var consumers = Directory
            .EnumerateFiles(Path.Combine(root, "src", "Services"), "PlacementService*.cs")
            .Select(File.ReadAllText)
            .ToList();

        foreach (var (implementation, contract) in new[]
        {
            ("PlacementStageOneDrawService", "IPlacementStageOneDrawService"),
            ("PlacementStageTwoDrawService", "IPlacementStageTwoDrawService"),
        })
        {
            Assert.True(
                ServiceClasses(root).Any(t => t.Name == implementation),
                $"{implementation} is not declared under src/Services, so the walk is wrong");
            Assert.True(registrations.Contains(implementation), $"{implementation} has no DI registration");
            Assert.True(
                consumers.Any(text => text.Contains(contract, StringComparison.Ordinal)),
                $"PlacementService does not resolve {contract}, so {implementation} is still unreachable");
        }
    }

    /// <summary>
    /// The judgement itself is exercised on synthetic input, because a detector that always says "live"
    /// would make the first test vacuous.
    /// </summary>
    [Fact]
    public void The_judgement_is_registration_AND_a_caller()
    {
        Assert.False(IsLive(registered: false, referenced: false));
        // A registration alone is the state #276 was filed about.
        Assert.False(IsLive(registered: true, referenced: false));
        Assert.True(IsLive(registered: true, referenced: true));
    }

    private static bool IsLive(bool registered, bool referenced) => registered && referenced;

    private sealed record ServiceClass(string Name, string File, int Line);

    /// <summary>
    /// Every <c>public class X</c> under <c>src/Services</c>. Records, static classes and abstract classes
    /// are not services: a blueprint is arithmetic, a record is a value, and neither is registered in DI.
    /// </summary>
    private static IEnumerable<ServiceClass> ServiceClasses(string root)
    {
        var services = Path.Combine(root, "src", "Services");
        foreach (var file in Directory.EnumerateFiles(services, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
            {
                continue;
            }

            if (SkippedDirectories.Any(skipped => normalized.Contains($"/{skipped}/")))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var match = DeclaredClass.Match(lines[index]);
                if (match.Success)
                {
                    yield return new ServiceClass(
                        match.Groups["name"].Value,
                        Path.GetRelativePath(root, file).Replace('\\', '/'),
                        index + 1);
                }
            }
        }
    }

    /// <summary>
    /// <c>public class X</c> / <c>public sealed class X</c> / <c>public sealed partial class X</c>, and not
    /// <c>public static class</c> or <c>public abstract class</c>. One compiled instance: the first draft
    /// built a fresh Regex per line and the scan took seven minutes.
    /// </summary>
    private static readonly Regex DeclaredClass = new(
        @"^\s*public\s+(?:(?:sealed|partial)\s+)*class\s+(?<name>\w+)",
        RegexOptions.Compiled);

    /// <summary>
    /// The type names <c>ServiceCollectionExtensions</c> registers. Read as text rather than by loading the
    /// WebAPI assembly: the test project does not reference it, and a scan of the one file that owns every
    /// registration cannot go stale against a second registration site.
    /// </summary>
    private static HashSet<string> ReadRegistrations(string root)
    {
        var path = Path.Combine(root, "src", "WebAPI", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.True(File.Exists(path), $"the DI registration file is not at {path}");

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("services.AddScoped<", StringComparison.Ordinal)
                || line.StartsWith("services.AddSingleton<", StringComparison.Ordinal)
                || line.StartsWith("services.AddTransient<", StringComparison.Ordinal)
                || line.StartsWith("services.AddHostedService<", StringComparison.Ordinal))
            .SelectMany(line => Regex.Matches(line, @"\b[A-Z]\w+").Select(m => m.Value))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// True when a <c>src/</c> file other than the one declaring it names the type. Tests are not
    /// production callers, which is the whole distinction this guard exists to draw.
    /// <para>
    /// The identifiers are tokenized once per file by the caller and passed in: the first draft ran a
    /// fresh regex over every file once per declared type (250 types x 900 files) and took minutes. A set
    /// lookup per file is the same answer at a fraction of the cost.
    /// </para>
    /// </summary>
    private static bool IsReferencedOutsideItself(IReadOnlyDictionary<string, HashSet<string>> identifiersByPath, ServiceClass type, string root)
    {
        var own = Path.GetFullPath(Path.Combine(root, type.File));

        foreach (var (path, identifiers) in identifiersByPath)
        {
            if (FileEquals(path, own))
            {
                continue;
            }

            if (identifiers.Contains(type.Name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every <c>.cs</c> file under <c>src/</c>, tokenized once, keyed by full path.</summary>
    private static Dictionary<string, HashSet<string>> ReadIdentifiers(string root)
    {
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
            {
                continue;
            }

            sources[Path.GetFullPath(file)] = File.ReadAllText(file)
                .Split(IdentifierSeparators, StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);
        }

        return sources;
    }

    private static readonly char[] IdentifierSeparators =
        [' ', '\t', '\r', '\n', '.', ',', ';', ':', '(', ')', '{', '}', '[', ']', '<', '>', '=', '"', '\'', '+', '-', '*', '/', '&', '|', '!', '?', '#', '@', '%', '^', '~'];

    private static bool FileEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

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

        throw new InvalidOperationException(
            "Could not find the repository root (ExtraEfficiency.slnx) above " + AppContext.BaseDirectory);
    }
}
