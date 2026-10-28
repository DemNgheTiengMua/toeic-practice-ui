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

public sealed partial class AuthService(
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
}
