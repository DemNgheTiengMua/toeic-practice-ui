using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{

    /// <summary>
    /// #186: a pinned version's four options have no OptionId column — CNT-06 stores labels A–D and
    /// one key. Both the sitting and the review shape carry an integer option id, so the id is
    /// derived from (version, index) rather than stored: stable for the life of the version, and
    /// impossible to collide with a legacy <c>QuestionOptions</c> id because the version is part of it.
    /// </summary>
    internal static int PoolOptionId(int itemVersionId, int index) => itemVersionId * 10 + index + 1;

    /// <summary>The index of a pool option id, or -1 when it names no option of this version.</summary>
    internal static int PoolOptionIndex(int optionId, int itemVersionId)
    {
        var index = optionId - itemVersionId * 10 - 1;
        return index is >= 0 and <= 3 ? index : -1;
    }

    internal static string OptionLabel(int index) => ((char)('A' + index)).ToString();

    /// <summary>The label of a decoded pool option id, or null when it names no option of the version.</summary>
    private static string? OptionLabelOrNull(int optionId, int itemVersionId)
    {
        var index = PoolOptionIndex(optionId, itemVersionId);
        return index < 0 ? null : OptionLabel(index);
    }

    /// <summary>
    /// #250: the per-option rationale a pool version stores as four columns, selected by label. Review
    /// renders it; the sitting does not. Returns null for a productive version, which has no options.
    /// </summary>
    private static string? OptionRationale(ItemVersion version, string? label) =>
        label?.Trim().ToUpperInvariant() switch
        {
            "A" => version.RationaleA,
            "B" => version.RationaleB,
            "C" => version.RationaleC,
            "D" => version.RationaleD,
            _ => null,
        };

    internal static int LabelIndex(string? label) => label?.Trim().ToUpperInvariant() switch
    {
        "A" => 0,
        "B" => 1,
        "C" => 2,
        "D" => 3,
        _ => -1,
    };

    /// <summary>
    /// #250: the labels <paramref name="keyLabel"/> marks correct, as a set. A key is a comma-separated
    /// ascending label list: 'A' for the four-option shape, 'A,C' for multi_select's two answers.
    /// <para>
    /// <b>Why a set and not a comparison.</b> Every reader that asked "is this the key" was really
    /// asking "does the key contain this label". With one label the two questions coincide, which is
    /// why the single-label form survived; the moment a key can hold two, `==` answers only for the
    /// first one and a multi_select would be marked wrong for the answer it just recorded. Returning a
    /// set makes the real question the one every caller has to ask.
    /// </para>
    /// <para>
    /// A malformed key yields an empty set rather than throwing: the constraint on <c>ItemVersions</c>
    /// is what refuses a bad key at write time, and a reader that threw on historical data would take
    /// the sitting down over a row it cannot fix.
    /// </para>
    /// </summary>
    internal static IReadOnlySet<string> KeyLabels(string? keyLabel)
    {
        if (string.IsNullOrWhiteSpace(keyLabel))
        {
            return EmptyLabels;
        }

        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in keyLabel.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (LabelIndex(part) >= 0)
            {
                labels.Add(part.ToUpperInvariant());
            }
        }

        return labels;
    }

    /// <summary>Whether <paramref name="label"/> is one of the labels the version marks correct.</summary>
    internal static bool IsKeyLabel(ItemVersion version, string? label) =>
        label is not null && KeyLabels(version.KeyLabel).Contains(label.Trim());

    /// <summary>
    /// #269: whether a TEXT answer matches a pinned version. The version stores no option rows, so a text
    /// completion is judged against its <c>AcceptedAnswersJson</c> (and, for a readable failure, against
    /// the keyed option texts). Whitespace is normalised and the comparison is case-insensitive - the same
    /// rule <c>EvaluateTextCompletion</c> applies to a legacy question, so the two shapes cannot disagree
    /// about what a right answer is.
    /// </summary>
    public static bool EvaluatePinnedText(ItemVersion version, string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        var candidate = System.Text.RegularExpressions.Regex
            .Replace(responseText.Trim(), @"\s+", " ");

        if (!version.AllowNumber && System.Text.RegularExpressions.Regex.IsMatch(candidate, @"\d"))
        {
            return false;
        }

        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (version.MaxWords is int max && words.Length > max)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(version.RequiredKeywords))
        {
            foreach (var keyword in ParseKeywords(version.RequiredKeywords))
            {
                if (!string.IsNullOrWhiteSpace(keyword) && !ContainsKeywordStem(candidate, keyword))
                {
                    return false;
                }
            }
        }

        var accepted = ParseAcceptedAnswers(version.AcceptedAnswersJson);
        foreach (var label in KeyLabels(version.KeyLabel))
        {
            var index = LabelIndex(label);
            var text = index < 0 ? null : OptionTexts(version)[index];
            if (!string.IsNullOrWhiteSpace(text))
            {
                accepted.Add(text.Trim());
            }
        }

        foreach (var option in accepted)
        {
            var normalized = System.Text.RegularExpressions.Regex
                .Replace(option.Trim(), @"\s+", " ");
            // Trailing sentence punctuation is not part of an answer. A learner typing "Photosynthesis."
            // into a completion has answered the question; before this, the full stop made it wrong. The
            // legacy path got away with exact equality because its accepted answers were authored to match
            // whatever the paper's own key said; a pool version's accepted list is authored separately.
            if (string.Equals(
                    candidate.TrimEnd('.', '!', '?', ',', ';', ':'),
                    normalized.TrimEnd('.', '!', '?', ',', ';', ':'),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly IReadOnlySet<string> EmptyLabels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>CNT-06: a version is either four options with a key, or a productive task with none.</summary>
    internal static bool IsProductive(ItemVersion version) => version.OptionA is null;

    internal static IReadOnlyList<string?> OptionTexts(ItemVersion version) =>
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
                    IsKeyLabel(version, OptionLabel(index)),
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
            .Select(a => a.ItemVersionId)
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
    /// #186 / #269: a pinned answer is right when the label it recorded is the pinned version's key, or
    /// when its text matches the version's accepted answers.
    /// </summary>
    private static bool EvaluateAnswer(
        AttemptAnswer answer,
        IReadOnlyDictionary<int, ItemVersion> pinnedVersions)
    {
        // #269: every answer pins a version, so the only way this fails is a version that is gone;
        // such an answer cannot make a correct claim.
        if (!pinnedVersions.TryGetValue(answer.ItemVersionId, out var version))
        {
            return false;
        }

        // #269: a pinned answer is graded by its KEYED LABEL or by its TEXT. Reading only the label
        // meant an inline_completion (a text answer with no label) was re-graded to false on submit,
        // overwriting the correct grade the save had written - so the sitting said right and the
        // review said wrong. The two now agree because both ask this one method.
        return string.IsNullOrWhiteSpace(answer.SelectedLabel) && !string.IsNullOrWhiteSpace(answer.ResponseText)
            ? EvaluatePinnedText(version, answer.ResponseText)
            : IsKeyLabel(version, answer.SelectedLabel);
    }

    /// <summary>
    /// #186: an answer's chosen option as the id the projection renders, whether the row pinned a
    /// version (label) or predates the pool (option id).
    /// </summary>
    private static int? ProjectedOptionId(AttemptAnswer? answer) =>
        answer?.ItemVersionId is int versionId
            ? SelectedPoolOptionId(versionId, answer)
            : answer?.SelectedOptionId;

    /// <summary>
    /// #253: the row-shaped adapter over <see cref="AnswerContent.IsAnswered"/>, the solution's one
    /// definition. The definition lives in Contracts because the review, the sitting and the practice
    /// drill all read the same four columns and only disagree when one of them re-invents the test.
    /// </summary>
    private static bool IsAnswered(AttemptAnswer? answer) =>
        answer is not null
        && AnswerContent.IsAnswered(
            answer.SelectedOptionId, answer.SelectedLabel, answer.ResponseText, answer.SelectedOptionIdsJson);
}
