
namespace Contracts;

public interface IAdminUsersClient
{
    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminUsersAsync(q, status, page, pageSize, cancellationToken);
    Task<AdminUserDto> LockUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<AdminUserDto> LockUserAsync(int userId, string bearerToken, CancellationToken cancellationToken = default) => LockUserAsync(userId, cancellationToken);
    Task<AdminUserDto> UnlockUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<AdminUserDto> UnlockUserAsync(int userId, string bearerToken, CancellationToken cancellationToken = default) => UnlockUserAsync(userId, cancellationToken);
    Task ResendUserActivationAsync(int userId, CancellationToken cancellationToken = default);
    Task ResendUserActivationAsync(int userId, string bearerToken, CancellationToken cancellationToken = default) => ResendUserActivationAsync(userId, cancellationToken);
    Task<AdjustCreditsResultDto> AdjustUserCreditsAsync(int userId, AdjustCreditsRequest request, CancellationToken cancellationToken = default);
    Task<AdjustCreditsResultDto> AdjustUserCreditsAsync(int userId, AdjustCreditsRequest request, string bearerToken, CancellationToken cancellationToken = default) => AdjustUserCreditsAsync(userId, request, cancellationToken);
}
