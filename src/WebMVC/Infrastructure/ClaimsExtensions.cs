using System.Security.Claims;
using Contracts;

namespace WebMVC.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static bool IsSignedIn(this ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true;

    public static int? GetUserId(this ClaimsPrincipal? principal) =>
        int.TryParse(principal?.FindFirst(IAccessTokenFactory.ClaimUserId)?.Value, out var id) ? id : null;

    public static string? GetEmail(this ClaimsPrincipal? principal) =>
        principal?.FindFirst(IAccessTokenFactory.ClaimEmail)?.Value;

    public static string? GetFullName(this ClaimsPrincipal? principal) =>
        principal?.FindFirst(IAccessTokenFactory.ClaimName)?.Value;

    public static string? GetDisplayName(this ClaimsPrincipal? principal) =>
        principal?.GetFullName() ?? principal?.GetEmail();

    public static string? GetRole(this ClaimsPrincipal? principal) =>
        principal?.FindFirst(IAccessTokenFactory.ClaimRole)?.Value;

    /// <summary>D16: the staff namespaces this principal holds, read from its <c>perm</c> claims.</summary>
    public static IReadOnlyList<string> GetPermissions(this ClaimsPrincipal? principal) =>
        principal?.FindAll(IAccessTokenFactory.ClaimPermission).Select(c => c.Value).ToList() ?? [];

    public static bool HasPermission(this ClaimsPrincipal? principal, string permission) =>
        principal?.HasClaim(IAccessTokenFactory.ClaimPermission, permission) == true;

    /// <summary>Whether this account does any staff work at all. Replaces the old "is the role named
    /// admin" test: a small team may hold one namespace and still needs the staff chrome, and a
    /// learner holds none.</summary>
    public static bool IsStaff(this ClaimsPrincipal? principal) => principal.GetPermissions().Count > 0;
}
