namespace Contracts;

/// <summary>
/// The token contract both apps share: the compact claim names, the single 8-hour lifetime
/// (no refresh token by design, issue #4), and the factory signature. The implementation lives
/// in WebAPI — it is the only app that holds the signing key and mints tokens.
/// </summary>
public interface IAccessTokenFactory
{
    public const int LifetimeHours = 8;

    public const string ClaimUserId = "uid";
    public const string ClaimEmail = "email";
    public const string ClaimRole = "role";
    public const string ClaimName = "name";

    AccessToken Create(int userId, string email, string role, string? fullName);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
