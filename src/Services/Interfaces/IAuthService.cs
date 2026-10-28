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
