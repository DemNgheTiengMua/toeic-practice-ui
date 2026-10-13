namespace Contracts;

/// <summary>
/// The error body every API failure carries. <see cref="Code"/> is the stable machine-readable
/// key the MVC site switches on; the rest follows RFC 9457 ProblemDetails.
/// </summary>
public sealed record ApiError(string Code, string Title, int Status, string? Detail = null)
{
    public static class Codes
    {
        public const string Unexpected = "UNEXPECTED";

        public const string NotFound = "NOT_FOUND";

        public const string Validation = "VALIDATION";

        /// <summary>Registration with an email that already has a live account.</summary>
        public const string EmailTaken = "AUTH_EMAIL_TAKEN";

        /// <summary>Unknown email or wrong password at sign-in (deliberately indistinguishable).</summary>
        public const string InvalidCredentials = "AUTH_INVALID_CREDENTIALS";

        /// <summary>A locked account, at sign-in or on any later request.</summary>
        public const string Locked = "AUTH_LOCKED";

        /// <summary>No valid bearer token on a request that needs one.</summary>
        public const string Unauthenticated = "AUTH_REQUIRED";

        /// <summary>Signed in, but the role doesn't allow this (e.g. a learner on an admin endpoint).</summary>
        public const string Forbidden = "AUTH_FORBIDDEN";
    }
}
