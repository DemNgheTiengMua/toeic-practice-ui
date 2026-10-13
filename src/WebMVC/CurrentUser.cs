using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;

namespace WebMVC;

/// <summary>Reads the signed-in user from the cookie principal and hands the stored JWT to the
/// API client for forwarding (ADR-0002). The JWT rides in the cookie's authentication properties,
/// so page script can never read it.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsSignedIn => Principal?.Identity?.IsAuthenticated == true;

    public int? UserId => int.TryParse(Principal?.FindFirst(IAccessTokenFactory.ClaimUserId)?.Value, out var id) ? id : null;

    public string? Email => Principal?.FindFirst(IAccessTokenFactory.ClaimEmail)?.Value;

    public string? FullName => Principal?.FindFirst(IAccessTokenFactory.ClaimName)?.Value;

    public string? Role => Principal?.FindFirst(IAccessTokenFactory.ClaimRole)?.Value;

    public bool IsAdmin => Role == Roles.Admin;

    public async Task<string?> GetBearerTokenAsync()
    {
        var http = accessor.HttpContext;
        if (http is null || !IsSignedIn)
        {
            return null;
        }

        // Cookie auth has no token store; the JWT rides in the ticket's properties (encrypted
        // into the cookie), which is exactly what the forwarding contract needs.
        var result = await http.AuthenticateAsync();
        return result.Properties?.GetString("jwt");
    }
}
