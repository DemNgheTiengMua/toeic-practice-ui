namespace BusinessObjects;

/// <summary>
/// #240 / D0.5: a lesson's two sub-modules. <c>module_primary</c> is the rule, the level-calibrated
/// strategy and the worked examples; <c>module_remedial</c> is distractor anatomy, or the band
/// k-1 -> k upgrade comparison for a productive skill.
/// <para>
/// CK_LessonCards_Module and CK_LLP_Module accept exactly these two codes, and the reader picks which
/// one to show from the visit, not from a query string.
/// </para>
/// </summary>
public static class LessonModule
{
    public const string Primary = "primary";

    public const string Remedial = "remedial";

    /// <summary>The remedial module a second attempt at a cell reads. Anything unrecognised reads the
    /// primary module rather than an empty one.</summary>
    public static string For(bool remedialVisit) => remedialVisit ? Remedial : Primary;
}
