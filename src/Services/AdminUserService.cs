using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

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

public sealed class AdminUserService(
    CompanionDbContext db,
    IAuthService authService,
    TimeProvider clock) : IAdminUserService
{
    public async Task<AdminUsersSummaryDto> GetUsersAsync(
        string? q = null,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var baseLearners = db.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.Role.Code == Roles.Learner);

        var activeCount = await baseLearners
            .CountAsync(u => !u.IsLocked && u.EmailVerifiedAt != null, cancellationToken);

        var lockedCount = await baseLearners
            .CountAsync(u => u.IsLocked, cancellationToken);

        var notActivatedCount = await baseLearners
            .CountAsync(u => !u.IsLocked && u.EmailVerifiedAt == null, cancellationToken);

        var query = baseLearners;

        if (!string.IsNullOrWhiteSpace(q))
        {
            var trimmed = q.Trim().ToLower();
            query = query.Where(u =>
                u.Email.ToLower().Contains(trimmed) ||
                (u.FullName != null && u.FullName.ToLower().Contains(trimmed)) ||
                (u.Phone != null && u.Phone.ToLower().Contains(trimmed)));
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var lowerStatus = status.Trim().ToLower();
            if (lowerStatus == "active")
            {
                query = query.Where(u => !u.IsLocked && u.EmailVerifiedAt != null);
            }
            else if (lowerStatus == "locked")
            {
                query = query.Where(u => u.IsLocked);
            }
            else if (lowerStatus is "not_activated" or "notactivated" or "unverified")
            {
                query = query.Where(u => !u.IsLocked && u.EmailVerifiedAt == null);
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto(
                u.UserId,
                u.Email,
                u.FullName,
                u.Phone,
                db.CreditTransactions.Where(c => c.UserId == u.UserId).Sum(c => (int?)c.Delta) ?? 0,
                db.ExamAttempts.Count(a => a.UserId == u.UserId),
                db.ExamAttempts.Where(a => a.UserId == u.UserId && a.TotalScore != null).Max(a => (int?)a.TotalScore),
                u.LastLoginAt,
                u.IsLocked,
                u.EmailVerifiedAt,
                u.CreatedAt))
            .ToListAsync(cancellationToken);

        return new AdminUsersSummaryDto(
            users,
            totalCount,
            page,
            pageSize,
            activeCount,
            lockedCount,
            notActivatedCount);
    }

    public async Task<AdminUserDto> SetLockAsync(
        int adminUserId,
        int targetUserId,
        bool isLocked,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.UserId == targetUserId && !u.IsDeleted, cancellationToken);

        if (user is null)
        {
            throw new ApiException(ApiError.Codes.UserNotFound, "User not found.", 404);
        }

        if (user.Role.Code == Roles.Admin)
        {
            throw new ApiException(ApiError.Codes.Forbidden, "Cannot modify lock status of an administrator.", 403);
        }

        user.IsLocked = isLocked;
        await db.SaveChangesAsync(cancellationToken);

        var creditsLeft = await db.CreditTransactions
            .Where(c => c.UserId == targetUserId)
            .SumAsync(c => (int?)c.Delta, cancellationToken) ?? 0;

        var testsTaken = await db.ExamAttempts
            .CountAsync(a => a.UserId == targetUserId, cancellationToken);

        var bestScore = await db.ExamAttempts
            .Where(a => a.UserId == targetUserId && a.TotalScore != null)
            .MaxAsync(a => (int?)a.TotalScore, cancellationToken);

        return new AdminUserDto(
            user.UserId,
            user.Email,
            user.FullName,
            user.Phone,
            creditsLeft,
            testsTaken,
            bestScore,
            user.LastLoginAt,
            user.IsLocked,
            user.EmailVerifiedAt,
            user.CreatedAt);
    }

    public async Task<AdjustCreditsResultDto> AdjustCreditsAsync(
        int adminUserId,
        int targetUserId,
        int delta,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (delta == 0)
        {
            throw new ApiException(ApiError.Codes.Validation, "Credit adjustment delta cannot be 0.", 400);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ApiException(ApiError.Codes.Validation, "Reason is required for credit adjustments.", 400);
        }

        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == targetUserId && !u.IsDeleted, cancellationToken);

        if (user is null)
        {
            throw new ApiException(ApiError.Codes.UserNotFound, "User not found.", 404);
        }

        var currentBalance = await db.CreditTransactions
            .Where(c => c.UserId == targetUserId)
            .SumAsync(c => (int?)c.Delta, cancellationToken) ?? 0;

        if (delta < 0 && currentBalance + delta < 0)
        {
            throw new ApiException(ApiError.Codes.InsufficientCredits, "Credit deduction cannot exceed the user's current balance.", 400);
        }

        var trimmedReason = reason.Trim();
        var txn = new CreditTransaction
        {
            UserId = targetUserId,
            Reason = delta > 0 ? "admin_grant" : "admin_revoke",
            Delta = delta,
            AdminUserId = adminUserId,
            Note = trimmedReason,
            CreatedAt = clock.GetUtcNow(),
        };

        db.CreditTransactions.Add(txn);
        await db.SaveChangesAsync(cancellationToken);

        var newBalance = currentBalance + delta;
        return new AdjustCreditsResultDto(txn.TxnId, targetUserId, newBalance, delta, trimmedReason);
    }

    public async Task ResendActivationAsync(
        int targetUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == targetUserId && !u.IsDeleted, cancellationToken);

        if (user is null)
        {
            throw new ApiException(ApiError.Codes.UserNotFound, "User not found.", 404);
        }

        if (user.IsLocked)
        {
            throw new ApiException(ApiError.Codes.Locked, "Cannot resend activation to a locked account.", 400);
        }

        if (user.EmailVerifiedAt != null)
        {
            throw new ApiException(ApiError.Codes.Validation, "Account is already verified.", 400);
        }

        await authService.ResendActivationAsync(targetUserId, cancellationToken);
    }
}
