
namespace Contracts;

public sealed record ResolveComplaintRequest(
    string Resolution,
    string InternalNotes,
    int GrantCredits = 0);
