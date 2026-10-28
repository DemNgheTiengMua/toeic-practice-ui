
namespace Contracts;

/// <summary>Sign-in result: the token the API minted and who it belongs to.</summary>
public sealed record AuthTokenResponse(string Token, DateTimeOffset ExpiresAt, AuthUserDto User);
