
namespace Contracts;

/// <summary>D16: <paramref name="Permissions"/> is the account's live set of staff namespaces.
/// It is additive to <paramref name="Role"/>, which the auth pipeline still needs.</summary>
public sealed record AuthUserDto(
    int UserId, string Email, string? FullName, string Role, bool EmailVerified,
    IReadOnlyList<string>? Permissions = null)
{
    public IReadOnlyList<string> Held => Permissions ?? [];
}
