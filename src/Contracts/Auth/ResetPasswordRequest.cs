
namespace Contracts;

public sealed record ResetPasswordRequest(string Token, string NewPassword);
