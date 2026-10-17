using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WebMVC.Models;

/// <summary>
/// Pure, deterministic formatter that converts AI insight Markdown into XSS-safe semantic HTML
/// without rendering raw HTML tags or clickable external links (ADR-0004).
/// </summary>
public static partial class AiNarrativeFormatter
{
    public static string ToSafeHtml(string? narrative)
    {
        if (string.IsNullOrWhiteSpace(narrative))
        {
            return string.Empty;
        }

        var normalized = narrative.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        var sb = new StringBuilder();
        var paragraphLines = new List<string>();
        var listItems = new List<string>();
        string? listType = null;

        void FlushParagraph()
        {
            if (paragraphLines.Count == 0)
            {
                return;
            }

            sb.Append("<p>");
            sb.Append(string.Join("<br />", paragraphLines));
            sb.Append("</p>");
            paragraphLines.Clear();
        }

        void FlushList()
        {
            if (listItems.Count == 0 || listType is null)
            {
                return;
            }

            sb.Append('<').Append(listType).Append('>');
            foreach (var item in listItems)
            {
                sb.Append("<li>").Append(item).Append("</li>");
            }
            sb.Append("</").Append(listType).Append('>');
            listItems.Clear();
            listType = null;
        }

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            var ulMatch = UnorderedListRegex().Match(trimmed);
            if (ulMatch.Success)
            {
                FlushParagraph();
                if (listType != "ul")
                {
                    FlushList();
                    listType = "ul";
                }
                listItems.Add(FormatInline(ulMatch.Groups[1].Value));
                continue;
            }

            var olMatch = OrderedListRegex().Match(trimmed);
            if (olMatch.Success)
            {
                FlushParagraph();
                if (listType != "ol")
                {
                    FlushList();
                    listType = "ol";
                }
                listItems.Add(FormatInline(olMatch.Groups[1].Value));
                continue;
            }

            FlushList();
            paragraphLines.Add(FormatInline(trimmed));
        }

        FlushParagraph();
        FlushList();

        return sb.ToString();
    }

    private static string FormatInline(string rawText)
    {
        // Step 1: HTML-encode all raw input first so raw <script>, <img>, <style>, <a> tags can never execute.
        var encoded = WebUtility.HtmlEncode(rawText);

        // Step 2: Extract inline code spans (`...`) into placeholders so markdown inside code is preserved literally.
        var codeSpans = new List<string>();
        encoded = InlineCodeRegex().Replace(encoded, match =>
        {
            var index = codeSpans.Count;
            codeSpans.Add(match.Groups[1].Value);
            return $"\u0000CODE_{index}\u0000";
        });

        // Step 3: Strip markdown link targets [label](url) -> label so links are never rendered as clickable anchors.
        encoded = MarkdownLinkRegex().Replace(encoded, "$1");

        // Step 4: Bold (**...**) and Italic (*...*).
        encoded = BoldRegex().Replace(encoded, "<strong>$1</strong>");
        encoded = ItalicRegex().Replace(encoded, "<em>$1</em>");

        // Step 5: Restore inline code placeholders.
        for (var i = 0; i < codeSpans.Count; i++)
        {
            encoded = encoded.Replace($"\u0000CODE_{i}\u0000", $"<code>{codeSpans[i]}</code>");
        }

        return encoded;
    }

    [GeneratedRegex(@"^[-*]\s+(.+)$")]
    private static partial Regex UnorderedListRegex();

    [GeneratedRegex(@"^\d+\.\s+(.+)$")]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)")]
    private static partial Regex ItalicRegex();
}
