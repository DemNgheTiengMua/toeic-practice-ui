
namespace Contracts;

/// <summary>
/// #350: resolving a complaint is one explicit choice, not an optional credit number. The make-good's
/// ceiling is the credits actually spent on the subject (see <see cref="AdminComplaintDetailDto.CreditsSpent"/>),
/// so compensating more than was lost is impossible; the refund arm records nothing and returns a handoff.
/// </summary>
public sealed record ResolveComplaintRequest(
    string Resolution,
    string InternalNotes,
    ComplaintCompensationDto Compensation = ComplaintCompensationDto.None,
    int GrantCredits = 0);
