using Contracts;

namespace Services;

/// <summary>
/// What happened when an admin removed a certificate, in the terms the admin needs to report it.
/// <para>
/// <b>`Deactivated` is not a failure.</b> A certificate that has been studied cannot be destroyed — its
/// exams, attempts, placements and ledger rows are a learner's history — so it is withdrawn from the
/// catalogue instead. The result says which of the two happened and what moved as a consequence, because
/// an admin who is told only "removed" will look for the row and not find it gone.
/// </para>
/// </summary>
public sealed record CertificateRemovalResultDto(
    bool Deleted,
    int CertId,
    string CertCode,
    /// <summary>Learners whose active certificate was moved off the removed one.</summary>
    int LearnersReassigned,
    /// <summary>The certificate those learners were moved to; null when none were.</summary>
    string? ReassignedToCode,
    string Message);
