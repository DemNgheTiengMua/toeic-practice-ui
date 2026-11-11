using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #187: the pool half of a drill. A session drawn from a cell has no exam and no section behind it,
/// so it renders from <see cref="ItemVersion"/> and records the version it answered — the same seam
/// #186 established for attempts. The option-id bijection is <see cref="AttemptEngine"/>'s, not a
/// second one: two schemes for one concept is how a review screen shows the wrong option.
/// </summary>
public sealed partial class PracticeService
{
    /// <summary>
    /// #235 / D11: a free drill and a path step draw through <b>the same selector</b>
    /// (<see cref="ItemSelector"/>), so the plan and the practice can no longer contradict each other.
    /// The cell, the tier, the cooldowns and the ordering all live there; this method only turns the
    /// draw into the (item, version) pairs a session stores.
    /// <para>
    /// A drill is <b>unrestricted but honest about its size</b>: a cell holding fewer servable items than
    /// a session returns what exists and the session's own question count is that number, never padded
    /// with repeats.
    /// </para>
    /// </summary>
    private async Task<PoolSelection> PlanPoolItemsAsync(
        int userId, int certId, string modality, int topicId, CefrLevel level, string contentTier,
        CancellationToken cancellationToken)
    {
        var selector = new ItemSelector(db, _clock);

        return await selector.SelectAsync(
            userId, certId, skillIds: [], modality, topicId, level, contentTier,
            SmartPracticePlanner.SessionSize, cancellationToken);
    }

    /// <summary>
    /// The versions the given answers pinned, keyed by version id. Empty when no answer carries a pin,
    /// which is every session recorded before this ticket — the cheap empty result is what keeps the
    /// legacy path unevaluated.
    /// </summary>
    private async Task<Dictionary<int, ItemVersion>> LoadPinnedVersionsAsync(
        IEnumerable<PracticeAnswer> answers, CancellationToken cancellationToken)
    {
        var versionIds = answers
            .Where(a => a.ItemVersionId.HasValue)
            .Select(a => a.ItemVersionId!.Value)
            .Distinct()
            .ToList();

        return versionIds.Count == 0
            ? []
            : (await db.ItemVersions.AsNoTracking()
                .Where(v => versionIds.Contains(v.ItemVersionId))
                .ToListAsync(cancellationToken))
                .ToDictionary(v => v.ItemVersionId);
    }

    /// <summary>Places each answer on its pinned version, keyed by answer id.</summary>
    private static Dictionary<int, ItemVersion> PlacePinnedVersions(
        IEnumerable<PracticeAnswer> answers, IReadOnlyDictionary<int, ItemVersion> versions)
    {
        var pinned = new Dictionary<int, ItemVersion>();
        foreach (var answer in answers)
        {
            if (answer.ItemVersionId is int versionId && versions.TryGetValue(versionId, out var version))
            {
                pinned[answer.AnswerId] = version;
            }
        }

        return pinned;
    }

    /// <summary>
    /// #186's bijection, reused: a version's four options carry no ids, so the id is derived from
    /// (version, index) and is stable for the life of the version.
    /// </summary>
    private static IReadOnlyList<PracticeQuestionOption> PinnedOptions(int itemVersionId, ItemVersion version) =>
        AttemptEngine.IsProductive(version)
            ? []
            : AttemptEngine.OptionTexts(version)
                .Select((text, index) => new PracticeQuestionOption(
                    AttemptEngine.PoolOptionId(itemVersionId, index),
                    AttemptEngine.OptionLabel(index),
                    text ?? string.Empty))
                .ToList();

    private static int? PinnedSelectedOptionId(int itemVersionId, string? selectedLabel)
    {
        var index = AttemptEngine.LabelIndex(selectedLabel);
        return index < 0 ? null : AttemptEngine.PoolOptionId(itemVersionId, index);
    }

    private static int? PinnedCorrectOptionId(int itemVersionId, ItemVersion version)
    {
        // #250: a key can name more than one correct option ('A,C' for multi_select). The review screen
        // marks every correct option from the version's own label set (AttemptEngine.PoolReviewOptions),
        // and this single id is the fallback the DTO carries. The first label is the honest choice: for
        // a one-label key it is exactly the previous behaviour, and for a set it names one of the
        // answers rather than none.
        var first = AttemptEngine.KeyLabels(version.KeyLabel).OrderBy(label => label, StringComparer.Ordinal).FirstOrDefault();
        var index = AttemptEngine.LabelIndex(first);
        return index < 0 ? null : AttemptEngine.PoolOptionId(itemVersionId, index);
    }

    private static string? PinnedOptionText(ItemVersion version, string? label) => label switch
    {
        "A" => version.OptionA,
        "B" => version.OptionB,
        "C" => version.OptionC,
        "D" => version.OptionD,
        _ => null,
    };

    /// <summary>
    /// #187: a pool answer is right when the label it recorded is the pinned version's key. The legacy
    /// question's options are not consulted at all — they may disagree with the version the learner sat,
    /// and the version is the one that scores.
    /// </summary>
    private static bool EvaluatePinnedAnswer(ItemVersion version, string? selectedLabel) =>
        selectedLabel is not null
        && AttemptEngine.LabelIndex(selectedLabel) >= 0
        && AttemptEngine.IsKeyLabel(version, selectedLabel);

    /// <summary>
    /// The render format a pinned version declares, as this codebase's <see cref="QuestionType"/> code.
    /// <para>
    /// <b>Why the shape is read from the key rather than the options.</b> "Has options" answers *what to
    /// draw*, and every drill screen also needs *how to grade*. A blank-shaped <c>inline_completion</c>
    /// has no options and is still objectively judged; a <c>multi_select</c> has options and a key that
    /// names two of them. One method so the take screen, the summary and the grader cannot disagree —
    /// measured failure it fixes: a <c>multi_select</c> rendered as a single-choice mcq discarded the
    /// learner's second choice, and a blank reported as <c>free_response</c> arrived with no accepted
    /// answers, so the drill graded nothing.
    /// </para>
    /// </summary>
    private static string RenderTypeOf(ItemVersion version)
    {
        if (version.OptionA is not null)
        {
            return AttemptEngine.KeyLabels(version.KeyLabel).Count > 1
                ? QuestionType.MultiSelect.ToCode()
                : QuestionType.Mcq.ToCode();
        }

        return string.IsNullOrWhiteSpace(version.AcceptedAnswersJson)
            ? QuestionType.FreeResponse.ToCode()
            : QuestionType.InlineCompletion.ToCode();
    }

    /// <summary>
    /// Whether a pinned version can be judged WITHOUT the AI queue. It can when it carries a key (a
    /// single label or a label set) or accepted answers.
    /// <para>
    /// <b>Why this is not <see cref="AttemptEngine.IsProductive"/>.</b> That method asks "has it options
    /// to render", and a blank-shaped <c>inline_completion</c> answers no to both - so using it as the
    /// grading test left a text blank and every <c>multi_select</c> drill recording NO verdict at all.
    /// Measured: a multi_select keyed <c>A,B</c> answered with both labels stored <c>IsCorrect = null</c>,
    /// and the summary then reported the learner had answered but been judged on nothing.
    /// </para>
    /// </summary>
    private static bool IsObjectivelyGradable(ItemVersion version) =>
        !string.IsNullOrWhiteSpace(version.KeyLabel)
        || !string.IsNullOrWhiteSpace(version.AcceptedAnswersJson);

    /// <summary>
    /// The verdict for a pool drill answer, by the shape the version declares. A label set is compared
    /// as a SET - a <c>multi_select</c> keyed <c>A,B</c> is right only when both and neither more are
    /// chosen - and text is judged by the version's accepted answers. Falls back to the label rule, so
    /// a plain mcq is graded exactly as before.
    /// </summary>
    private static bool EvaluatePinnedResponse(
        ItemVersion version, string? selectedLabel, string? selectedOptionIdsJson, string? responseText)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            return AttemptEngine.EvaluatePinnedText(version, responseText);
        }

        var key = AttemptEngine.KeyLabels(version.KeyLabel);
        if (key.Count > 1)
        {
            // Decode each chosen pool id back to its label: an id is (version, index), so an id from
            // another version decodes to -1 and is dropped rather than counted.
            var chosen = AttemptEngine
                .ParseOptionIds(selectedOptionIdsJson, fallbackOptionId: null)
                .Select(id => AttemptEngine.PoolOptionIndex(id, version.ItemVersionId))
                .Where(index => index >= 0)
                .Select(AttemptEngine.OptionLabel)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return chosen.SetEquals(key);
        }

        return EvaluatePinnedAnswer(version, selectedLabel);
    }

    /// <summary>
    /// The option the learner picked, expressed as the id every screen renders: the pinned answer's
    /// label decodes against its version, and a legacy answer keeps its own id.
    /// </summary>
    private static int? ProjectedOptionId(PracticeAnswer answer, ItemVersion? pinned) =>
        pinned is null
            ? answer.SelectedOptionId
            : PinnedSelectedOptionId(pinned.ItemVersionId, answer.SelectedLabel);

    /// <summary>Where a session sits: its section for a section drill, its topic for a pool drill.</summary>
    private static (string Code, string Name) ScopeLabels(PracticeSession session) =>
        session.Section is { } section
            ? (section.Code, section.Name)
            : session.Topic is { } topic
                ? (topic.Code, topic.Name)
                : ("", "");

    private static PracticeSessionInfo ToSessionInfo(PracticeSession session)
    {
        var (code, name) = ScopeLabels(session);
        return new PracticeSessionInfo(
            session.SessionId,
            session.SectionId,
            code,
            name,
            EnumCodes.Parse<Modality>(session.Modality),
            session.QuestionCount,
            EnumCodes.Parse<PracticeStatus>(session.Status),
            session.CorrectCount,
            EnumCodes.Parse<QuestionSource>(session.QuestionSource),
            session.StartedAt,
            session.FinishedAt,
            session.TopicId,
            session.CefrLevel);
    }

    /// <summary>
    /// The same shape for a session just created, whose scope row was read with AsNoTracking: assigning
    /// it to the tracked navigation would attach a second instance of a key the context may already
    /// hold, so the labels are passed rather than fixed up.
    /// </summary>
    private static PracticeSessionInfo ToSessionInfo(PracticeSession session, string code, string name) =>
        ToSessionInfo(session) with { SectionCode = code, SectionName = name };
}
