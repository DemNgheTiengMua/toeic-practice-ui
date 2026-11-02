using System.IO;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Two defects found by walking the real result page with a learner account, both invisible to the
/// suite because nothing rendered this view with a scored attempt.
/// </summary>
public sealed class ExamResultPresentationTests
{
    private static string View()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var path = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Result.cshtml");
        Assert.True(File.Exists(path), "Exams/Result.cshtml must exist");
        return File.ReadAllText(path);
    }

    [Fact]
    public void The_band_is_shown_by_name_not_by_its_database_code()
    {
        // A TOEIC attempt with zero correct answers places the learner in the band whose code is
        // "below_A1". The page printed that raw, underscore and all, because it read BandCode while
        // the band table's own name sat unused in the same view model.
        var view = View();

        Assert.DoesNotContain("@res.BandCode</span>", view, System.StringComparison.Ordinal);
        Assert.Contains("res.BandName", view, System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_score_delta_placard_follows_the_sign_rather_than_always_reading_success()
    {
        // The delta block was painted sign-green unconditionally, so a learner who dropped 710 points
        // saw the fall on a success-coloured placard. The colour carries the verdict here.
        var view = View();
        Assert.Contains("score-hero__delta--down", view, System.StringComparison.Ordinal);
        Assert.Contains("score-hero__delta--up", view, System.StringComparison.Ordinal);

        var repoRoot = TestPathHelper.FindRepoRoot();
        var css = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "css", "components.css"));
        Assert.Contains(".score-hero__delta--down { background: var(--sign-red); }", css, System.StringComparison.Ordinal);
    }
}
