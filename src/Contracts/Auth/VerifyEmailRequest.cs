
namespace Contracts;

/// <summary>The token from an emailed activation link.</summary>
public sealed record VerifyEmailRequest(string Token);
