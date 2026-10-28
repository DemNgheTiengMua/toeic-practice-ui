using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>Who someone is once signed in. <paramref name="Role"/> is one of Roles (Contracts).
/// <paramref name="EmailVerified"/> gates buying credits, never the free loop.
/// <paramref name="SessionStamp"/> is derived from the current password hash and rides in the
/// access token; a password change alters it, which ends every session issued before.</summary>
public sealed record AuthUser(
    int UserId, string Email, string? FullName, string Role, bool EmailVerified, string SessionStamp);
