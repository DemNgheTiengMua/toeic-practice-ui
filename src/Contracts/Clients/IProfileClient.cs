
namespace Contracts;

public interface IProfileClient
{
    Task<ProfileDto> GetProfileAsync(CancellationToken cancellationToken = default);
    Task<ProfileDto> GetProfileAsync(string bearerToken, CancellationToken cancellationToken = default) => GetProfileAsync(cancellationToken);
    Task<ProfileDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default);
    Task<ProfileDto> UpdateProfileAsync(UpdateProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateProfileAsync(request, cancellationToken);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, string bearerToken, CancellationToken cancellationToken = default) => ChangePasswordAsync(request, cancellationToken);
}
