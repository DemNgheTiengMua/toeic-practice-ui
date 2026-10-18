using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

public sealed class ExamsViewTests
{
    [Fact]
    public void OptionMarkerAndText_MustHavePointerEventsNone_ToAvoidInterceptingClicks()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var cssPath = Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "css", "components.css");

        Assert.True(File.Exists(cssPath), "components.css must exist");
        var cssContent = File.ReadAllText(cssPath);

        // Match .option__marker block
        var markerMatch = Regex.Match(cssContent, @"\.option__marker\s*\{(?<rules>[^}]+)\}");
        Assert.True(markerMatch.Success, ".option__marker rule block must be found in components.css");
        Assert.Matches(@"pointer-events\s*:\s*none", markerMatch.Groups["rules"].Value);

        // Match .option__text block
        var textMatch = Regex.Match(cssContent, @"\.option__text\s*\{(?<rules>[^}]+)\}");
        Assert.True(textMatch.Success, ".option__text rule block must be found in components.css");
        Assert.Matches(@"pointer-events\s*:\s*none", textMatch.Groups["rules"].Value);
    }
}
