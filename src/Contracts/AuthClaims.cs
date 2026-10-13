using System.Security.Claims;

namespace Contracts;

/// <summary>Builds the compact claim list for one signed-in user. Shared by the API (which puts
/// them in the JWT) and the MVC site (which mirrors them into the cookie identity) so the two
/// can never drift on names or on the optional name claim.</summary>
public static class AuthClaims
{
    public static List<Claim> For(int userId, string email, string role, string? fullName)
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

        return claims;
    }
}
