using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>Thrown when a caller asks for something identity refuses: duplicate email at
/// registration, bad credentials at sign-in. <see cref="Code"/> is the API's stable error code.</summary>
public sealed class AuthException(string code, string message) : Exception(message)
{
    public const string DuplicateEmail = AuthErrorCodes.EmailTaken;
    public const string InvalidCredentials = AuthErrorCodes.InvalidCredentials;
    public const string Locked = AuthErrorCodes.Locked;

    public string Code { get; } = code;
}

public interface IAuthService
{
    Task<AuthUser> RegisterAsync(RegisterInput input, CancellationToken cancellationToken = default);

    Task<AuthUser> SignInAsync(SignInInput input, CancellationToken cancellationToken = default);

    /// <summary>Re-checks a signed-in user on every request: locked, soft-deleted or missing fails.</summary>
    Task<bool> IsActiveAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>The user's live record. Callers that re-validate a session against the database
    /// (the MVC site's cookie check) must use this, not the token's claims — the claims are a
    /// sign-in-time snapshot.</summary>
    Task<AuthUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
}

public sealed class AuthService(CompanionDbContext db) : IAuthService
{
    // The framework hasher (PBKDF2-SHA256, iterated, salted per password, format version 1).
    private static readonly PasswordHasher<User> Hasher = new();

    public async Task<AuthUser> RegisterAsync(RegisterInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        var email = input.Email.Trim().ToLowerInvariant();
        var emailTaken = await db.Users
            .AnyAsync(u => !u.IsDeleted && u.Email.ToLower() == email, cancellationToken);
        if (emailTaken)
        {
            throw new AuthException(AuthException.DuplicateEmail, "An account with this email already exists.");
        }

        var learnerRoleId = await db.Roles
            .Where(r => r.Code == Roles.Learner)
            .Select(r => r.RoleId)
            .SingleOrDefaultAsync(cancellationToken);
        if (learnerRoleId == 0)
        {
            // The database wasn't seeded (docs/seed-demo.sql inserts both roles) — surface a
            // configuration error, not an opaque InvalidOperationException from SingleAsync.
            throw new InvalidOperationException("The 'learner' role is missing; run docs/seed-demo.sql.");
        }

        var user = new User
        {
            RoleId = learnerRoleId,
            Email = email,
            FullName = input.FullName.Trim(),
            Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
            TermsAcceptedAt = DateTimeOffset.UtcNow,
        };
        // The column is VARBINARY; the framework hasher works in base64 strings — convert at the boundary.
        user.PasswordHash = Convert.FromBase64String(Hasher.HashPassword(user, input.Password));

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthUser(user.UserId, user.Email, user.FullName, Roles.Learner);
    }

    public async Task<AuthUser> SignInAsync(SignInInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        var email = input.Email.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => !u.IsDeleted && u.Email.ToLower() == email, cancellationToken);

        // Same refusal for "no such email" and "wrong password" so the response can't enumerate accounts.
        if (user is null
            || Hasher.VerifyHashedPassword(user, Convert.ToBase64String(user.PasswordHash), input.Password)
                == PasswordVerificationResult.Failed)
        {
            throw new AuthException(AuthException.InvalidCredentials, "Incorrect email or password.");
        }

        if (user.IsLocked)
        {
            throw new AuthException(AuthException.Locked, "This account is locked. Contact support.");
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return new AuthUser(user.UserId, user.Email, user.FullName, user.Role.Code);
    }

    public async Task<bool> IsActiveAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await db.Users
            .AnyAsync(u => u.UserId == userId && !u.IsLocked && !u.IsDeleted, cancellationToken);
    }

    public async Task<AuthUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId && !u.IsDeleted)
            .Select(u => new AuthUser(u.UserId, u.Email, u.FullName, u.Role.Code))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
