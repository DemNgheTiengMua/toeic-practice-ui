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
}
