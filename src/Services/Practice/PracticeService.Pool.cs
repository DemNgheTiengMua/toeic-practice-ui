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
    /// #187: the latest version of every active item in one cell, so a drill serves what the pool
    /// currently says. Ordered least-recently-answered first, then by item, so a repeated draw is a
    /// reproducible choice rather than whatever the storage engine returns.
    /// <para>
    /// #235 replaces this with the one selector shared by path and free drilling; until then the
    /// ordering here is deliberately the minimum that never serves the same item twice in a row.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<(int ItemId, int ItemVersionId)>> PlanPoolItemsAsync(
        int userId, string modality, int topicId, CefrLevel level, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var items = await db.Items
            .AsNoTracking()
            .Where(i => i.TopicId == topicId
                && i.Modality == modality
                && i.CefrLevel == level
                && i.LifecycleStatus == "active")
            .Select(i => new
            {
                i.ItemId,
                LastAnsweredAt = db.PracticeAnswers
                    .Where(pa => pa.ItemId == i.ItemId && pa.Session.UserId == userId)
                    .Max(pa => (DateTime?)pa.AnsweredAt),
            })
            .ToListAsync(cancellationToken);

        var itemIds = items.Select(i => i.ItemId).ToList();
        var latestVersions = await db.ItemVersions
            .AsNoTracking()
            .Where(v => itemIds.Contains(v.ItemId))
            .GroupBy(v => v.ItemId)
            .Select(g => new { ItemId = g.Key, ItemVersionId = g.Max(v => v.ItemVersionId) })
            .ToDictionaryAsync(v => v.ItemId, v => v.ItemVersionId, cancellationToken);

        return items
            .Where(i => latestVersions.ContainsKey(i.ItemId))
            .OrderBy(i => i.LastAnsweredAt ?? DateTime.MinValue)
            .ThenBy(i => i.ItemId)
            .Take(SmartPracticePlanner.SessionSize)
            .Select(i => (i.ItemId, latestVersions[i.ItemId]))
            .ToList();
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
        var index = AttemptEngine.LabelIndex(version.KeyLabel);
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
        && string.Equals(selectedLabel, version.KeyLabel, StringComparison.OrdinalIgnoreCase);

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
