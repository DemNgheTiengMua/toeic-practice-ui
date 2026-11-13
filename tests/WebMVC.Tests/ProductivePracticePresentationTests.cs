using Contracts;

namespace WebMVC.Tests;

/// <summary>
/// #239: remaining non-retired assertions for <see cref="WordCount"/> and <c>Shared/_PriorAttempts.cshtml</c>
/// after Speaking and Writing studios moved to React + HeroUI v3 (<c>#299</c>).
/// </summary>
public sealed class ProductivePracticePresentationTests
{
    private static readonly string Root = TestPathHelper.FindRepoRoot();

    private static string View(string relative) =>
        File.ReadAllText(Path.Combine(Root, "src", "WebMVC", "Views", relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The value the page receives is <see cref="WordCount.Pattern"/> itself, not a literal.</summary>
    [Fact]
    public void The_pattern_the_controller_sends_is_the_servers_own()
    {
        Assert.Equal(@"[ \t\r\n]+", WordCount.Pattern);
        Assert.Equal(1, WordCount.Count("one\u00A0two"));
        Assert.Equal(2, WordCount.Count("one two"));
    }

    [Fact]
    public void The_prior_attemptlist_links_through_the_services_route_and_labels_every_status()
    {
        var partial = View("Shared/_PriorAttempts.cshtml");

        Assert.Contains("attempt.ReopenPath", partial);
        Assert.Contains("attempt.CanReview", partial);

        // D19: a stored code must not reach a text node.
        Assert.Contains("DisplayLabels.LabelForCode<AttemptStatusDto>", partial);
        Assert.DoesNotContain("@attempt.Status<", partial);
    }
}
