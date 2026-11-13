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
    public bool IsSpeaking { get; } = Take.CurrentQuestion.QuestionType is "free_response"
        && Take.CurrentQuestion.Modality == ModalityDto.Speaking;
    public bool IsWriting { get; } = Take.CurrentQuestion.QuestionType is "free_response" or "writing"
        && Take.CurrentQuestion.Modality != ModalityDto.Speaking;
    public bool HasSpeakingAudio { get; } = !string.IsNullOrEmpty(Take.CurrentQuestion.ResponseAudioPath);
    public string SpeakingAudioUrl { get; } = $"/speaking/audio/{Take.AttemptId}/{Take.CurrentQuestion.QuestionId}";
    public int PrepSeconds { get; } = Take.CurrentQuestion.PrepSeconds is > 0 ? Take.CurrentQuestion.PrepSeconds.Value : 15;
    public int ResponseSeconds { get; } = Take.CurrentQuestion.ResponseSeconds is > 0 ? Take.CurrentQuestion.ResponseSeconds.Value : 30;
    public string ResponseTimerText { get; } = FormatTimer(Take.CurrentQuestion.ResponseSeconds is > 0 ? Take.CurrentQuestion.ResponseSeconds.Value : 30);
    public string FlagButtonClass { get; } = Take.CurrentQuestion.IsFlagged ? "btn--primary" : "";
    public string FlagPressed { get; } = Take.CurrentQuestion.IsFlagged ? "true" : "false";
    public string FlagLabel { get; } = Take.CurrentQuestion.IsFlagged ? "Flagged for review" : "Flag for review";
    // One-way sections block rewinding the server-side position (D3). Mirror ExamTakeQuestionPresentation
    // so the placement sitting never offers a control the API would refuse.
    public bool HidePrevious { get; } = IsOneWayNavigation(Take.CurrentQuestion);
    public bool CanGoPrevious { get; } = CanGoBack(Take);
    public string? PreviousUrl { get; } = CanGoBack(Take)
        ? $"/placement/take/{Take.PlacementId}?q={Take.CurrentQuestion.DisplayOrder - 1}" : null;
    public string? NextUrl { get; } = Take.CurrentQuestion.DisplayOrder < Take.TotalQuestions
        ? $"/placement/take/{Take.PlacementId}?q={Take.CurrentQuestion.DisplayOrder + 1}" : null;
    // Moving past the last question of a skill, or of a locked question group, closes it for good,
    // so the Next control confirms first, as the mock exam does before a skill advance and before
    // leaving a HYBRID_GROUP_LOCKED group.
    public string? NextConfirm { get; } = LeavingLockedPartConfirmation(Take);
    public string SubmitUrl { get; } = $"/placement/take/{Take.PlacementId}/submit";
    public int ProgressPercent { get; } = GetProgressPercent(Take);
    public bool ShowPhases { get; } = Take.Phases?.Count > 1;
    public IReadOnlyList<PhaseDisplay> Phases { get; } = BuildPhases(Take);
    public IReadOnlyList<OptionDisplay> Options { get; } = Take.CurrentQuestion.Options
        .Select(option => new OptionDisplay(option.OptionId, option.Label, option.Text,
            option.OptionId == Take.CurrentQuestion.SelectedOptionId,
            option.OptionId == Take.CurrentQuestion.SelectedOptionId ? "is-selected" : "")).ToArray();
    public IReadOnlyList<NavigatorGroupDisplay> NavigatorGroups { get; } = BuildNavigator(Take);
    public ExamAudioPlayerModel? AudioPlayer { get; } = BuildAudioPlayer(Take.CurrentQuestion);
    public string PageDataJson { get; } = BuildPageData(Take);

    /// <summary>
    /// #221 / D18: the one labelled settings control, scoped to what this paper actually examines.
    /// A reading-only placement gets <c>null</c> and therefore no audio control at all - the criterion
    /// that a gate which always offers the microphone would fail while looking correct.
    /// <para>
    /// Derived from the paper's own questions, not from the certificate: a certificate that examines
    /// speaking but places it in a separate paper must not put a microphone control on this one.
    /// </para>
    /// </summary>
    public AudioEquipmentCheckModel? AudioEquipment { get; } = BuildAudioEquipment(Take);

    private static AudioEquipmentCheckModel? BuildAudioEquipment(PlacementTakeViewDto take)
    {
        var questions = take.Questions ?? [take.CurrentQuestion];
        var hasListening = questions.Any(q => q.Modality == ModalityDto.Listening);
        var hasSpeaking = questions.Any(q => q.Modality == ModalityDto.Speaking);
        return hasListening || hasSpeaking
            ? new AudioEquipmentCheckModel(ShowInput: hasSpeaking, ShowOutput: hasListening, Sitting: true)
            : null;
    }

    /// <summary>
    /// #217 / PLC-18: EVERY section of the sitting with its outstanding count, so the confirmation can
    /// name the count per section and the client can move each row's number as the learner answers
    /// without a second copy of the sitting in the page. A section with zero outstanding is present
    /// but hidden, which is what keeps the table from reshuffling as counts fall.
    /// </summary>
    public IReadOnlyList<UnansweredSectionDisplay> Sections { get; } = BuildSections(Take);

    /// <summary>True when anything is still outstanding, i.e. the confirmation's cost is real.</summary>
    public bool HasUnanswered => Sections.Any(section => section.UnansweredCount > 0);

    public int UnansweredTotal => Sections.Sum(section => section.UnansweredCount);

    private static int GetProgressPercent(PlacementTakeViewDto take) =>
        (int)Math.Round((double)take.AnsweredCount / Math.Max(1, take.TotalQuestions) * 100);

    private static bool IsOneWayNavigation(AttemptQuestionDto question) =>
        question.NavigationMode is "AUDIO_DRIVEN" or "SYSTEM_PACED_AUTO";

    // ...and never into a question the server has locked, such as the end of a finished skill.
    private static bool CanGoBack(PlacementTakeViewDto take) =>
        take.CurrentQuestion.DisplayOrder > 1
        && !IsOneWayNavigation(take.CurrentQuestion)
        && take.Navigator.FirstOrDefault(n => n.DisplayOrder == take.CurrentQuestion.DisplayOrder - 1)?.IsLocked != true;

    private static string? LeavingLockedPartConfirmation(PlacementTakeViewDto take)
    {
        var current = take.CurrentQuestion;
        var next = take.Questions?.FirstOrDefault(q => q.DisplayOrder == current.DisplayOrder + 1);
        if (next is null)
        {
            return null;
        }

        if (next.SkillCode != current.SkillCode)
        {
            return $"You are about to finish {current.SkillName} and start {next.SkillName}. "
                   + $"You cannot return to {current.SkillName} afterwards. Continue?";
        }

        return current.NavigationMode == "HYBRID_GROUP_LOCKED" && next.GroupId != current.GroupId
            ? "You are about to finish this question group. You cannot return to it or change its answers "
              + "afterwards. Continue?"
            : null;
    }

    // #214 / PLC-12: a placement has no countdown, so the strip states where each skill stands rather
    // than how long is left. A finished skill is closed for good; the current one is being answered; a
    // later one opens when the learner reaches it.
    private static IReadOnlyList<PhaseDisplay> BuildPhases(PlacementTakeViewDto take)
    {
        var phases = take.Phases ?? [];
        var active = phases.ToList().FindIndex(phase => phase.SkillCode == take.CurrentQuestion.SkillCode);
        return phases.Select((phase, index) => new PhaseDisplay(
            phase.SkillCode,
            phase.SkillName,
            index == active ? "step" : "false",
            index < active ? "Closed"
                : index == active ? "In progress"
                : "Not started")).ToArray();
    }

    private static ExamAudioPlayerModel? BuildAudioPlayer(AttemptQuestionDto question) =>
        string.IsNullOrEmpty(question.AudioPath) ? null : new ExamAudioPlayerModel(
            AudioPath: question.AudioPath,
            AllowReplay: false,
            SectionTitle: $"{question.SkillName} audio",
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
                        item.SectionId,
                        item.DisplayOrder,
                        $"/placement/take/{take.PlacementId}?q={item.DisplayOrder}",
                        "navigator__cell" + (item.IsCurrent ? " is-current" : "")
                            + (item.IsAnswered ? " is-answered" : " is-unanswered")
                            + (item.IsFlagged ? " is-marked" : "") + (item.IsLocked ? " is-locked" : ""),
                        $"Question {item.DisplayOrder} {(item.IsAnswered ? "answered" : "unanswered")}",
                        item.IsCurrent ? "true" : "false",
                        item.IsLocked)).ToArray());
            }).ToArray();

    /// <summary>
    /// #217 / PLC-18: the unanswered count PER SECTION, which is what the submission confirmation
    /// names. Counted from the navigator's own <c>IsAnswered</c> flag rather than from the paper,
    /// because a paper total cannot tell a learner whether they abandoned a whole skill or missed two
    /// items in the one they were on - and only the section grain tells them where to go back.
    /// <para>
    /// Ordered by each section's first question, so the modal lists sections in sitting order. A
    /// section with nothing outstanding keeps its row and is hidden by the view: dropping it would
    /// reshuffle the table each time a count reached zero.
    /// </para>
    /// </summary>
    private static IReadOnlyList<UnansweredSectionDisplay> BuildSections(PlacementTakeViewDto take)
    {
        // The display name lives on the question rows, not on a navigator cell; a sitting that has no
        // Questions projection falls back to the section code rather than printing a blank heading.
        var names = (take.Questions ?? [])
            .GroupBy(question => question.SectionId)
            .ToDictionary(group => group.Key, group => group.First().SectionName);

        return take.Navigator
            .GroupBy(item => item.SectionId)
            .Select(group => new UnansweredSectionDisplay(
                group.Key,
                names.TryGetValue(group.Key, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : group.First().SectionCode,
                group.Count(),
                group.Count(item => !item.IsAnswered)))
            .OrderBy(section => take.Navigator
                .Where(item => item.SectionId == section.SectionId)
                .Min(item => item.DisplayOrder))
            .ToArray();
    }

    private static string FormatTimer(int seconds) =>
        $"{seconds / 60:D2}:{seconds % 60:D2}";

    private static string BuildPageData(PlacementTakeViewDto take)
    {
        var question = take.CurrentQuestion;
        var audioKey = (question.AudioPath ?? "")
            + (question.AudioStartMs.HasValue || question.AudioEndMs.HasValue
                ? $":{question.AudioStartMs ?? 0}-{question.AudioEndMs ?? 0}" : "");
        // The default JSON encoder escapes HTML delimiters before this enters a script data island.
        // #214 / PLC-12: no remainingSeconds - there is no countdown to feed.
        return JsonSerializer.Serialize(new
        {
            placementId = take.PlacementId,
            attemptId = take.AttemptId,
            questionId = question.QuestionId,
            displayOrder = question.DisplayOrder,
            isFlagged = question.IsFlagged,
            totalQuestions = take.TotalQuestions,
            answeredCount = take.AnsweredCount,
            progressPercent = GetProgressPercent(take),
            audioKey,
            isSpeaking = question.QuestionType == "free_response" && question.Modality == ModalityDto.Speaking,
            prepSeconds = question.PrepSeconds is > 0 ? question.PrepSeconds.Value : 15,
            responseSeconds = question.ResponseSeconds is > 0 ? question.ResponseSeconds.Value : 30,
            hasSpeakingAudio = !string.IsNullOrEmpty(question.ResponseAudioPath)
        });
    }

    public sealed record PhaseDisplay(string SkillCode, string SkillName, string AriaCurrent, string StateText);
    public sealed record OptionDisplay(int OptionId, string Label, string Text, bool IsSelected, string CssClass);
    public sealed record NavigatorGroupDisplay(
        string Title, string RangeLabel, string AriaLabel, IReadOnlyList<NavigatorCellDisplay> Items);
    public sealed record NavigatorCellDisplay(
        int QuestionId, int SectionId, int DisplayOrder, string Url, string CssClass, string AriaLabel, string AriaCurrent, bool IsLocked);

    /// <summary>
    /// #217 / PLC-18: one section of the sitting with its outstanding count, for the submit
    /// confirmation. `UnansweredCount` and `TotalCount` are separate so the modal can print
    /// "2 of 8 unanswered in Reading" rather than a number with no denominator.
    /// </summary>
    public sealed record UnansweredSectionDisplay(
        int SectionId, string SectionName, int TotalCount, int UnansweredCount);
}
