using System.Security.Claims;

namespace Contracts;

/// <summary>Builds the compact claim list for one signed-in user. Shared by the API (which puts
/// them in the JWT) and the MVC site (which mirrors them into the cookie identity) so the two
/// can never drift on names or on the optional name claim.
/// <para>
/// Each staff namespace the account holds rides in its own <c>perm</c> claim (D16). That is what
/// makes every gate a <c>RequireClaim</c> on a capability, and it means the principal's authority
/// cannot be widened by anything but a fresh sign-in or re-validation.
/// </para></summary>
public static class AuthClaims
{
    public static List<Claim> For(
        int userId, string email, string role, string? fullName,
        IReadOnlyList<string>? permissions = null)
    {
        var claims = new List<Claim>
        {
            new(IAccessTokenFactory.ClaimUserId, userId.ToString()),
            new(IAccessTokenFactory.ClaimEmail, email),
            new(IAccessTokenFactory.ClaimRole, role),
        };
        if (!string.IsNullOrEmpty(fullName))
        {
            claims.Add(new Claim(IAccessTokenFactory.ClaimName, fullName));
        }

        foreach (var permission in permissions ?? [])
        {
            claims.Add(new Claim(IAccessTokenFactory.ClaimPermission, permission));
        }

        return claims;
    }
}
