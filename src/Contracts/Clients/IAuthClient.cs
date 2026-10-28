
namespace Contracts;

public interface IAuthClient
{
    Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthTokenResponse> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);
    Task<AuthUserDto> GetMeAsync(CancellationToken cancellationToken = default);
    Task<AuthUserDto> GetMeAsync(string bearerToken, CancellationToken cancellationToken = default) => GetMeAsync(cancellationToken);
    Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default);
    Task ResendActivationAsync(CancellationToken cancellationToken = default);
    Task ResendActivationAsync(string bearerToken, CancellationToken cancellationToken = default) => ResendActivationAsync(cancellationToken);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
