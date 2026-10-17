using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class AiNarrativeFormatterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    public void ToSafeHtml_returns_empty_string_for_null_or_whitespace(string? input)
    {
        Assert.Equal(string.Empty, AiNarrativeFormatter.ToSafeHtml(input));
    }

    [Fact]
    public void ToSafeHtml_renders_bold_italic_and_inline_code_within_paragraphs()
    {
        var markdown = "Your **Part 5** accuracy is *improving* when applying `subject-verb` rules.\n\nKeep practicing **daily**.";

        var html = AiNarrativeFormatter.ToSafeHtml(markdown);

        Assert.Equal(
            "<p>Your <strong>Part 5</strong> accuracy is <em>improving</em> when applying <code>subject-verb</code> rules.</p>" +
            "<p>Keep practicing <strong>daily</strong>.</p>",
            html);
    }

    [Fact]
    public void ToSafeHtml_renders_unordered_and_ordered_lists()
    {
        var markdown = """
            Key findings:
            - Drill **Part 3** conversations
            * Review *inference* traps

            Next steps:
            1. Complete 10 questions
            2. Reconcile `SavedQuestions`
            """;

        var html = AiNarrativeFormatter.ToSafeHtml(markdown);

        Assert.Contains("<p>Key findings:</p>", html);
        Assert.Contains("<ul><li>Drill <strong>Part 3</strong> conversations</li><li>Review <em>inference</em> traps</li></ul>", html);
        Assert.Contains("<p>Next steps:</p>", html);
        Assert.Contains("<ol><li>Complete 10 questions</li><li>Reconcile <code>SavedQuestions</code></li></ol>", html);
    }

    [Fact]
    public void ToSafeHtml_html_encodes_raw_html_and_script_payloads()
    {
        var malicious = "<script>alert(1)</script> **<img src=x onerror=alert(1)>** <style>body{display:none}</style> <a href=\"javascript:alert(1)\">click</a>";

        var html = AiNarrativeFormatter.ToSafeHtml(malicious);

        Assert.DoesNotContain("<script", html, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", html, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a ", html, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("<strong>&lt;img src=x onerror=alert(1)&gt;</strong>", html);
    }

    [Fact]
    public void ToSafeHtml_never_renders_markdown_or_raw_links_as_clickable_anchors()
    {
        var markdown = "Visit [External Site](https://example.com) or [Trap](javascript:alert(1)) or https://example.com/phish";

        var html = AiNarrativeFormatter.ToSafeHtml(markdown);

        Assert.DoesNotContain("<a", html, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=", html, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("External Site", html);
    }
}
