
namespace Contracts;

/// <summary>Everything the learner shell renders on every page: the active certificate, the ones
/// the switcher offers and the credit balance. Dates render through DateTimeFormatter, which
/// loads the time zone itself (issue #6).
/// <para>
/// #242: the shell needs two more facts, and only these two. <see cref="ExaminedSkills"/> is what
/// tells a two-skill certificate's six doors which two it cannot open (LRN-10), and
/// <see cref="ExamDate"/> is the indicator LRN-15 puts in the shell. Both are optional on the wire
/// so every existing caller keeps compiling; a null list means "not reported" and leaves every door
/// open, because a projection that failed must not lock a learner out of a screen they own.
/// </para></summary>
public sealed record LearnerContextDto(
    CertificateOptionDto Active,
    IReadOnlyList<CertificateOptionDto> Certificates,
    int CreditBalance,
    IReadOnlyList<ModalityDto>? ExaminedSkills = null,
    DateOnly? ExamDate = null)
{
    /// <summary>Whether the active certificate examines <paramref name="skill"/>. Unknown counts as
    /// examined: the door opens, and the reason a learner cannot find a screen is never "we could not
    /// read the certificate".</summary>
    public bool Examines(ModalityDto skill) => ExaminedSkills is null || ExaminedSkills.Contains(skill);
}
