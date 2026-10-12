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
    }
}
