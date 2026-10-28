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

    public async Task<AuthUser> SignInAsync(SignInInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        var email = input.Email.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => !u.IsDeleted && u.Email.ToLower() == email, cancellationToken);

        if (user is null)
        {
            throw new AuthException(AuthException.InvalidCredentials, "Incorrect email or password.");
        }

        if (user.IsLocked)
        {
            throw new AuthException(AuthException.Locked, "This account is locked. Contact support.");
        }

        var now = clock.GetUtcNow();
        if (user.LockoutEnd.HasValue)
        {
            if (user.LockoutEnd.Value > now)
            {
                throw new AuthException(AuthException.Locked, "Account is temporarily locked due to failed sign-in attempts. Try again later.");
            }

            user.LockoutEnd = null;
            user.FailedAttempts = 0;
        }

        if (Hasher.VerifyHashedPassword(user, Convert.ToBase64String(user.PasswordHash), input.Password)
            == PasswordVerificationResult.Failed)
        {
            user.FailedAttempts++;
            if (user.FailedAttempts >= 5)
            {
                user.LockoutEnd = now.AddMinutes(15);
            }
            await db.SaveChangesAsync(cancellationToken);
            throw new AuthException(AuthException.InvalidCredentials, "Incorrect email or password.");
        }

        user.FailedAttempts = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return new AuthUser(
            user.UserId, user.Email, user.FullName, user.Role.Code, user.EmailVerifiedAt != null, user.SecurityStamp);
    }

    public async Task<SessionStatus> CheckSessionAsync(int userId, string? sessionStamp, CancellationToken cancellationToken = default)
    {
        var row = await db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId && !u.IsDeleted)
            .Select(u => new { u.IsLocked, u.SecurityStamp })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return SessionStatus.Ended;
        }

        if (row.IsLocked)
        {
            return SessionStatus.Locked;
        }

        if (sessionStamp is null || row.SecurityStamp is null)
        {
            return SessionStatus.Ended;
        }

        // Limit stackalloc to reasonable max length (SecurityStamp is 36 chars) to prevent stack overflow from untrusted claims
        if (sessionStamp.Length > 128)
        {
            return SessionStatus.Ended;
        }

        Span<byte> expectedBytes = stackalloc byte[Encoding.UTF8.GetMaxByteCount(row.SecurityStamp.Length)];
        var expectedLen = Encoding.UTF8.GetBytes(row.SecurityStamp, expectedBytes);

        Span<byte> actualBytes = stackalloc byte[Encoding.UTF8.GetMaxByteCount(sessionStamp.Length)];
        var actualLen = Encoding.UTF8.GetBytes(sessionStamp, actualBytes);

        return CryptographicOperations.FixedTimeEquals(expectedBytes[..expectedLen], actualBytes[..actualLen])
            ? SessionStatus.Active
            : SessionStatus.Ended;
    }

    public async Task<AuthUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var row = await db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId && !u.IsDeleted)
            .Select(u => new { u.UserId, u.Email, u.FullName, Role = u.Role.Code, Verified = u.EmailVerifiedAt != null, u.SecurityStamp })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new AuthUser(row.UserId, row.Email, row.FullName, row.Role, row.Verified, row.SecurityStamp);
    }
}
