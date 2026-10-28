using Contracts;

namespace WebMVC.Models;

public sealed class StartPracticeFormModel
{
    public int SectionId { get; set; }

    public int QuestionCount { get; set; } = 20;

    public string QuestionSource { get; set; } = "all";

    public string FeedbackMode { get; set; } = "immediate";
}
