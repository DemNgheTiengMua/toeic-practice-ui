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
}
