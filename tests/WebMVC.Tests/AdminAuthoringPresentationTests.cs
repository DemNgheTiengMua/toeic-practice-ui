using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

public sealed class AdminAuthoringPresentationTests
{
    [Theory]
    [InlineData("ExamStudio")]
    [InlineData("CertificateWizard")]
    [InlineData("_QuickIngestModal")]
    [InlineData("Exams")]
    [InlineData("ExamEditor")]
    [InlineData("ScoreConversion")]
    public void AuthoringViews_UseExternalAssets(string view)
    {
        var text = ReadComposedView(view);
        Assert.DoesNotMatch(@"<script(?:\s[^>]*)?>\s*[^<\s]", text);
        Assert.DoesNotContain("<style", text);
        Assert.DoesNotContain(".GroupBy(", text);
        Assert.DoesNotContain(".Where(", text);
        Assert.DoesNotContain(".Sum(", text);
        Assert.DoesNotContain(".Select(", text);
        Assert.DoesNotContain(".FirstOrDefault(", text);
        Assert.DoesNotContain("Math.", text);
        Assert.DoesNotContain("@inject", text);
        Assert.DoesNotContain("JsonSerializer", text);
    }

    internal static string ReadComposedView(string view)
    {
        var seen = new HashSet<string>();
        var directory = Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", "Admin");
        string Read(string name)
        {
            var path = name.StartsWith("~/")
                ? Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", name[2..])
                : Path.Combine(directory, name.EndsWith(".cshtml") ? name : name + ".cshtml");
            if (!seen.Add(path)) return "";
            Assert.True(File.Exists(path), $"Missing composed view: {path}");
            var content = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(content, "PartialAsync\\(\"([^\"]+)\""))
                content += "\n" + Read(match.Groups[1].Value);
            return content;
        }
        return Read(view);
    }
}
