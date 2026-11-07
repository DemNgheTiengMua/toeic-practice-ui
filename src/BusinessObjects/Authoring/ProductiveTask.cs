using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>
/// #204: the productive studio's own refusals. They are not gate codes, for the reason
/// <see cref="StimulusStudioRules"/> is not: a gate judges an item against its cell or its bundle,
/// and these judge the <em>shape of a productive task</em> - how many sub-tasks it may carry, which
/// window its type allows, and what the placement cap does to both. Named here so a caller branches
/// on a code and the screen can name the field the author must change.
/// </summary>
public static class ProductiveTaskRules
{
    /// <summary>D0.4: the task's prompt copy is missing.</summary>
    public const string Prompt = "studio.productive.prompt";

    /// <summary>D0.4: a speaking task is one prompt and one recording; a writing task is one or two parts.</summary>
    public const string SubTaskCount = "studio.productive.subtasks";

    /// <summary>D0.4: a writing sub-task carries its own word range, and a range that is inverted demands nothing.</summary>
    public const string WordRange = "studio.productive.words";

    /// <summary>D0.4 and CK_ItemVersions_Timing: a preparation or response window outside its type's bounds.</summary>
    public const string Timing = "studio.productive.timing";

    /// <summary>D0.2 / D0.7: placement's recording cap and its narrower written ranges.</summary>
    public const string PlacementCap = "studio.productive.placement_cap";

    /// <summary>D0.4: a type that needs a visual stimulus was saved without one, or carries one it never shows.</summary>
    public const string VisualStimulus = "studio.productive.image";

    /// <summary>D0.4: the type is not one this skill authors.</summary>
    public const string ItemType = "studio.productive.item_type";

    /// <summary>ADM-07: the studio's context (topic and level) is not complete.</summary>
    public const string Context = "studio.productive.context";
}

/// <summary>#204: one studio rule broken, named by code and by sentence.</summary>
public sealed record ProductiveRefusal(string Rule, string Message);

/// <summary>
/// #204: one part of a productive task, as the rules judge it - its own type, prompt, word range and
/// windows. A one-part task carries one, a two-part writing task carries two, and a speaking task
/// always exactly one.
/// <para>
/// The word range and the windows live here rather than on the task because D0.7 weights Task 2
/// twice Task 1: two parts with one shared range would score the shorter one against the longer
/// one's demand.
/// </para>
/// </summary>
public sealed record ProductiveSubTaskShape(
    string ItemType,
    string Prompt,
    int? MinWords = null,
    int? MaxWords = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    /// <summary>
    /// What the learner is told after answering. Gate one requires it of every item, productive ones
    /// included (CNT-35), so the studio asks for it rather than letting the seam refuse a task the
    /// author thought was complete.
    /// </summary>
    string? Explanation = null);

/// <summary>
/// #204: one of D0.4's eight productive types, with the bounds that type carries.
/// <para>
/// <b>Why a table and not a derivation.</b> D0.4's four rows per skill differ in exactly three ways
/// that matter here - whether the prompt is shown with a visual stimulus, the preparation window and
/// the response window - and none of them is a function of the band. Deriving them from the type's
/// name would be a second reading of the specification that could disagree with the first.
/// </para>
/// <para>
/// <b><see cref="MinimumWords"/> is the type's own floor, not the band's.</b> D0.4 writes
/// "50-150 words by band", which states a type range and lets the band narrow it within that;
/// D0.2's band demand is applied by <see cref="ProductiveTaskEnvelope"/> and only where this type's
/// ceiling can host it. See the note there for why that ordering is the only representable one.
/// </para>
/// </summary>
public sealed record ProductiveTypeBounds(
    string Modality,
    string ItemType,
    /// <summary>D0.4's "optional image" versus "table or chart": the type's visual stimulus.</summary>
    ProductiveVisual Visual,
    /// <summary>The preparation window in seconds, inclusive; null when the type states none.</summary>
    int? MinimumPrepSeconds,
    int? MaximumPrepSeconds,
    /// <summary>The recording window (speaking) or the composition budget (writing), in seconds.</summary>
    int? MinimumResponseSeconds,
    int? MaximumResponseSeconds,
    /// <summary>D0.4's word range for this type, or null for a speaking task (bounded by its window).</summary>
    int? MinimumWords,
    int? MaximumWords);

/// <summary>Whether a productive type needs a visual stimulus, may show one, or never does.</summary>
public enum ProductiveVisual
{
    /// <summary>The prompt is copy only. An image would be a field no screen renders.</summary>
    None,
    /// <summary>The type reads either way - a picture with a description, or a description alone.</summary>
    Optional,
    /// <summary>A chart, table or process diagram is the task. Without it there is nothing to synthesise.</summary>
    Required,
}

/// <summary>
/// #204 / S-07: what a productive task may be, in D0.4's and D0.2's terms. Pure: no database, no
/// clock, no model - the cell a task joins and the rubric it is graded by are the service's business.
/// <para>
/// <b>The input this guard must not pass.</b> A speaking task authored "for placement" with a
/// 120-second recording window is a perfectly legal practice task: D0.2 allows 90-120 s at C1/C2, and
/// every per-item rule accepts it. It is only wrong <em>for placement</em>, where the conditions must
/// be equal across candidates - and a cap discovered at the end of a sitting is a candidate who spoke
/// for twice as long as the next one. That is why the cap is a bound on the draft and not a trim at
/// extraction: the author has to see it before they do the work.
/// </para>
/// </summary>
public static class ProductiveTaskEnvelope
{
    /// <summary>D0.2 and D0.7: a placement speaking task is capped at 60 seconds for every band.</summary>
    public const int PlacementRecordingCapSeconds = 60;

    /// <summary>
    /// D0.7: placement-eligible C1/C2 prompts are scoped below their practice envelope, because two
    /// tasks inside a 22-minute writing budget cannot both be 250-350 words. Both ends move together:
    /// "120-140 words (Task 1) and 180-220 words (Task 2)".
    /// </summary>
    public const int PlacementTaskOneWordFloor = 120;

    public const int PlacementTaskOneWordCeiling = 140;

    public const int PlacementTaskTwoWordFloor = 180;

    public const int PlacementTaskTwoWordCeiling = 220;

    /// <summary>D0.2's writing output demand per band: the floor any declared range must reach.</summary>
    private static readonly IReadOnlyDictionary<int, int> WordFloorByRank = new Dictionary<int, int>
    {
        [CefrLevel.A1.Rank] = 30,
        [CefrLevel.A2.Rank] = 60,
        [CefrLevel.B1.Rank] = 110,
        [CefrLevel.B2.Rank] = 180,
        [CefrLevel.C1.Rank] = 250,
        [CefrLevel.C2.Rank] = 280,
    };

    /// <summary>D0.4's four rows per skill, read straight off the table.</summary>
    private static readonly IReadOnlyList<ProductiveTypeBounds> Table =
    [
        //        skill      type                            visual     prep        record       words
        new("speaking", "descriptive_monologue",   ProductiveVisual.Optional, 30, 45,  45,  60, null, null),
        new("speaking", "situational_negotiation", ProductiveVisual.None,     45, 60,  60,  90, null, null),
        new("speaking", "argumentative_monologue", ProductiveVisual.None,     60, 60,  90, 120, null, null),
        new("speaking", "visual_data_synthesis",   ProductiveVisual.Required, 45, 45,  60,  90, null, null),
        // writing: D0.4's "50-150 / 60-180 / 80-300 / 80-320 words by band"
        new("writing",  "transactional_correspondence", ProductiveVisual.None,     null, null, 480,  900,  50, 150),
        new("writing",  "visual_data_synthesis",   ProductiveVisual.Required,  null, null,  600, 1200,  60, 180),
        new("writing",  "persuasive_essay",        ProductiveVisual.None,      null, null,  840, 1800,  80, 300),
        new("writing",  "analytical_evaluation_essay", ProductiveVisual.None,  null, null,  840, 1800,  80, 320),
    ];

    /// <summary>D0.2's writing floor for one band, or null for the reportable floor.</summary>
    public static int? WordFloor(CefrLevel band) =>
        WordFloorByRank.TryGetValue(band.Rank, out var floor) ? floor : null;

    /// <summary>The productive skills the studio authors.</summary>
    public static bool IsProductiveSkill(string? modality) =>
        string.Equals(modality, "speaking", StringComparison.Ordinal)
        || string.Equals(modality, "writing", StringComparison.Ordinal);

    /// <summary>D0.4's productive types for one skill, in table order.</summary>
    public static IReadOnlyList<string> TypesFor(string modality) =>
        Table.Where(row => string.Equals(row.Modality, modality, StringComparison.Ordinal))
            .Select(row => row.ItemType)
            .ToList();

    /// <summary>The bounds one (skill, type) carries, or null when that pair is not a productive type.</summary>
    public static ProductiveTypeBounds? Bounds(string? modality, string? itemType) =>
        Table.FirstOrDefault(row =>
            string.Equals(row.Modality, modality, StringComparison.Ordinal)
            && string.Equals(row.ItemType, itemType?.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// D0.4's "prompt 60-100 words" and its siblings are ranges the specification gives the
    /// <em>copy</em>; a task whose prompt is one sentence in a placeholder is not authored. The
    /// ranges themselves are not enforced here: they differ per type in ways a single number would
    /// flatten, and a refusal that guesses at them rejects real prompts.
    /// </summary>
    public static IReadOnlyList<ProductiveRefusal> Review(
        string? modality,
        CefrLevel level,
        bool placementEligible,
        string? promptCopy,
        string? imagePath,
        string? imageAlt,
        IReadOnlyList<ProductiveSubTaskShape> subTasks)
    {
        ArgumentNullException.ThrowIfNull(subTasks);

        var refusals = new List<ProductiveRefusal>();

        if (!IsProductiveSkill(modality))
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.ItemType,
                $"'{modality}' is not a skill the productive studio authors. A shared passage or audio serves "
                + "reading and listening; a speaking or writing prompt is one task and belongs here (D0.4)."));
            return refusals;
        }

        if (level.IsReportableFloor)
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.Context,
                $"'{level.Code}' is not an authored level. Content is authored at A1-C2."));
            return refusals;
        }

        if (string.IsNullOrWhiteSpace(promptCopy))
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.Prompt,
                "the task carries no prompt. A productive task is the instruction a learner reads and is "
                + "graded against; with none, there is nothing to answer and nothing to score (D0.4)."));
        }

        var speaking = string.Equals(modality, "speaking", StringComparison.Ordinal);

        // A speaking task is one prompt and one recording. A writing task is one part, or the two a
        // two-task sitting is weighted for (D0.7: Task 2 counts twice Task 1).
        if (speaking && subTasks.Count != 1)
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.SubTaskCount,
                $"a speaking task carries exactly one sub-task; this one carries {subTasks.Count}. One prompt, "
                + "one preparation window and one recording is what a speaking task is (D0.4)."));
        }
        else if (!speaking && (subTasks.Count < 1 || subTasks.Count > 2))
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.SubTaskCount,
                $"a writing task carries one sub-task, or the two a two-task sitting is weighted for; this one "
                + $"carries {subTasks.Count}. A two-part task needs BOTH parts, each with its own word range "
                + "and time budget, or the sitting scores a task that was never authored (D0.7)."));
        }

        for (var index = 0; index < subTasks.Count; index++)
        {
            var position = index + 1;
            var subTask = subTasks[index];
            ReviewSubTask(refusals, modality!, level, placementEligible, subTask, position, subTasks.Count);
        }

        // The visual stimulus, after the types are known: which of them needs one is the type's fact.
        var types = subTasks.Select(subTask => subTask?.ItemType).ToList();
        var needsImage = types.Any(type => Bounds(modality, type)?.Visual == ProductiveVisual.Required);
        var allowsImage = types.Any(type => Bounds(modality, type)?.Visual is ProductiveVisual.Required or ProductiveVisual.Optional);

        if (needsImage && string.IsNullOrWhiteSpace(imagePath))
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.VisualStimulus,
                $"a {types.First(type => Bounds(modality, type)?.Visual == ProductiveVisual.Required)} task "
                + "is a synthesis of the table or chart it shows. Without the visual there is nothing to "
                + "summarise, and the prompt alone asks the learner to invent the data (D0.4)."));
        }

        if (!allowsImage && !string.IsNullOrWhiteSpace(imagePath))
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.VisualStimulus,
                "none of this task's types shows a visual stimulus; an image here is a field no screen "
                + "renders and no rubric scores (D0.4)."));
        }

        if (!string.IsNullOrWhiteSpace(imagePath) && string.IsNullOrWhiteSpace(imageAlt))
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.VisualStimulus,
                "the visual stimulus needs alt text (UI-5): an undescribed chart is inaccessible, and the "
                + "learner who cannot see it has no task at all."));
        }

        return refusals;
    }

    /// <summary>
    /// One sub-task's type, word range and windows. <paramref name="position"/> is 1-based and
    /// <paramref name="count"/> is how many the task carries, because D0.7's placement cap narrows
    /// Task 1 and Task 2 differently.
    /// </summary>
    private static void ReviewSubTask(
        List<ProductiveRefusal> refusals,
        string modality,
        CefrLevel level,
        bool placementEligible,
        ProductiveSubTaskShape? subTask,
        int position,
        int count)
    {
        if (subTask is null || string.IsNullOrWhiteSpace(subTask.Prompt))
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.Prompt,
                $"sub-task {position} carries no prompt. Each part of a two-part task is answered separately, "
                + "so each needs its own instruction (D0.7)."));
            return;
        }

        if (string.IsNullOrWhiteSpace(subTask.Explanation))
        {
            // Gate one owns this rule (CNT-35) and would refuse the task at the seam; stating it here
            // means the author is told on the field rather than after a save, which is the same reason
            // the placement cap is on the analysis.
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.Prompt,
                $"sub-task {position} carries no explanation. Every item - a productive task included - says "
                + "what the answer should have contained, because a learner told only that they were wrong "
                + "learns nothing (CNT-35)."));
        }

        var bounds = Bounds(modality, subTask.ItemType);
        if (bounds is null)
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.ItemType,
                $"'{subTask.ItemType}' is not a {modality} task type. A {modality} task is one of: "
                + $"{string.Join(", ", TypesFor(modality))} (D0.4, CK_Items_Modality_Type)."));
            return;
        }

        ReviewWords(refusals, bounds, level, placementEligible, subTask, position, count);
        ReviewWindows(refusals, bounds, placementEligible, subTask, position);
    }

    private static void ReviewWords(
        List<ProductiveRefusal> refusals,
        ProductiveTypeBounds bounds,
        CefrLevel level,
        bool placementEligible,
        ProductiveSubTaskShape subTask,
        int position,
        int count)
    {
        if (bounds.MaximumWords is not int typeCeiling)
        {
            // Speaking: a spoken answer's length is its recording window, and a word count over a
            // transcript of speech is not an authored demand. A declared range here is a field the
            // sitting would show and the rubric would ignore.
            if (subTask.MinWords is not null || subTask.MaxWords is not null)
            {
                refusals.Add(new ProductiveRefusal(
                    ProductiveTaskRules.WordRange,
                    $"sub-task {position} declares a word range, but a {bounds.ItemType} answer is bounded by "
                    + "its recording window. A word count over a transcript of speech is a measurement of the "
                    + "learner's speaking rate, not an authored demand (D0.2)."));
            }

            return;
        }

        if (subTask.MinWords is not int minimum || subTask.MaxWords is not int maximum)
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.WordRange,
                $"sub-task {position} declares no word range. A {bounds.ItemType} answer is counted against "
                + $"one, and the sitting's word meter and the under-length penalty both read it (D0.4)."));
            return;
        }

        if (minimum <= 0 || maximum < minimum)
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.WordRange,
                $"sub-task {position} declares {minimum}-{maximum} words. A ceiling below its floor, or a floor "
                + "of zero, is a target no answer can meet or one every answer meets; neither is a demand (D0.4)."));
            return;
        }

        var (floor, ceiling, placementScoped) = Range(bounds, level, placementEligible, position, count);

        if (minimum < floor)
        {
            refusals.Add(new ProductiveRefusal(
                placementScoped ? ProductiveTaskRules.PlacementCap : ProductiveTaskRules.WordRange,
                placementScoped
                    ? $"sub-task {position} asks for at least {minimum} words; a placement task at {level.Code} "
                      + $"is scoped to {floor}-{ceiling} (D0.7). Placement-eligible C1/C2 prompts sit below the "
                      + "practice envelope so two of them fit the placement writing budget, and the scope is "
                      + "shown here so it is seen before the work."
                    : $"sub-task {position} asks for at least {minimum} words, below {level.Code}'s {floor}. The "
                      + "band's writing output demand is what the level claim means: a task shorter than it "
                      + $"cannot measure {level.Code} writing, whatever it asks (D0.2)."));
        }

        if (maximum > ceiling)
        {
            refusals.Add(new ProductiveRefusal(
                placementScoped ? ProductiveTaskRules.PlacementCap : ProductiveTaskRules.WordRange,
                placementScoped
                    ? $"sub-task {position} allows up to {maximum} words; a placement task at {level.Code} is "
                      + $"scoped to {floor}-{ceiling} (D0.7). Two tasks inside the placement writing budget "
                      + "cannot both run to the practice envelope, and the scope is shown before the work."
                    : $"sub-task {position} allows up to {maximum} words; {bounds.ItemType} is bounded at "
                      + $"{ceiling} for this task (D0.4)."));
        }
    }

    /// <summary>
    /// The word range one part may declare, after D0.4's per-type range, D0.2's per-band demand and
    /// D0.7's placement scoping are reconciled.
    /// <para>
    /// <b>Where the specification corrects itself.</b> D0.4 writes each writing type as a range "by
    /// band" - 50-150, 60-180, 80-300, 80-320 - and D0.2 states a band's writing demand as 60-90 at
    /// A2 rising to 250-320 at C1. At B2 and above those two contradict: a transactional
    /// correspondence capped at 150 words cannot also be 180-250 words of B2 writing. D0.2 is the
    /// authority on what a band means, and D0.4's "by band" qualifier says so in its own words, so
    /// the band floor wins and the type's ceiling lifts to it. The result is a tight range
    /// (180-180 at B2) rather than an authorable contradiction, and it is named here so nobody
    /// re-derives it a different way.
    /// </para>
    /// <para>
    /// Placement narrows both ends, never one: a cap that moved only the ceiling would leave C1's
    /// 250-word floor under a 140-word ceiling.
    /// </para>
    /// </summary>
    public static (int Floor, int Ceiling, bool PlacementScoped) Range(
        ProductiveTypeBounds bounds,
        CefrLevel level,
        bool placementEligible,
        int position,
        int count)
    {
        var bandFloor = WordFloor(level) ?? 0;
        var typeFloor = bounds.MinimumWords ?? 0;
        var typeCeiling = bounds.MaximumWords ?? int.MaxValue;

        var floor = Math.Max(typeFloor, bandFloor);
        var ceiling = Math.Max(typeCeiling, floor);

        if (placementEligible && level >= CefrLevel.C1)
        {
            var second = count > 1 && position == 2;
            var placementFloor = second ? PlacementTaskTwoWordFloor : PlacementTaskOneWordFloor;
            var placementCeiling = second ? PlacementTaskTwoWordCeiling : PlacementTaskOneWordCeiling;

            floor = Math.Min(floor, placementFloor);
            ceiling = Math.Min(ceiling, placementCeiling);
            return (floor, ceiling, true);
        }

        return (floor, ceiling, false);
    }

    /// <summary>
    /// The two windows one part may declare.
    /// <para>
    /// <b>Where placement corrects D0.4, the same way it corrects D0.2.</b> D0.2 states that placement
    /// speaking is capped at 60 seconds "for every band" and D0.7 repeats it as a cap, while D0.4's
    /// argumentative monologue records for 90-120 s and its negotiation for 60-90. A placement
    /// argumentative task therefore cannot satisfy both as written: the cap is the placement rule, so
    /// it replaces the type's floor rather than being added above it - otherwise the only placement
    /// task authorable from that row is one the spec forbids. Practice keeps D0.4's floor untouched.
    /// </para>
    /// </summary>
    private static void ReviewWindows(
        List<ProductiveRefusal> refusals,
        ProductiveTypeBounds bounds,
        bool placementEligible,
        ProductiveSubTaskShape subTask,
        int position)
    {
        if (bounds.MinimumPrepSeconds is int prepFloor)
        {
            if (subTask.PrepSeconds is not int prep)
            {
                refusals.Add(new ProductiveRefusal(
                    ProductiveTaskRules.Timing,
                    $"sub-task {position} declares no preparation window; a {bounds.ItemType} task runs "
                    + $"{prepFloor}-{bounds.MaximumPrepSeconds} s of preparation before the answer (D0.4)."));
            }
            else if (prep <= 0 || prep < prepFloor || prep > bounds.MaximumPrepSeconds)
            {
                refusals.Add(new ProductiveRefusal(
                    ProductiveTaskRules.Timing,
                    $"sub-task {position} prepares for {prep} s; a {bounds.ItemType} task allows "
                    + $"{prepFloor}-{bounds.MaximumPrepSeconds} s (D0.4). Zero is not 'no preparation' - that "
                    + "is an absent window - and the countdown renders it as already finished."));
            }
        }

        // A writing task's preparation is planning time the spec does not bound. A declared one must
        // still be a real window, for the same reason every other declared window must be.
        if (bounds.MinimumPrepSeconds is null && subTask.PrepSeconds is int planning && planning <= 0)
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.Timing,
                $"sub-task {position} declares a {planning} s planning window. Zero is not 'no planning' - "
                + "that is an absent window, and the countdown renders zero as already finished."));
        }

        // Speaking is the only skill with a placement recording cap: D0.2's "60 s in placement" is
        // about a recording, and a written response is bounded by its word range and its budget.
        var speaking = bounds.MaximumWords is null;
        var placementCapped = speaking && placementEligible;

        var responseFloor = placementCapped
            ? Math.Min(bounds.MinimumResponseSeconds!.Value, PlacementRecordingCapSeconds)
            : bounds.MinimumResponseSeconds!.Value;

        var responseCeiling = placementCapped
            ? PlacementRecordingCapSeconds
            : bounds.MaximumResponseSeconds!.Value;

        if (subTask.ResponseSeconds is not int response)
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.Timing,
                $"sub-task {position} declares no response window; a "
                + $"{(placementCapped ? "placement " : string.Empty)}{bounds.ItemType} task allows "
                + $"{responseFloor}-{responseCeiling} s (D0.4{(placementCapped ? ", D0.2" : string.Empty)})."));
            return;
        }

        if (response <= 0 || response < responseFloor)
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.Timing,
                $"sub-task {position} runs for {response} s; a "
                + $"{(placementCapped ? "placement " : string.Empty)}{bounds.ItemType} task allows at least "
                + $"{responseFloor} s (D0.4)."));
            return;
        }

        if (placementCapped && response > PlacementRecordingCapSeconds)
        {
            // D0.2/D0.7: placement is bounded so conditions are equal across candidates.
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.PlacementCap,
                $"sub-task {position} records for {response} s; a placement speaking task is capped at "
                + $"{PlacementRecordingCapSeconds} s for every band (D0.2, D0.7). Practice may run as long as "
                + "the learner needs; placement may not, and the cap is shown before the work."));
            return;
        }

        if (response > responseCeiling)
        {
            refusals.Add(new ProductiveRefusal(
                ProductiveTaskRules.Timing,
                $"sub-task {position} runs for {response} s; a {bounds.ItemType} task allows at most "
                + $"{responseCeiling} s (D0.4)."));
        }
    }
}
