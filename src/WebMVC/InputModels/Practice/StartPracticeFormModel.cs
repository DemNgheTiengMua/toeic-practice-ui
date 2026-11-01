namespace WebMVC.Models;

/// <summary>#155: the learner picks a section, not a drill recipe. The count is ten and the source is
/// mastery state, so neither is posted.</summary>
public sealed class StartPracticeFormModel
{
    public int SectionId { get; set; }

    public string FeedbackMode { get; set; } = "immediate";
}
