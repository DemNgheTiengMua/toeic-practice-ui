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

    public static bool IsAdmin(this ClaimsPrincipal? principal) =>
        principal?.GetRole() == Roles.Admin;
}
