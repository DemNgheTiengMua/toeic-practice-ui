using Xunit;

namespace WebMVC.Tests;

public sealed class FreeResponsePresentationTests
{
    [Fact]
    public void Speaking_take_passes_recording_configuration_to_external_script()
    {
        var root = TestPathHelper.FindRepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src/WebMVC/Views/Speaking/Take.cshtml"));
        foreach (var attribute in new[] { "id=\"practice-data\"", "data-attempt-id", "data-question-id", "data-prep-seconds", "data-response-seconds", "data-remaining-seconds", "data-has-audio" })
            Assert.Contains(attribute, view);
    }

    [Fact]
    public void Writing_take_loads_script_after_modal_and_uses_external_navigation_handler()
    {
        var root = TestPathHelper.FindRepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src/WebMVC/Views/Writing/Take.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "src/WebMVC/wwwroot/js/writing/take.js"));
        Assert.Contains("_TakeEditor", view);
        Assert.Contains("writing-editor", File.ReadAllText(Path.Combine(root, "src/WebMVC/Views/Writing/_TakeEditor.cshtml")));
        Assert.Contains("id=\"submit-form\"", view);
        Assert.True(view.IndexOf("id=\"submit-modal\"", StringComparison.Ordinal) < view.IndexOf("~/js/writing/take.js", StringComparison.Ordinal));
        Assert.DoesNotContain("onclick=", view);
        Assert.DoesNotContain("@Url", script);
    }

    [Theory]
    [InlineData("Speaking")]
    [InlineData("Writing")]
    public void Free_response_views_have_no_inline_code_blocks(string area)
    {
        var root = TestPathHelper.FindRepoRoot();
        foreach (var path in Directory.GetFiles(Path.Combine(root, "src/WebMVC/Views", area), "*.cshtml"))
        {
            var view = File.ReadAllText(path);
            Assert.DoesNotMatch(@"<style\b", view);
            Assert.DoesNotContain("Math.", view);
            Assert.DoesNotContain(".Count(", view);
            Assert.DoesNotContain("onclick=", view);
            Assert.DoesNotMatch(@"<script(?![^>]*\bsrc\s*=)[^>]*>", view);
        }
    }

    [Fact]
    public void Writing_review_shows_each_located_error_its_correction_the_priority_fix_and_the_rewrite()
    {
        // E2.1 (#151): the page's job is to let the learner fix the text, so all three blocks are
        // rendered from the model's own fields and each one is optional on its own.
        var root = TestPathHelper.FindRepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src/WebMVC/Views/Writing/Review.cshtml"));

        Assert.Contains("q.ErrorSpans", view);
        Assert.Contains("span.OriginalSnippet", view);
        Assert.Contains("span.CorrectedSnippet", view);
        Assert.Contains("span.ExplanationVi", view);
        Assert.Contains("q.PriorityFix", view);
        Assert.Contains("q.ModelRewrite", view);
    }
}
