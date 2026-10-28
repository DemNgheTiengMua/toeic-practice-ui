
namespace Contracts;

public sealed record RegisterRequest(string FullName, string Email, string? Phone, string Password, bool TermsAccepted);
