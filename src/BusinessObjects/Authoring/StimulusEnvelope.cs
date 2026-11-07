using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>
/// #203: the studio's own refusals. They are not gate codes: a gate judges one item or one bundle's
/// spans, and these judge the <em>shape of authoring</em> - how many children a stimulus may carry and
/// which levels they may sit at. They live in <see cref="StimulusStudioRules"/> so a caller branches on
/// a code and the screen can name the thing the author must change.
/// </summary>
public static class StimulusStudioRules
{
    /// <summary>D0.4 and CNT-20/CNT-21: the bundle's child count is outside its type's bounds.</summary>
    public const string ChildCount = "studio.children.count";

    /// <summary>D0.3: a child's level is outside the stimulus's envelope.</summary>
    public const string ChildLevel = "studio.envelope.level";

    /// <summary>D0.3: fewer than two-thirds of the children sit at the stimulus's own level.</summary>
    public const string AtLevelShare = "studio.envelope.share";

    /// <summary>D0.4: the item type is not one this skill authors.</summary>
    public const string ItemType = "studio.item_type";

    /// <summary>CNT-11: a child's evidence span is measured in the wrong unit for its skill.</summary>
    public const string SpanUnit = "studio.span.unit";

    /// <summary>A stimulus with no passage, transcript or audio is not a stimulus.</summary>
    public const string StimulusText = "studio.stimulus.text";
}

/// <summary>#203: one studio rule broken, named by code and by sentence.</summary>
public sealed record StimulusRefusal(string Rule, string Message);

/// <summary>
/// #203 / ADM-07: what a stimulus of one skill, level and type may carry.
/// <para>
/// <b>The child count is fixed by (skill, level), not by level alone.</b> CNT-21's volume table packs
/// listening audio and reading passages differently at the same band - A2 is two items per audio and
/// three per passage, B2 is three per audio and four per passage - so a control that locked the count
/// from the level would either overfill every A2 audio or starve every B2 passage. The locked figure is
/// the table's modal count for that pair.
/// </para>
/// <para>
/// <b>Why <see cref="Minimum"/> is below <see cref="Locked"/>.</b> The same table states exceptions:
/// at A1 one passage in thirty-seven carries three items, and at B2, C1 and C2 a few carry three
/// instead of four. A save rule that demanded exactly the modal count would make those rows
/// un-authorable, so the count is locked for the slots the screen pre-creates and bounded by D0.4's
/// 2-4 for the save.
/// </para>
/// </summary>
public sealed record StimulusChildQuota(
    int Locked,
    int Minimum,
    int Maximum,
    IReadOnlyList<CefrLevel> AllowedChildLevels,
    int MinimumAtStimulusLevel,
    string SpanUnit);

/// <summary>
/// #203: the ±1 stimulus envelope (D0.3) and the child count (D0.4, CNT-20, CNT-21). Pure: no
/// database, no clock, no model.
/// <para>
/// <b>The input this guard must not pass.</b> A C1 inference item on a 130-word A2 notice is written,
/// and every per-item gate accepts it: gate one sees four good options, gate two profiles the notice
/// against A2 and finds it in band, gate four classifies the <em>item</em> at C1 and agrees. Nothing
/// compares the two levels to each other, which is why this rule exists beside the gates rather than
/// inside one.
/// </para>
/// </summary>
public static class StimulusEnvelope
{
    /// <summary>D0.3: at or above this band a child may sit one level away from its stimulus.</summary>
    public static readonly CefrLevel FirstEnvelopeBand = CefrLevel.B1;

    /// <summary>D0.3: at least two-thirds of a stimulus's children sit at its own level.</summary>
    public const int AtLevelNumerator = 2;

    public const int AtLevelDenominator = 3;

    /// <summary>D0.4's bound for every shared-stimulus objective type.</summary>
    public const int MinimumSharedChildren = 2;

    public const int MaximumSharedChildren = 4;

    /// <summary>
    /// CNT-21's modal children per stimulus, read off the volume table. Listening packs 2/2/3/3/4/3
    /// and reading 2/3/3/4/4/4 across A1-C2.
    /// </summary>
    private static readonly IReadOnlyDictionary<(string Modality, int Rank), int> ModalChildren =
        new Dictionary<(string, int), int>
        {
            [("listening", CefrLevel.A1.Rank)] = 2,
            [("listening", CefrLevel.A2.Rank)] = 2,
            [("listening", CefrLevel.B1.Rank)] = 3,
            [("listening", CefrLevel.B2.Rank)] = 3,
            [("listening", CefrLevel.C1.Rank)] = 4,
            [("listening", CefrLevel.C2.Rank)] = 3,
            [("reading", CefrLevel.A1.Rank)] = 2,
            [("reading", CefrLevel.A2.Rank)] = 3,
            [("reading", CefrLevel.B1.Rank)] = 3,
            [("reading", CefrLevel.B2.Rank)] = 4,
            [("reading", CefrLevel.C1.Rank)] = 4,
            [("reading", CefrLevel.C2.Rank)] = 4,
        };

    /// <summary>
    /// D0.4's standalone types carry one item and no sibling: a cloze is one blank in one sentence,
    /// and a cohesion item's four options are the four insertion positions, not four children.
    /// </summary>
    private static readonly HashSet<string> StandaloneTypes = new(StringComparer.Ordinal)
    {
        "sentence_cloze",
        "text_insertion_cohesion",
    };

    /// <summary>The objective skills the studio authors. Productive tasks are S-07's screen.</summary>
    public static bool IsStudioSkill(string? modality) =>
        string.Equals(modality, "listening", StringComparison.Ordinal)
        || string.Equals(modality, "reading", StringComparison.Ordinal);

    public static string SpanUnitFor(string? modality) =>
        string.Equals(modality, "listening", StringComparison.Ordinal) ? "sec" : "char";

    /// <summary>
    /// The number of children a bundle locks, and the levels they may sit at. Throws rather than
    /// inventing a quota for a skill or type the studio does not author, because a made-up count would
    /// pre-create the wrong number of slots and the author would author into it.
    /// </summary>
    public static StimulusChildQuota Quote(string? modality, CefrLevel stimulusLevel, string? itemType)
    {
        if (!IsStudioSkill(modality))
        {
            throw new ArgumentException($"'{modality}' is not a skill the stimulus studio authors.", nameof(modality));
        }

        if (stimulusLevel.IsReportableFloor)
        {
            throw new ArgumentException($"'{stimulusLevel.Code}' is the reportable floor; content is authored at A1-C2 only.", nameof(stimulusLevel));
        }

        var type = itemType?.Trim() ?? string.Empty;
        if (type.Length == 0)
        {
            throw new ArgumentException("An item type is required: D0.4's types carry different child bounds.", nameof(itemType));
        }

        var allowed = AllowedLevels(stimulusLevel);

        if (StandaloneTypes.Contains(type))
        {
            return new StimulusChildQuota(1, 1, 1, allowed, 1, SpanUnitFor(modality));
        }

        var locked = ModalChildren.TryGetValue((modality!, stimulusLevel.Rank), out var modal)
            ? modal
            : MinimumSharedChildren;

        return new StimulusChildQuota(
            locked,
            MinimumSharedChildren,
            MaximumSharedChildren,
            allowed,
            MinimumAtLevel(locked),
            SpanUnitFor(modality));
    }

    /// <summary>D0.3: no envelope at the peripheral bands; ±1 from B1 upward.</summary>
    public static IReadOnlyList<CefrLevel> AllowedLevels(CefrLevel stimulusLevel)
    {
        if (stimulusLevel.IsReportableFloor)
        {
            return [];
        }

        if (stimulusLevel < FirstEnvelopeBand)
        {
            return [stimulusLevel];
        }

        return CefrLevel.AuthoredLevels
            .Where(level => level.Rank >= stimulusLevel.Rank - 1 && level.Rank <= stimulusLevel.Rank + 1)
            .ToList();
    }

    /// <summary>D0.3's "at least two-thirds", rounded up: two children need both, four need three.</summary>
    public static int MinimumAtLevel(int childCount) =>
        (childCount * AtLevelNumerator + AtLevelDenominator - 1) / AtLevelDenominator;

    /// <summary>
    /// Judges a bundle's shape before anything is written. Every refusal names the rule and the number,
    /// because an author told only "the bundle is invalid" changes one field and tries again.
    /// </summary>
    public static IReadOnlyList<StimulusRefusal> Review(
        string? modality,
        CefrLevel stimulusLevel,
        string? itemType,
        IReadOnlyList<CefrLevel> childLevels,
        string? spanUnit)
    {
        ArgumentNullException.ThrowIfNull(childLevels);

        var refusals = new List<StimulusRefusal>();

        if (!IsStudioSkill(modality))
        {
            refusals.Add(new StimulusRefusal(
                StimulusStudioRules.ItemType,
                $"'{modality}' is not a skill the stimulus studio authors. A shared passage or audio serves reading and listening; a speaking or writing prompt is one task and belongs to the productive-task studio (D0.4)."));
            return refusals;
        }

        if (stimulusLevel.IsReportableFloor)
        {
            refusals.Add(new StimulusRefusal(
                StimulusStudioRules.ChildLevel,
                $"'{stimulusLevel.Code}' is not an authored level. Content is authored at A1-C2."));
            return refusals;
        }

        var quota = Quote(modality, stimulusLevel, itemType);

        var expectedUnit = SpanUnitFor(modality);
        if (!string.IsNullOrWhiteSpace(spanUnit) && !string.Equals(spanUnit.Trim(), expectedUnit, StringComparison.Ordinal))
        {
            refusals.Add(new StimulusRefusal(
                StimulusStudioRules.SpanUnit,
                $"a {modality} child's evidence span is measured in '{expectedUnit}' (CNT-11), not '{spanUnit}'. "
                + "Character offsets read as seconds, or the reverse, make the gap and overlap rules meaningless."));
        }

        if (childLevels.Count < quota.Minimum || childLevels.Count > quota.Maximum)
        {
            refusals.Add(new StimulusRefusal(
                StimulusStudioRules.ChildCount,
                $"the bundle carries {childLevels.Count} child item(s); "
                + (quota.Minimum == quota.Maximum
                    ? $"a {itemType} stimulus carries exactly {quota.Minimum} (D0.4)."
                    : $"a shared {itemType} stimulus carries {quota.Minimum} to {quota.Maximum} (D0.4).")));
        }

        var allowed = quota.AllowedChildLevels;
        var outside = childLevels
            .Select((level, index) => (Level: level, Position: index + 1))
            .Where(child => !allowed.Contains(child.Level))
            .ToList();

        foreach (var child in outside)
        {
            refusals.Add(new StimulusRefusal(
                StimulusStudioRules.ChildLevel,
                stimulusLevel < FirstEnvelopeBand
                    ? $"child {child.Position} is {child.Level.Code} and the stimulus is {stimulusLevel.Code}. "
                      + "At A1 and A2 every child sits at the stimulus's own level: a basic stimulus cannot host a "
                      + "harder question without turning it into a trick (D0.3)."
                    : $"child {child.Position} is {child.Level.Code} and the stimulus is {stimulusLevel.Code}. "
                      + $"A child may sit one level either side, so it must be one of "
                      + $"{string.Join(", ", allowed.Select(level => level.Code))} (D0.3)."));
        }

        if (childLevels.Count > 0)
        {
            var atLevel = childLevels.Count(level => level == stimulusLevel);
            var required = MinimumAtLevel(childLevels.Count);
            if (atLevel < required)
            {
                refusals.Add(new StimulusRefusal(
                    StimulusStudioRules.AtLevelShare,
                    $"{atLevel} of {childLevels.Count} child item(s) sit at {stimulusLevel.Code}; at least "
                    + $"{required} must. Two-thirds of a bundle at the stimulus's own level is what stops a "
                    + "passage being used to smuggle items of another difficulty (D0.3)."));
            }
        }

        return refusals;
    }
}
