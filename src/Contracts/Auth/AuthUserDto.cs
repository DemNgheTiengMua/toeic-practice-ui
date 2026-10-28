
namespace Contracts;

public sealed record AuthUserDto(int UserId, string Email, string? FullName, string Role, bool EmailVerified);
