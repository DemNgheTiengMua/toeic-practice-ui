namespace Contracts;

/// <summary>
/// The auth error codes Services throws and ApiError.Codes puts on the wire. Both live in
/// Contracts — the one assembly every layer references — so the strings can't drift.
/// </summary>
public static class AuthErrorCodes
{
    /// <summary>Registration with an email that already has a live account.</summary>
    public const string EmailTaken = "AUTH_EMAIL_TAKEN";

    /// <summary>Unknown email or wrong password at sign-in (deliberately indistinguishable).</summary>
    public const string InvalidCredentials = "AUTH_INVALID_CREDENTIALS";

    /// <summary>A locked account, at sign-in or on any later request.</summary>
    public const string Locked = "AUTH_LOCKED";

    /// <summary>An activation or reset link that is unknown, already used, or expired.</summary>
    public const string TokenInvalid = "AUTH_TOKEN_INVALID";
}
