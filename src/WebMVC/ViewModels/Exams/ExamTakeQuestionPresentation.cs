using Contracts;

namespace WebMVC.Models;

public sealed record ExamTakeQuestionPresentation
{
    public required int QuestionId { get; init; }
    public required int DisplayOrder { get; init; }
    public required string SkillName { get; init; }
    public required string SectionName { get; init; }
    public required string Stem { get; init; }
    public string? Passage { get; init; }
    public string? ImagePath { get; init; }
    public required string ImageAlt { get; init; }
    public bool HasPassage { get; init; }
    public bool HasImage { get; init; }
    public bool RequireImage { get; init; }
    public bool HideOptionText { get; init; }
    public bool IsCluster { get; init; }
    public bool HideStem { get; init; }
    public bool IsFreeResponse { get; init; }
    public bool HasRequiredKeywords { get; init; }
    public bool IsSystemPacedAuto { get; init; }
    public int PrepSeconds { get; init; }
    public int ResponseSeconds { get; init; }
    public string? ResponseText { get; init; }
    public required string FlagClass { get; init; }
    public required string FlagState { get; init; }
    public required string FlagLabel { get; init; }
    public bool CanGoPrevious { get; init; }
    public bool HidePrevious { get; init; }
    public required string NextLabel { get; init; }
    public required IReadOnlyList<ExamTakeOptionPresentation> Options { get; init; }

    internal static ExamTakeQuestionPresentation Create(AttemptQuestionDto q, int totalQuestions, bool clustered = false)
    {
        var options = !clustered && q.HideOptionText && q.OptionCount > 0 ? q.Options.Take(q.OptionCount) : q.Options;
        return new ExamTakeQuestionPresentation
        {
            QuestionId = q.QuestionId,
            DisplayOrder = q.DisplayOrder,
            SkillName = q.SkillName,
            SectionName = q.SectionName,
            Stem = q.Stem,
            Passage = q.Passage,
            ImagePath = q.ImagePath,
            ImageAlt = q.ImageAlt ?? "Question illustration",
            HasPassage = !string.IsNullOrEmpty(q.Passage),
            HasImage = !string.IsNullOrEmpty(q.ImagePath),
            RequireImage = q.RequireImage,
            HideOptionText = q.HideOptionText,
            IsCluster = q.ClusterSize == 3,
            HideStem = q.ClusterSize == 3 || (q.HideOptionText && q.OptionCount == 3),
            IsFreeResponse = q.QuestionType == "free_response",
            HasRequiredKeywords = !string.IsNullOrWhiteSpace(q.RequiredKeywords),
            IsSystemPacedAuto = q.NavigationMode == "SYSTEM_PACED_AUTO",
            PrepSeconds = q.PrepSeconds ?? 15,
            ResponseSeconds = q.ResponseSeconds ?? 30,
            ResponseText = q.ResponseText,
            FlagClass = q.IsFlagged ? "btn--primary" : "",
            FlagState = q.IsFlagged ? "true" : "false",
            FlagLabel = q.IsFlagged ? "Flagged for review" : "Flag for review",
            CanGoPrevious = q.DisplayOrder > 1,
            HidePrevious = q.NavigationMode is "AUDIO_DRIVEN" or "SYSTEM_PACED_AUTO",
            NextLabel = q.DisplayOrder >= totalQuestions ? "Review & Submit exam" : "Next question",
            Options = options.Select(opt => new ExamTakeOptionPresentation(opt.OptionId, opt.Label, opt.Text, opt.OptionId == q.SelectedOptionId)).ToArray()
        };
    }
}
