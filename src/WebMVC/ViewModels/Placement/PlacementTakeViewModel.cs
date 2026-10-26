using System.Text.Json;
using Contracts;

namespace WebMVC.Models;

public sealed record PlacementTakeViewModel(
    PlacementTakeViewDto Take,
    string? ErrorMessage = null)
{
    public AttemptQuestionDto Question { get; } = Take.CurrentQuestion;
    public string PageTitle { get; } = $"Placement Test — Question {Take.CurrentQuestion.DisplayOrder} — extra efficient";
    public bool HasError { get; } = !string.IsNullOrEmpty(ErrorMessage);
    public bool HasPassage { get; } = !string.IsNullOrEmpty(Take.CurrentQuestion.Passage);
    public bool HasImage { get; } = !string.IsNullOrEmpty(Take.CurrentQuestion.ImagePath);
    public string ImageAlt { get; } = Take.CurrentQuestion.ImageAlt ?? "Question illustration";
    public bool IsWriting { get; } = Take.CurrentQuestion.QuestionType is "free_response" or "writing";
    public string FlagButtonClass { get; } = Take.CurrentQuestion.IsFlagged ? "btn--primary" : "";
    public string FlagPressed { get; } = Take.CurrentQuestion.IsFlagged ? "true" : "false";
    public string FlagLabel { get; } = Take.CurrentQuestion.IsFlagged ? "Flagged for review" : "Flag for review";
    public string? PreviousUrl { get; } = Take.CurrentQuestion.DisplayOrder > 1
        ? $"/placement/take/{Take.PlacementId}?q={Take.CurrentQuestion.DisplayOrder - 1}" : null;
    public string? NextUrl { get; } = Take.CurrentQuestion.DisplayOrder < Take.TotalQuestions
        ? $"/placement/take/{Take.PlacementId}?q={Take.CurrentQuestion.DisplayOrder + 1}" : null;
    public string SubmitUrl { get; } = $"/placement/take/{Take.PlacementId}/submit";
    public int ProgressPercent { get; } = GetProgressPercent(Take);
    public bool ShowPhases { get; } = Take.Phases?.Count > 1;
    public IReadOnlyList<PhaseDisplay> Phases { get; } = (Take.Phases ?? [])
        .Select(phase => new PhaseDisplay(phase.SkillCode, phase.SkillName,
            Take.CurrentQuestion.SkillCode == phase.SkillCode ? "step" : "false")).ToArray();
    public IReadOnlyList<OptionDisplay> Options { get; } = Take.CurrentQuestion.Options
        .Select(option => new OptionDisplay(option.OptionId, option.Label, option.Text,
            option.OptionId == Take.CurrentQuestion.SelectedOptionId,
            option.OptionId == Take.CurrentQuestion.SelectedOptionId ? "is-selected" : "")).ToArray();
    public IReadOnlyList<NavigatorGroupDisplay> NavigatorGroups { get; } = BuildNavigator(Take);
    public ExamAudioPlayerModel? AudioPlayer { get; } = BuildAudioPlayer(Take.CurrentQuestion);
    public string PageDataJson { get; } = BuildPageData(Take);

    private static int GetProgressPercent(PlacementTakeViewDto take) =>
        (int)Math.Round((double)take.AnsweredCount / Math.Max(1, take.TotalQuestions) * 100);

    private static ExamAudioPlayerModel? BuildAudioPlayer(AttemptQuestionDto question) =>
        string.IsNullOrEmpty(question.AudioPath) ? null : new ExamAudioPlayerModel(
            AudioPath: question.AudioPath,
            AllowReplay: false,
            SectionTitle: "Diagnostic Listening Audio",
            BadgeText: "READY",
            StartMs: question.AudioStartMs,
            EndMs: question.AudioEndMs,
            Mode: "exam");

    private static IReadOnlyList<NavigatorGroupDisplay> BuildNavigator(PlacementTakeViewDto take) =>
        take.Navigator
            .GroupBy(item => new { item.SectionId, item.SectionCode, item.Modality })
            .OrderBy(group => group.Min(item => item.DisplayOrder))
            .Select(group =>
            {
                var items = group.OrderBy(item => item.DisplayOrder).ToArray();
                var first = items[0].DisplayOrder;
                var last = items[^1].DisplayOrder;
                return new NavigatorGroupDisplay(
                    group.Key.Modality.ToString(),
                    $"{first}–{last}",
                    $"Questions {first} to {last}",
                    items.Select(item => new NavigatorCellDisplay(
                        item.QuestionId,
                        item.DisplayOrder,
                        $"/placement/take/{take.PlacementId}?q={item.DisplayOrder}",
                        "navigator__cell" + (item.IsCurrent ? " is-current" : "")
                            + (item.IsAnswered ? " is-answered" : " is-unanswered")
                            + (item.IsFlagged ? " is-flagged" : ""),
                        $"Question {item.DisplayOrder} {(item.IsAnswered ? "answered" : "unanswered")}",
                        item.IsCurrent ? "true" : "false")).ToArray());
            }).ToArray();

    private static string BuildPageData(PlacementTakeViewDto take)
    {
        var question = take.CurrentQuestion;
        var audioKey = (question.AudioPath ?? "")
            + (question.AudioStartMs.HasValue || question.AudioEndMs.HasValue
                ? $":{question.AudioStartMs ?? 0}-{question.AudioEndMs ?? 0}" : "");
        // The default JSON encoder escapes HTML delimiters before this enters a script data island.
        return JsonSerializer.Serialize(new
        {
            placementId = take.PlacementId,
            questionId = question.QuestionId,
            remainingSeconds = take.RemainingSeconds,
            isFlagged = question.IsFlagged,
            totalQuestions = take.TotalQuestions,
            answeredCount = take.AnsweredCount,
            progressPercent = GetProgressPercent(take),
            audioKey
        });
    }

    public sealed record PhaseDisplay(string SkillCode, string SkillName, string AriaCurrent);
    public sealed record OptionDisplay(int OptionId, string Label, string Text, bool IsSelected, string CssClass);
    public sealed record NavigatorGroupDisplay(
        string Title, string RangeLabel, string AriaLabel, IReadOnlyList<NavigatorCellDisplay> Items);
    public sealed record NavigatorCellDisplay(
        int QuestionId, int DisplayOrder, string Url, string CssClass, string AriaLabel, string AriaCurrent);
}
