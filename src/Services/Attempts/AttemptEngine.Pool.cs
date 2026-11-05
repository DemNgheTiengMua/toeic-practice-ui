using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    /// <summary>
    /// #186: a paper position resolved through the pool — the item, the version the binding pins, and
    /// the version row itself. This is the whole of "reads through item versions": the binding is
    /// placed on the paper by (section, 1-based position within that section), because that pair is
    /// what <c>UQ_ExamItems_Exam_Section_Order</c> makes unique and what the sitting projection
    /// already orders by. A binding names no <c>QuestionId</c>, so position is the only join there is.
    /// </summary>
    private sealed record BoundPaperPosition(int ItemId, int ItemVersionId, ItemVersion Version);

    /// <summary>
    /// The exam's bindings, keyed by paper position. Empty for a paper the pool has not assembled,
    /// which is every paper #231 has not reached yet — the cheap empty result is what keeps the
    /// legacy path untouched and unevaluated for those.
    /// </summary>
    private Task<Dictionary<(int SectionId, int DisplayOrder), BoundPaperPosition>> LoadPaperBindingsAsync(
        int examId,
        CancellationToken cancellationToken) =>
        db.ExamItems
            .AsNoTracking()
            .Where(b => b.ExamId == examId)
            .Join(
                db.ItemVersions.AsNoTracking(),
                b => b.ItemVersionId,
                v => v.ItemVersionId,
                (b, v) => new { b.SectionId, b.DisplayOrder, b.ItemId, b.ItemVersionId, Version = v })
            .ToDictionaryAsync(
                b => (b.SectionId, b.DisplayOrder),
                b => new BoundPaperPosition(b.ItemId, b.ItemVersionId, b.Version),
                cancellationToken);

    /// <summary>
    /// Places the bindings on the ordered paper. <paramref name="questions"/> must be in sitting
    /// order (the order <see cref="LoadExamQuestionsAsync"/> returns), because a position is a rank
    /// within its section, not a column.
    /// </summary>
    private static Dictionary<int, BoundPaperPosition> PlaceBindings(
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<(int SectionId, int DisplayOrder), BoundPaperPosition> bindings)
    {
        var placed = new Dictionary<int, BoundPaperPosition>();
        if (bindings.Count == 0)
        {
            return placed;
        }

        var positionInSection = new Dictionary<int, int>();
        foreach (var question in questions)
        {
            var position = positionInSection[question.SectionId] = positionInSection.GetValueOrDefault(question.SectionId) + 1;
            if (bindings.TryGetValue((question.SectionId, position), out var bound))
            {
                placed[question.QuestionId] = bound;
            }
        }

        return placed;
    }

    /// <summary>
    /// #186: a bound position must render something. The schema already forbids a binding without a
    /// version (<c>ExamItems.ItemVersionId</c> is NOT NULL and FK-enforced), so the reachable form of
    /// "this paper's binding points at nothing" is a position with no question behind it — a paper
    /// half-assembled by an edit that removed a question from a bound section. An attempt is refused
    /// before the first write rather than created as a paper with a hole in it.
    /// </summary>
    private static void EnsureBindingsRender(
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<(int SectionId, int DisplayOrder), BoundPaperPosition> bindings)
    {
        if (bindings.Count == 0)
        {
            return;
        }

        var positions = new HashSet<(int SectionId, int DisplayOrder)>();
        var positionInSection = new Dictionary<int, int>();
        foreach (var question in questions)
        {
            var position = positionInSection[question.SectionId] = positionInSection.GetValueOrDefault(question.SectionId) + 1;
            positions.Add((question.SectionId, position));
        }

        foreach (var (key, bound) in bindings)
        {
            if (positions.Contains(key))
            {
                continue;
            }

            throw new AttemptEngineException(
                AttemptEngineException.PaperNotAssembled,
                $"This paper binds item version {bound.ItemVersionId} at section {key.SectionId} position {key.DisplayOrder}, but that position holds no question. The attempt was not created.");
        }
    }

    /// <summary>
    /// #186: a pinned version's four options have no OptionId column — CNT-06 stores labels A–D and
    /// one key. Both the sitting and the review shape carry an integer option id, so the id is
    /// derived from (version, index) rather than stored: stable for the life of the version, and
    /// impossible to collide with a legacy <c>QuestionOptions</c> id because the version is part of it.
    /// </summary>
    private static int PoolOptionId(int itemVersionId, int index) => itemVersionId * 10 + index + 1;

    /// <summary>The index of a pool option id, or -1 when it names no option of this version.</summary>
    private static int PoolOptionIndex(int optionId, int itemVersionId)
    {
        var index = optionId - itemVersionId * 10 - 1;
        return index is >= 0 and <= 3 ? index : -1;
    }

    private static string OptionLabel(int index) => ((char)('A' + index)).ToString();

    /// <summary>The label of a decoded pool option id, or null when it names no option of the version.</summary>
    private static string? OptionLabelOrNull(int optionId, int itemVersionId)
    {
        var index = PoolOptionIndex(optionId, itemVersionId);
        return index < 0 ? null : OptionLabel(index);
    }

    private static int LabelIndex(string? label) => label?.Trim().ToUpperInvariant() switch
    {
        "A" => 0,
        "B" => 1,
        "C" => 2,
        "D" => 3,
        _ => -1,
    };

    /// <summary>CNT-06: a version is either four options with a key, or a productive task with none.</summary>
    private static bool IsProductive(ItemVersion version) => version.OptionA is null;

    private static IReadOnlyList<string?> OptionTexts(ItemVersion version) =>
        [version.OptionA, version.OptionB, version.OptionC, version.OptionD];

    private static IReadOnlyList<AttemptOption> PoolOptions(int itemVersionId, ItemVersion version) =>
        IsProductive(version)
            ? []
            : OptionTexts(version)
                .Select((text, index) => new AttemptOption(PoolOptionId(itemVersionId, index), OptionLabel(index), text ?? string.Empty))
                .ToList();

    private static IReadOnlyList<AttemptReviewOption> PoolReviewOptions(int itemVersionId, ItemVersion version) =>
        IsProductive(version)
            ? []
            : OptionTexts(version)
                .Select((text, index) => new AttemptReviewOption(
                    PoolOptionId(itemVersionId, index),
                    OptionLabel(index),
                    text ?? string.Empty,
                    string.Equals(OptionLabel(index), version.KeyLabel, StringComparison.OrdinalIgnoreCase),
                    Rationale: null))
                .ToList();

    /// <summary>
    /// The option the learner picked, expressed as the id the projection renders. A pinned answer
    /// stores the label (an item version has no option rows to point at); the legacy path stores the
    /// id. Both come back as one id so nothing downstream learns there are two stores.
    /// </summary>
    private static int? SelectedPoolOptionId(int itemVersionId, AttemptAnswer? answer)
    {
        var index = LabelIndex(answer?.SelectedLabel);
        return index < 0 ? null : PoolOptionId(itemVersionId, index);
    }

    /// <summary>
    /// #186: the versions the given answers pinned, keyed by version id. Empty when no answer carries a
    /// pin (a paper the pool has not assembled), which is the case that keeps scoring exactly as before.
    /// </summary>
    private async Task<Dictionary<int, ItemVersion>> LoadPinnedVersionsAsync(
        IEnumerable<AttemptAnswer> answers,
        CancellationToken cancellationToken)
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

    /// <summary>
    /// #186: a pinned answer is right when the label it recorded is the pinned version's key. The legacy
    /// question's options are not consulted at all — they may disagree with the version the learner sat,
    /// and the version is the one that scores. An answer with no pin scores against the question row,
    /// which is exactly what the engine did before this ticket.
    /// </summary>
    private static bool EvaluateAnswer(
        AttemptAnswer answer,
        IReadOnlyDictionary<int, ItemVersion> pinnedVersions,
        ILookup<int, QuestionOption> optionsByQuestion)
    {
        if (answer.ItemVersionId is int versionId && pinnedVersions.TryGetValue(versionId, out var version))
        {
            return answer.SelectedLabel is not null
                && string.Equals(answer.SelectedLabel, version.KeyLabel, StringComparison.OrdinalIgnoreCase);
        }

        return EvaluateObjectiveAnswer(
            answer.Question,
            optionsByQuestion[answer.QuestionId],
            answer.SelectedOptionId,
            answer.SelectedOptionIdsJson,
            answer.ResponseText);
    }

    /// <summary>
    /// #186: the binding covering one question of a paper, resolved the same way
    /// <see cref="PlaceBindings"/> does — by (section, 1-based position within that section). Used on
    /// the path that creates an answer row lazily for a question attempt creation did not seed one for.
    /// </summary>
    private async Task<BoundPaperPosition?> FindBoundPositionAsync(
        int examId,
        int questionId,
        CancellationToken cancellationToken)
    {
        var bindings = await LoadPaperBindingsAsync(examId, cancellationToken);
        if (bindings.Count == 0)
        {
            return null;
        }

        var questions = await LoadExamQuestionsAsync(examId, cancellationToken);
        return PlaceBindings(questions, bindings).GetValueOrDefault(questionId);
    }

    /// <summary>
    /// #186: an answer's chosen option as the id the projection renders, whether the row pinned a
    /// version (label) or predates the pool (option id).
    /// </summary>
    private static int? ProjectedOptionId(AttemptAnswer? answer) =>
        answer?.ItemVersionId is int versionId
            ? SelectedPoolOptionId(versionId, answer)
            : answer?.SelectedOptionId;

    private static bool IsAnswered(AttemptAnswer? answer) =>
        answer is not null
        && (answer.SelectedOptionId.HasValue
            || !string.IsNullOrWhiteSpace(answer.SelectedLabel)
            || !string.IsNullOrWhiteSpace(answer.ResponseText)
            || !string.IsNullOrWhiteSpace(answer.SelectedOptionIdsJson));
}
