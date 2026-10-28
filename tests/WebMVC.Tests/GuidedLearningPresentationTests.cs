using System.IO;
using System.Linq;
using Xunit;

namespace WebMVC.Tests;

public sealed class GuidedLearningPresentationTests
{
    [Theory]
    [InlineData("Dashboard")]
    [InlineData("LearningPath")]
    [InlineData("Lessons")]
    [InlineData("Mentors")]
    [InlineData("Admin/Lessons")]
    [InlineData("Admin/MentorSlots.cshtml")]
    public void Views_ReceivePreparedPresentationAndUseExternalAssets(string folder)
    {
        var directory = Path.Combine(TestPathHelper.FindRepoRoot(), "src/WebMVC/Views", folder);
        foreach (var path in File.Exists(directory) ? new[] { directory } : Directory.GetFiles(directory, "*.cshtml"))
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("@inject", text);
            Assert.DoesNotContain("<style", text);
            Assert.DoesNotMatch(@"<script(?:\s[^>]*)?>\s*[^<\s]", text);
            Assert.DoesNotContain(".GroupBy(", text);
            Assert.DoesNotContain(".OrderBy(", text);
            Assert.DoesNotContain(".Select(", text);
            Assert.DoesNotContain(".Where(", text);
            Assert.DoesNotContain(".Any(", text);
            Assert.DoesNotContain(".Sum(", text);
            Assert.DoesNotContain("Math.", text);
            Assert.DoesNotContain("JsonSerializer", text);
            Assert.DoesNotContain(" switch", text);
            Assert.DoesNotContain("AiNarrativeFormatter.", text);
        }
    }
}
