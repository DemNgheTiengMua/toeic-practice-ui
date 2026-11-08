using System;

namespace Contracts;

/// <summary>
/// #226 / ADM-12: the six publish invariants, named once.
/// <para>
/// The codes are the vocabulary the rail, the publish refusal and the schema guard's fixture exemption
/// all agree on. They are not gate codes: a gate judges one item or one bundle, and these judge a whole
/// paper.
/// </para>
/// </summary>
public static class ExamInvariants
{
    /// <summary>INV-1: every enabled skill has a section, and every section holds exactly the items it declares.</summary>
    public const string SkillCoverage = "inv1.skill_coverage";

    /// <summary>INV-2: every bound stimulus carries its complete child group (envelope, spans, audio gap).</summary>
    public const string StimulusGroup = "inv2.stimulus_group";

    /// <summary>INV-3: every bound item is active, has verifier agreement, and has cleared its stratum.</summary>
    public const string ItemClearance = "inv3.item_clearance";

    /// <summary>INV-4: every enabled productive skill has a locked rubric, and every bound task carries three anchors.</summary>
    public const string RubricReady = "inv4.rubric_ready";

    /// <summary>INV-5: section timers sum to the declared duration, and the cut-score projection is monotonic.</summary>
    public const string TimersAndProjection = "inv5.timers_projection";

    /// <summary>INV-6: a production exam binds no demo item. Enforced by trigger; checked here as a rail row.</summary>
    public const string TierIsolation = "inv6.tier_isolation";

    public static readonly string[] All =
    [
        SkillCoverage, StimulusGroup, ItemClearance, RubricReady, TimersAndProjection, TierIsolation,
    ];
}
