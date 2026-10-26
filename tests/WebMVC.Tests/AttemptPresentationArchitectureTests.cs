using System.Text.RegularExpressions;

namespace WebMVC.Tests;

public sealed class AttemptPresentationArchitectureTests
{
    [Fact]
    public void Completed_review_loads_its_filters_independently_of_the_active_sitting_shell()
    {
        var root = Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC");
        var view = File.ReadAllText(Path.Combine(root, "Views", "Exams", "Review.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "exams", "completed-review.js"));
        Assert.Contains("~/js/exams/completed-review.js", view);
        Assert.Contains("review-part-select", script);
        Assert.Contains("review-filter-select", script);
        Assert.Contains("applyFilters", script);
        Assert.DoesNotContain("ExamSitting", script);
    }

    [Theory]
    [InlineData("Exams")]
    [InlineData("Placement")]
    public void Attempt_views_only_render_prepared_data_and_link_executable_assets(string feature)
    {
        var folder = Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", feature);
        foreach (var path in Directory.EnumerateFiles(folder, "*.cshtml"))
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("@inject", source);
            Assert.DoesNotMatch(@"<style\b", source);
            Assert.DoesNotMatch(@"<script(?![^>]*(?:\bsrc\s*=|application/json))[^>]*>", source);
            Assert.DoesNotMatch(@"new\s+[\w.]*Dto\b", source);
            Assert.DoesNotMatch(@"\.(?:GroupBy|Select|Where|OrderBy|TakeLast|Sum|Max|FirstOrDefault)\s*\(", source);
        }
    }
}
