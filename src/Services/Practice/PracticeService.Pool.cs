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
