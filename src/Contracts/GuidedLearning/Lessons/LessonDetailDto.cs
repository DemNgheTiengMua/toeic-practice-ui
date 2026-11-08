namespace Contracts;

/// <summary>
/// #240: the two module codes, mirrored from CK_LessonCards_Module so a DTO can name one without
/// dragging the domain assembly across the wire boundary.
/// </summary>
public static class LessonModuleCodes
{
    public const string Primary = "primary";

    public const string Remedial = "remedial";
}

/// <summary>
/// #240: one module of a leveled lesson. A lesson carries a primary module and a remedial one, and
/// they are separate card sets - a second attempt at the same cell must not re-read identical
/// instruction.
/// </summary>
public sealed record LessonDetailDto(
    int LessonId,
    string Title,
    string? Summary,
    int SkillId,
    int? CategoryId,
    int EstimatedMinutes,
    List<LessonCardDto> Cards,
    int TopicId = 0,
    string TopicCode = "",
    string TopicName = "",
    string CefrLevel = "",
    string Module = LessonModuleCodes.Primary,
    bool HasRemedialModule = false)
{
    /// <summary>D0.5: what a learner sees the module called.</summary>
    public bool IsRemedial => Module == LessonModuleCodes.Remedial;
}
