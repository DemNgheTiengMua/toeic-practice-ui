using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public interface IAdminUserService
{
    Task<AdminUsersSummaryDto> GetUsersAsync(
        string? q = null,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<AdminUserDto> SetLockAsync(
        int adminUserId,
        int targetUserId,
        bool isLocked,
        CancellationToken cancellationToken = default);

    Task<AdjustCreditsResultDto> AdjustCreditsAsync(
        int adminUserId,
        int targetUserId,
        int delta,
        string reason,
        CancellationToken cancellationToken = default);

    Task ResendActivationAsync(
        int targetUserId,
        CancellationToken cancellationToken = default);
}
