using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Services;

public sealed partial class AuthService
{

    public async Task<ProfileInfo> GetProfileAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId && !u.IsDeleted)
            .Select(u => new ProfileInfo(u.FullName ?? "", u.Phone, u.TimeZoneId))
            .SingleAsync(cancellationToken);
    }

    public async Task<ProfileInfo> UpdateProfileAsync(int userId, UpdateProfileInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        if (!TimeZones.Contains(input.TimeZoneId))
        {
            throw new ValidationException("Pick a time zone from the list.");
        }

        var user = await db.Users.SingleAsync(u => u.UserId == userId && !u.IsDeleted, cancellationToken);
        user.FullName = input.FullName.Trim();
        user.Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
        user.TimeZoneId = input.TimeZoneId;
        await db.SaveChangesAsync(cancellationToken);

        return new ProfileInfo(user.FullName, user.Phone, user.TimeZoneId);
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        var user = await db.Users.SingleAsync(u => u.UserId == userId && !u.IsDeleted, cancellationToken);
        if (user.IsLocked)
        {
            throw new AuthException(AuthException.Locked, "This account is locked. Contact support.");
        }

        var now = clock.GetUtcNow();
        if (user.LockoutEnd.HasValue)
        {
            if (user.LockoutEnd.Value > now)
            {
                throw new AuthException(AuthException.Locked, "Account is temporarily locked. Try again later.");
            }

            user.LockoutEnd = null;
            user.FailedAttempts = 0;
        }

        if (Hasher.VerifyHashedPassword(user, Convert.ToBase64String(user.PasswordHash), input.CurrentPassword)
                == PasswordVerificationResult.Failed)
        {
            user.FailedAttempts++;
            if (user.FailedAttempts >= 5)
            {
                user.LockoutEnd = now.AddMinutes(15);
            }
            await db.SaveChangesAsync(cancellationToken);
            throw new AuthException(AuthException.InvalidCredentials, "The current password is incorrect.");
        }

        user.FailedAttempts = 0;
        user.LockoutEnd = null;
        user.SecurityStamp = Guid.NewGuid().ToString();

        // Every emailed link issued before the change dies with the old password, or whoever
        // holds one (a hijacked mailbox) could still take the account over, or verify it, after
        // the owner secures it. All writes commit together. The session stamp ends old sessions.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await RetireLinksAsync(db.PasswordResetTokens, userId, now, cancellationToken);
        await RetireLinksAsync(db.EmailVerificationTokens, userId, now, cancellationToken);

        user.PasswordHash = Convert.FromBase64String(Hasher.HashPassword(user, input.NewPassword));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
