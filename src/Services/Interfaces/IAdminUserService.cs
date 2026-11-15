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

    Task<AdminUserDetailDto> GetUserDetailAsync(
        int targetUserId,
        CancellationToken cancellationToken = default);

    Task ResendActivationAsync(
        int targetUserId,
        CancellationToken cancellationToken = default);

    /// <summary>ADR-0018: marks the address verified on the learner's behalf. The escape route when
    /// the relay is misconfigured or the mail never arrives — without it a verification gate with a
    /// broken mailer locks every new account out with no fix short of editing the database.</summary>
    Task VerifyEmailAsync(
        int targetUserId,
        CancellationToken cancellationToken = default);
}
