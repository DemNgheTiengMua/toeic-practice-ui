using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #245 and #249: <b>the rebuild ships as the only path, and the guard that keeps it that way.</b>
/// <para>
/// Both tickets are satisfied in the negative. There is no account-level flag to flip (#245) and so
/// nothing to remove (#249): the old screens were deleted in place rather than parked behind a switch,
/// which is why their criteria are met without a migration. That is a good outcome and a fragile one,
/// because "we did not build the flag" is a fact about a moment, not about the code.
/// </para>
/// <para>
/// <b>What would break this silently.</b> The first screen that needs to be "off for now" reaches for a
/// boolean, and the rebuild becomes two paths in one afternoon. The second is a per-user column, which
/// produces exactly the two-answers failure the rebuild exists to prevent. Neither is a dramatic
/// change; both are one line. This file is the line that stops them, and it is the automated pass the
/// criteria ask for rather than an inspection someone has to remember to repeat.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong:</b> a scan over the wrong root walks no files and
/// asserts nothing. The scanned count is therefore asserted too, in each test.
/// </para>
/// </summary>
public sealed class SingleExperienceGuardTests
{
    /// <summary>
    /// The names a rebuild switch is actually given. Every one of these is a shape a real "put it behind
    /// a flag" change has used, and none of them exists today.
    /// </summary>
    private static readonly Regex SwitchNames = new(
        @"\b(RebuildFlag|CefrRebuild|UseRebuild\w*|RebuiltExperience|ExperienceFlag|IsRebuildEnabled"
        + @"|LegacyPath|OldExperience|FeatureFlag|EnableLegacy\w*)\b",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// A type that exists only to keep the retired path reachable. #249's second criterion names the
    /// words; the rebuild kept none of them, and the assertion keeps it that way.
    /// </summary>
    private static readonly Regex RetiredAdapter = new(
        @"\b(class|record|interface)\s+\w*(Adapter|Shim|LegacyBridge)\b");

    /// <summary>
    /// <b>#245.1 and #245.5, #249.1.</b> No rebuild switch exists anywhere in the product, in any
    /// spelling, and no column selects one. A per-user flag would appear here as surely as an
    /// account-level one, which is why this is one assertion rather than two: the ticket's distinction
    /// is about where the switch lives, and the cheapest way to hold it is that there is none.
    /// </summary>
    [Fact]
    public void No_rebuild_switch_exists_in_the_product()
    {
        var (files, offenders) = Scan("src", file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        Assert.True(files > 500, $"the switch scan reached only {files} files; it looked in the wrong place");
        Assert.Empty(offenders.Where(o => !IsComment(o.Text)));
    }

    /// <summary>
    /// <b>#249.2.</b> No adapter, shim or legacy bridge survives, and none can be added back under the
    /// name. The schema's own retirement tests cover the tables; this covers the code shape that would
    /// keep a retired table in use without adding a column.
    /// </summary>
    [Fact]
    public void No_adapter_or_shim_keeps_a_retired_path_alive()
    {
        var (files, offenders) = Scan("src", file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

        Assert.True(files > 400, $"the adapter scan reached only {files} files; it looked in the wrong place");
        Assert.Empty(offenders.Where(o => RetiredAdapter.IsMatch(o.Text) && !IsComment(o.Text)));
    }

    /// <summary>
    /// <b>#249.3, half of it.</b> Every view a controller names by path exists on disk. A deleted view
    /// whose reference remained is a 500 at request time, not a compile error, and that is the failure
    /// shape this catches — the other half (nav entries) is
    /// <see cref="Every_nav_entry_resolves_to_a_controller_or_a_route"/>.
    /// </summary>
    [Fact]
    public void Every_view_a_controller_names_by_path_exists()
    {
        var root = TestPathHelper.FindRepoRoot();
        var reference = new Regex(@"View\(\s*""~/Views/([^""]+)""");
        var checkedCount = 0;

        foreach (var controller in Directory.EnumerateFiles(
            Path.Combine(root, "src", "WebMVC", "Controllers"), "*.cs"))
        {
            foreach (var match in reference.Matches(File.ReadAllText(controller)).Cast<Match>())
            {
                checkedCount++;
                var relative = match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar);
                var path = Path.Combine(root, "src", "WebMVC", "Views", relative);
                Assert.True(File.Exists(path), $"{Path.GetFileName(controller)} names ~/Views/{match.Groups[1].Value}, which does not exist");
            }
        }

        Assert.True(checkedCount > 20, $"the view scan checked only {checkedCount} references; it looked in the wrong place");
    }

    /// <summary>
    /// <b>#249.3, the other half.</b> Every in-product link points at a first segment that some
    /// controller serves — either through an absolute route or through the conventional
    /// <c>{controller}</c> route.
    /// <para>
    /// A link left behind by a deleted flow is the visible half of a dangling route: the page still
    /// renders and the click 404s. Razor expressions, fragments and external URLs are skipped because
    /// their target is not knowable from the source.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_nav_entry_resolves_to_a_controller_or_a_route()
    {
        var root = TestPathHelper.FindRepoRoot();
        var views = Path.Combine(root, "src", "WebMVC", "Views");
        var controllers = Path.Combine(root, "src", "WebMVC", "Controllers");

        // What the router can serve: every absolute route's first segment, plus every controller's
        // conventional name (the default pattern is {controller=Home}/{action=Index}/{id?}).
        var served = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var absolute = new Regex(@"\[(?:HttpGet|HttpPost|HttpPut|HttpDelete)\(""(/[^""]+)""\)\]");
        foreach (var file in Directory.EnumerateFiles(controllers, "*.cs"))
        {
            foreach (var match in absolute.Matches(File.ReadAllText(file)).Cast<Match>())
            {
                var segment = match.Groups[1].Value.Trim('/').Split('/')[0];
                if (segment.Length > 0)
                {
                    served.Add(segment);
                }
            }
        }

        foreach (var file in Directory.EnumerateFiles(controllers, "*.cs"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            served.Add(name[..^"Controller".Length]);
        }

        // ADR-0015: routes ported from WebMVC to the React SPA in src/web
        var spaRoutes = new[]
        {
            "auth", "dashboard", "pricing", "certificates", "sign-in", "register", "forgot-password", "reset-password",
            "verify-email", "learning-path", "weakness", "weakness-analysis", "onboarding", "placement", "lessons",
            "practice", "speaking", "writing", "saved-questions", "wallet", "mentors", "profile", "support"
        };
        foreach (var route in spaRoutes)
        {
            served.Add(route);
        }

        var href = new Regex(@"href=""(/[^""]*)""");
        var checkedCount = 0;
        var dangling = new List<string>();

        foreach (var file in Directory.EnumerateFiles(views, "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var match in href.Matches(text).Cast<Match>())
            {
                var target = match.Groups[1].Value;
                // A Razor expression, a fragment or a query string names no segment we can read here.
                if (target.Contains('@') || target.StartsWith("/#", StringComparison.Ordinal))
                {
                    continue;
                }

                var segment = target.Trim('/').Split('?')[0].Split('/')[0];
                if (segment.Length == 0)
                {
                    // The site root.
                    continue;
                }

                checkedCount++;
                if (!served.Contains(segment))
                {
                    dangling.Add($"{Path.GetRelativePath(views, file)} -> {target}");
                }
            }
        }

        Assert.True(checkedCount > 100, $"the nav scan checked only {checkedCount} links; it looked in the wrong place");
        Assert.Empty(dangling);
    }

    /// <summary>
    /// Walks one root and returns the files it read plus every line naming something it should not.
    /// </summary>
    private static (int Files, List<(string File, int Line, string Text)> Offenders) Scan(
        string relativeRoot, Func<string, bool> include)
    {
        var root = TestPathHelper.FindRepoRoot();
        var offenders = new List<(string, int, string)>();
        var files = 0;

        foreach (var path in Directory.EnumerateFiles(
            Path.Combine(root, relativeRoot), "*", SearchOption.AllDirectories))
        {
            if (!include(path) || IsBuildOutput(path))
            {
                continue;
            }

            files++;
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            foreach (var (line, text) in File.ReadLines(path).Select((text, i) => (i + 1, text)))
            {
                if (SwitchNames.IsMatch(text) || RetiredAdapter.IsMatch(text))
                {
                    offenders.Add((relative, line, text));
                }
            }
        }

        return (files, offenders);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// A comment explaining what the retired path was cannot reinstate it, and this rebuild carries
    /// more prose about deletions than most. Only code counts.
    /// </summary>
    private static bool IsComment(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("--", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal);
    }
}
