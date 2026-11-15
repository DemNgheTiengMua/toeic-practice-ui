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

    public async Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (user, now) = await ConsumeAsync(db.EmailVerificationTokens, token, cancellationToken);

        user.EmailVerifiedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResendActivationAsync(string email, CancellationToken cancellationToken = default)
    {
        // ADR-0018: reachable WITHOUT a session, because an unverified account cannot sign in. The
        // caller therefore cannot be trusted to own the address, so this mirrors
        // RequestPasswordResetAsync: an unknown or already-verified address completes normally and
        // the answer never says which. A failed send is logged, not raised, for the same reason.
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return;
        }

        var user = await db.Users
            .SingleOrDefaultAsync(
                u => !u.IsDeleted && !u.IsLocked && u.Email.ToLower() == normalized, cancellationToken);
        if (user is null || user.EmailVerifiedAt is not null)
        {
            return;
        }

        try
        {
            await SendActivationAsync(user, cancellationToken);
        }
        catch (EmailDeliveryException ex)
        {
            log.LogError(ex, "Activation mail for user {UserId} could not be sent", user.UserId);
        }
    }
}
