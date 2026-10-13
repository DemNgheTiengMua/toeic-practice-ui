namespace Contracts;

public sealed record RegisterRequest(string FullName, string Email, string? Phone, string Password, bool TermsAccepted);

public sealed record SignInRequest(string Email, string Password);

/// <summary>Sign-in result: the token the API minted and who it belongs to.</summary>
public sealed record AuthTokenResponse(string Token, DateTimeOffset ExpiresAt, AuthUserDto User);

public sealed record AuthUserDto(int UserId, string Email, string? FullName, string Role, bool EmailVerified);

public sealed record ProfileDto(string FullName, string? Phone, string TimeZoneId);

public sealed record UpdateProfileRequest(string FullName, string? Phone, string TimeZoneId);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>The token from an emailed activation link.</summary>
public sealed record VerifyEmailRequest(string Token);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);
