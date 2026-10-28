using Contracts;
namespace WebMVC.Models;

public sealed record LessonCardPresentation(LessonCardDto Card, int Index)
{
    public string ContentHtml { get; } = AiNarrativeFormatter.ToSafeHtml(Card.ContentMarkdown);
    public string CssClass => Card.CardType switch
    {
        "rule" => "lesson-card--rule", "pitfall" => "lesson-card--pitfall",
        "example" => "lesson-card--example", _ => ""
    };
}
