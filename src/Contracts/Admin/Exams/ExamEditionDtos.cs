namespace Contracts;

/// <summary>
/// #227 / D20: fork a paper that has finished attempts into a new edition, so a structural repair
/// never rewrites what a learner sat.
/// <para>
/// There is no <c>CopyItems</c> flag: an edition that did not copy the paper would not be an edition.
/// <see cref="Name"/> is required because the paper's name is unique per certificate
/// (<c>UX_Exams_Cert_Name</c>), so a fork cannot inherit it.
/// </para>
/// </summary>
public sealed record ForkExamEditionRequest(string? Name, string? InternalNote = null);

/// <summary>
/// What the fork produced. <see cref="QuestionCount"/> and <see cref="BindingCount"/> are the two
/// numbers that say the copy was complete rather than merely created - a fork with a paper row and no
/// questions would satisfy "a new edition exists" and be unsittable.
/// </summary>
public sealed record AdminForkEditionResultDto(
    int ExamId,
    string Name,
    int EditionNumber,
    int SupersedesExamId,
    int SupersedesEditionNumber,
    int QuestionCount,
    int BindingCount);
