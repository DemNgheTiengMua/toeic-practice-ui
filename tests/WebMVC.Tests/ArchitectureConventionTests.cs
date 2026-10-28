using System.Text.RegularExpressions;

namespace WebMVC.Tests;

public sealed class ArchitectureConventionTests
{
    [Fact]
    public void Razor_views_remain_pure_and_below_the_size_ceiling()
    {
        var viewsRoot = Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views");
        var inlineScript = new Regex(@"<script\b(?!(?:[^>]*\bsrc\s*=|[^>]*\btype\s*=\s*[""']application/json[""']))[^>]*>", RegexOptions.IgnoreCase);
        var dtoConstruction = new Regex(@"new\s+[\w.]+Dto\s*\(", RegexOptions.CultureInvariant);

        foreach (var path in Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch(@"@inject\s+(?:DateTimeFormatter|CurrentUser)\b", source);
            Assert.DoesNotMatch(@"<style\b", source);
            Assert.DoesNotMatch(inlineScript, source);
            Assert.DoesNotContain(".GroupBy(", source);
            Assert.DoesNotMatch(dtoConstruction, source);
            Assert.True(File.ReadLines(path).Count() <= 450, path);
        }
    }

    [Fact]
    public void Legacy_bundle_units_contain_no_domain_type_declarations()
    {
        var root = TestPathHelper.FindRepoRoot();
        foreach (var relativePath in new[]
        {
            "src/Contracts/DomainClients.cs",
            "src/BusinessObjects/PracticeModels.cs",
            "src/BusinessObjects/SpeakingPracticeModels.cs",
            "src/BusinessObjects/WritingPracticeModels.cs",
            "src/WebMVC/Models/PracticeViewModels.cs",
            "src/WebMVC/Models/SpeakingViewModels.cs",
            "src/WebMVC/Models/WritingViewModels.cs"
        })
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            Assert.DoesNotMatch(@"\bpublic\s+(?:sealed\s+)?(?:class|record|enum|interface)\b", File.ReadAllText(path));
        }
    }
}