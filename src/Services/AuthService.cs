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

/// <summary>Thrown when a caller asks for something identity refuses: duplicate email at
/// registration, bad credentials at sign-in. <see cref="Code"/> is the API's stable error code.</summary>
public sealed class AuthException(string code, string message) : Exception(message)
{
    public const string DuplicateEmail = AuthErrorCodes.EmailTaken;
    public const string InvalidCredentials = AuthErrorCodes.InvalidCredentials;
    public const string Locked = AuthErrorCodes.Locked;
    public const string TokenInvalid = AuthErrorCodes.TokenInvalid;

    public string Code { get; } = code;
}

public interface IAuthService
{
    Task<AuthUser> RegisterAsync(RegisterInput input, CancellationToken cancellationToken = default);

    Task<AuthUser> SignInAsync(SignInInput input, CancellationToken cancellationToken = default);

    /// <summary>Re-checks a signed-in user on every request. Locked is reported apart from Ended
    /// (soft-deleted, missing, or a session stamp from before the last password change) so the
    /// site can say "contact support" only when that's true.</summary>
    Task<SessionStatus> CheckSessionAsync(int userId, string? sessionStamp, CancellationToken cancellationToken = default);

    /// <summary>The user's live record. Callers that re-validate a session against the database
    /// (the MVC site's cookie check) must use this, not the token's claims — the claims are a
    /// sign-in-time snapshot.</summary>
    Task<AuthUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>The learner's editable profile: name, phone, time zone (an IANA id from the
    /// Contracts.TimeZones shortlist).</summary>
    Task<ProfileInfo> GetProfileAsync(int userId, CancellationToken cancellationToken = default);

    Task<ProfileInfo> UpdateProfileAsync(int userId, UpdateProfileInput input, CancellationToken cancellationToken = default);

    /// <summary>Requires the current password — a stolen session must not be enough to change it.</summary>
    Task ChangePasswordAsync(int userId, ChangePasswordInput input, CancellationToken cancellationToken = default);

    /// <summary>Consumes an activation token and marks the email verified. Unknown, used or
    /// expired tokens all fail with <see cref="AuthException.TokenInvalid"/>.</summary>
    Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Issues a fresh activation link, retiring any earlier one. A no-op for an already
    /// verified or missing account. A failed send raises <see cref="EmailDeliveryException"/> so the
    /// signed-in learner can be told to retry (no enumeration risk: they own the account).</summary>
    Task ResendActivationAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Emails a reset link if the address has a live account. Unknown address and failed
    /// send both complete normally (the failure is logged), so the outcome can't enumerate accounts.</summary>
    Task RequestPasswordResetAsync(ForgotPasswordInput input, CancellationToken cancellationToken = default);

    /// <summary>Consumes a reset token and replaces the password. Also verifies the email: following
    /// the link proves the learner controls the mailbox.</summary>
    Task ResetPasswordAsync(ResetPasswordInput input, CancellationToken cancellationToken = default);
}

/// <summary>What the profile page edits.</summary>
public sealed record ProfileInfo(string FullName, string? Phone, string TimeZoneId);

public enum SessionStatus
{
    Active,
    Locked,
    Ended,
}

/// <summary>Where emailed links point: the MVC site, the only thing a learner's browser talks to.</summary>
public sealed class AuthLinkOptions
{
    public const string SectionName = "AuthLinks";

    public string PublicBaseUrl { get; set; } = "http://localhost:5000";
}

public sealed class AuthService(
    CompanionDbContext db,
    IEmailSender mail,
    TimeProvider clock,
    AuthLinkOptions links,
    ILogger<AuthService> log) : IAuthService
{
    // The framework hasher (PBKDF2-SHA256, iterated, salted per password, format version 1).
    private static readonly PasswordHasher<User> Hasher = new();

    public static readonly TimeSpan ActivationLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(15);

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
            TermsAcceptedAt = clock.GetUtcNow(),
            SecurityStamp = Guid.NewGuid().ToString(),
            FailedAttempts = 0,
        };
        // The column is VARBINARY; the framework hasher works in base64 strings — convert at the boundary.
        user.PasswordHash = Convert.FromBase64String(Hasher.HashPassword(user, input.Password));

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        // The account exists whether or not the mail goes out; a failed send is logged and the
        // learner can resend from the dashboard reminder.
        try
        {
            await SendActivationAsync(user, cancellationToken);
        }
        catch (EmailDeliveryException ex)
        {
            log.LogError(ex, "Activation mail for user {UserId} could not be sent", user.UserId);
        }

        return new AuthUser(
            user.UserId, user.Email, user.FullName, Roles.Learner, EmailVerified: false, user.SecurityStamp);
    }

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

    public async Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (user, now) = await ConsumeAsync(db.EmailVerificationTokens, token, cancellationToken);

        user.EmailVerifiedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResendActivationAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .SingleOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted && !u.IsLocked, cancellationToken);
        if (user is null || user.EmailVerifiedAt is not null)
        {
            return;
        }

        await SendActivationAsync(user, cancellationToken);
    }

    public async Task RequestPasswordResetAsync(ForgotPasswordInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        var email = input.Email.Trim().ToLowerInvariant();
        var user = await db.Users
            .SingleOrDefaultAsync(u => !u.IsDeleted && u.Email.ToLower() == email, cancellationToken);
        // Locked behaves like unknown: no link, same quiet outcome.
        if (user is null || user.IsLocked)
        {
            return;
        }

        var token = await IssueAsync(db.PasswordResetTokens, user, ResetLifetime, cancellationToken);
        try
        {
            await SendResetMailAsync(user, token, cancellationToken);
        }
        catch (EmailDeliveryException ex)
        {
            // Swallowed, not surfaced: a failure only a registered address can produce would tell
            // the caller the account exists.
            log.LogError(ex, "Password reset mail for user {UserId} could not be sent", user.UserId);
        }
    }

    private Task SendResetMailAsync(User user, string token, CancellationToken cancellationToken) =>
        mail.SendAsync(new EmailMessage(
            user.Email,
            "Reset your extra efficient password",
            $"""
            Hello {user.FullName ?? user.Email},

            Someone asked to reset the password for this account. To choose a new one, open:

            {Link("/auth/reset-password", token)}

            The link works once and expires in {ResetLifetime.TotalMinutes:0} minutes. If you didn't
            ask for this, ignore this email; your password stays the same.
            """),
            cancellationToken);

    public async Task ResetPasswordAsync(ResetPasswordInput input, CancellationToken cancellationToken = default)
    {
        // Validate before touching the token, so a too-short password doesn't burn the link.
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (user, now) = await ConsumeAsync(db.PasswordResetTokens, input.Token, cancellationToken);

        user.PasswordHash = Convert.FromBase64String(Hasher.HashPassword(user, input.NewPassword));
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.FailedAttempts = 0;
        user.LockoutEnd = null;
        user.EmailVerifiedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SendActivationAsync(User user, CancellationToken cancellationToken)
    {
        var token = await IssueAsync(db.EmailVerificationTokens, user, ActivationLifetime, cancellationToken);
        await mail.SendAsync(new EmailMessage(
            user.Email,
            "Verify your extra efficient email",
            $"""
            Hello {user.FullName ?? user.Email},

            Welcome to extra efficient. Confirm this is your email address by opening:

            {Link("/auth/verify-email", token)}

            The link works once and expires in {ActivationLifetime.TotalHours:0} hours. You can keep
            practising in the meantime; buying credits needs a verified email.
            """),
            cancellationToken);
    }

    /// <summary>Stores a fresh link for <paramref name="user"/> and returns the raw token for the
    /// email. Only the newest link is live: the user's earlier unused links are retired.</summary>
    private async Task<string> IssueAsync<TToken>(
        DbSet<TToken> tokens, User user, TimeSpan lifetime, CancellationToken cancellationToken)
        where TToken : class, IOneTimeToken, new()
    {
        var now = clock.GetUtcNow();
        await RetireLinksAsync(tokens, user.UserId, now, cancellationToken);

        var token = NewToken();
        tokens.Add(new TToken { UserId = user.UserId, TokenHash = HashToken(token), ExpiresAt = now + lifetime });
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    /// <summary>Marks every unused link of this kind for the user as used, so none of them works.</summary>
    private static Task RetireLinksAsync<TToken>(
        DbSet<TToken> tokens, int userId, DateTimeOffset now, CancellationToken cancellationToken)
        where TToken : class, IOneTimeToken =>
        tokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);

    /// <summary>Marks a live link used and returns its (tracked) owner. Call inside a transaction
    /// so the token and whatever it unlocks commit together. Unknown, used, expired, or owned by a
    /// deleted account all fail the same way.</summary>
    private async Task<(User User, DateTimeOffset Now)> ConsumeAsync<TToken>(
        DbSet<TToken> tokens, string? rawToken, CancellationToken cancellationToken)
        where TToken : class, IOneTimeToken
    {
        var now = clock.GetUtcNow();
        var hash = HashToken(rawToken);
        // A locked account's links are dead too: a lock must not be escapable through an emailed link.
        var live = tokens.Where(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > now
            && !t.User.IsDeleted && !t.User.IsLocked);
        var user = await live.Select(t => t.User).SingleOrDefaultAsync(cancellationToken);

        // The conditional update is the single-use guarantee: of two concurrent clicks only one
        // still finds UsedAt NULL and gets a row back.
        if (user is null
            || await live.ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken) == 0)
        {
            throw new AuthException(AuthException.TokenInvalid, "This link is invalid, already used, or expired. Ask for a new one.");
        }

        return (user, now);
    }

    // 256 bits from the OS CSPRNG, base64url so it rides in a query string unescaped.
    private static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    // Only the hash is stored; a database leak doesn't hand out working links.
    private static byte[] HashToken(string? token) => SHA256.HashData(Encoding.ASCII.GetBytes(token ?? ""));

    private string Link(string path, string token) =>
        $"{links.PublicBaseUrl.TrimEnd('/')}{path}?token={token}";

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
