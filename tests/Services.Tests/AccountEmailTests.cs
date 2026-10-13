using System.Text;
using BusinessObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Services.Tests;

/// <summary>Activation and password reset, driven through <see cref="IAuthService"/> with the
/// mail fake and a hand-moved clock (issue #5).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class AccountEmailTests(DatabaseFixture database)
{
    private const string VerifyPath = "/auth/verify-email";
    private const string ResetPath = "/auth/reset-password";

    private readonly RecordingEmailSender mail = new();
    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);

    private AuthService CreateService() => new(
        DbContextFactory.Create(database.ConnectionString),
        mail,
        clock,
        new AuthLinkOptions { PublicBaseUrl = "http://localhost:5000" },
        NullLogger<AuthService>.Instance);

    private async Task<AuthUser> RegisterAsync(string email, string password = "first-password")
    {
        await database.SqlAsync("""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            """);
        return await CreateService().RegisterAsync(new RegisterInput("Learner", email, null, password, true));
    }

    [Fact]
    public async Task Register_emails_an_activation_link_and_leaves_the_account_unverified()
    {
        var user = await RegisterAsync("activate@example.com");

        Assert.False(user.EmailVerified);
        var message = Assert.Single(mail.To("activate@example.com"));
        Assert.Contains("http://localhost:5000" + VerifyPath + "?token=", message.Body);
    }

    [Fact]
    public async Task Unverified_learner_can_sign_in()
    {
        await RegisterAsync("unverified@example.com", "right-password");

        var user = await CreateService().SignInAsync(new SignInInput("unverified@example.com", "right-password"));

        Assert.False(user.EmailVerified);
    }

    [Fact]
    public async Task Activation_link_verifies_the_email_once_only()
    {
        var user = await RegisterAsync("once@example.com");
        var token = mail.LastToken("once@example.com", VerifyPath);

        await CreateService().VerifyEmailAsync(token);

        Assert.True((await CreateService().GetUserAsync(user.UserId))!.EmailVerified);
        var reuse = await Assert.ThrowsAsync<AuthException>(() => CreateService().VerifyEmailAsync(token));
        Assert.Equal(AuthException.TokenInvalid, reuse.Code);
    }

    [Fact]
    public async Task Activation_link_expires_after_24_hours()
    {
        var user = await RegisterAsync("late@example.com");
        var token = mail.LastToken("late@example.com", VerifyPath);

        clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<AuthException>(() => CreateService().VerifyEmailAsync(token));
        Assert.Equal(AuthException.TokenInvalid, ex.Code);
        Assert.False((await CreateService().GetUserAsync(user.UserId))!.EmailVerified);
    }

    [Fact]
    public async Task Resending_activation_retires_the_earlier_link()
    {
        var user = await RegisterAsync("resend@example.com");
        var first = mail.LastToken("resend@example.com", VerifyPath);

        await CreateService().ResendActivationAsync(user.UserId);
        var second = mail.LastToken("resend@example.com", VerifyPath);

        Assert.NotEqual(first, second);
        await Assert.ThrowsAsync<AuthException>(() => CreateService().VerifyEmailAsync(first));
        await CreateService().VerifyEmailAsync(second);
        Assert.True((await CreateService().GetUserAsync(user.UserId))!.EmailVerified);
    }

    [Fact]
    public async Task Resending_activation_to_a_verified_learner_sends_nothing()
    {
        var user = await RegisterAsync("already@example.com");
        await CreateService().VerifyEmailAsync(mail.LastToken("already@example.com", VerifyPath));

        await CreateService().ResendActivationAsync(user.UserId);

        Assert.Single(mail.To("already@example.com"));
    }

    [Fact]
    public async Task The_emailed_token_is_not_what_the_database_stores()
    {
        var user = await RegisterAsync("hashed@example.com");
        var token = mail.LastToken("hashed@example.com", VerifyPath);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = (await db.EmailVerificationTokens.SingleAsync(t => t.UserId == user.UserId)).TokenHash;
        Assert.NotEqual(Encoding.ASCII.GetBytes(token), stored);
        Assert.DoesNotContain(token, Convert.ToBase64String(stored));
        Assert.DoesNotContain(token, Encoding.ASCII.GetString(stored));
    }

    [Fact]
    public async Task Password_reset_completes_quietly_when_mail_cannot_be_sent()
    {
        await RegisterAsync("quiet@example.com", "old-password");
        mail.Fail = true;

        // Same outcome as an unknown address: the caller can't tell the account exists.
        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("quiet@example.com"));
        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("ghost-quiet@example.com"));
    }

    [Fact]
    public async Task Resending_activation_reports_a_failed_send()
    {
        var user = await RegisterAsync("loud@example.com");
        mail.Fail = true;

        await Assert.ThrowsAsync<EmailDeliveryException>(() => CreateService().ResendActivationAsync(user.UserId));
    }

    [Fact]
    public async Task Password_reset_request_validates_the_email()
    {
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("not-an-email")));
    }

    [Fact]
    public async Task Register_still_succeeds_when_mail_cannot_be_sent()
    {
        mail.Fail = true;

        var user = await RegisterAsync("nomail@example.com");

        Assert.True(user.UserId > 0);
        Assert.False(user.EmailVerified);
    }

    [Fact]
    public async Task Changing_the_password_kills_an_outstanding_reset_link()
    {
        // Someone who got a reset link (e.g. from a hijacked mailbox) must not still be able to
        // take the account over after the owner notices and changes their password.
        var user = await RegisterAsync("race@example.com", "old-password");
        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("race@example.com"));
        var stolen = mail.LastToken("race@example.com", ResetPath);

        await CreateService().ChangePasswordAsync(user.UserId, new ChangePasswordInput("old-password", "owner-password"));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().ResetPasswordAsync(new ResetPasswordInput(stolen, "attacker-password")));
        Assert.Equal(AuthException.TokenInvalid, ex.Code);
        await CreateService().SignInAsync(new SignInInput("race@example.com", "owner-password"));
    }

    [Fact]
    public async Task A_reset_kills_the_other_outstanding_reset_links()
    {
        // Two links were requested; consuming one must retire the rest, so an older link can't be
        // replayed later to re-take the account.
        await RegisterAsync("twolinks@example.com", "old-password");
        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("twolinks@example.com"));
        var first = mail.LastToken("twolinks@example.com", ResetPath);
        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("twolinks@example.com"));
        var second = mail.LastToken("twolinks@example.com", ResetPath);
        Assert.NotEqual(first, second);

        await CreateService().ResetPasswordAsync(new ResetPasswordInput(second, "new-password"));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().ResetPasswordAsync(new ResetPasswordInput(first, "attacker-password")));
        Assert.Equal(AuthException.TokenInvalid, ex.Code);
    }

    [Fact]
    public async Task Password_reset_replaces_the_password_and_the_link_works_once()
    {
        await RegisterAsync("reset@example.com", "old-password");

        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("RESET@example.com"));
        var token = mail.LastToken("reset@example.com", ResetPath);
        await CreateService().ResetPasswordAsync(new ResetPasswordInput(token, "new-password"));

        await CreateService().SignInAsync(new SignInInput("reset@example.com", "new-password"));
        var old = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().SignInAsync(new SignInInput("reset@example.com", "old-password")));
        Assert.Equal(AuthException.InvalidCredentials, old.Code);
        var reuse = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().ResetPasswordAsync(new ResetPasswordInput(token, "third-password")));
        Assert.Equal(AuthException.TokenInvalid, reuse.Code);
    }

    [Fact]
    public async Task Password_reset_link_expires_after_15_minutes()
    {
        await RegisterAsync("slow@example.com", "old-password");
        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("slow@example.com"));
        var token = mail.LastToken("slow@example.com", ResetPath);

        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().ResetPasswordAsync(new ResetPasswordInput(token, "new-password")));
        Assert.Equal(AuthException.TokenInvalid, ex.Code);
        await CreateService().SignInAsync(new SignInInput("slow@example.com", "old-password"));
    }

    [Fact]
    public async Task Password_reset_for_an_unknown_email_sends_nothing_and_does_not_fail()
    {
        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("nobody@example.com"));

        Assert.Empty(mail.To("nobody@example.com"));
    }

    [Fact]
    public async Task Password_reset_proves_the_mailbox_so_it_verifies_the_email()
    {
        var user = await RegisterAsync("proves@example.com", "old-password");
        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("proves@example.com"));

        await CreateService().ResetPasswordAsync(
            new ResetPasswordInput(mail.LastToken("proves@example.com", ResetPath), "new-password"));

        Assert.True((await CreateService().GetUserAsync(user.UserId))!.EmailVerified);
    }

    [Fact]
    public async Task Password_reset_ends_sessions_issued_under_the_old_password()
    {
        await RegisterAsync("sessions@example.com", "old-password");
        var before = await CreateService().SignInAsync(new SignInInput("sessions@example.com", "old-password"));
        Assert.Equal(SessionStatus.Active, await CreateService().CheckSessionAsync(before.UserId, before.SessionStamp));

        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("sessions@example.com"));
        await CreateService().ResetPasswordAsync(
            new ResetPasswordInput(mail.LastToken("sessions@example.com", ResetPath), "new-password"));

        Assert.Equal(SessionStatus.Ended, await CreateService().CheckSessionAsync(before.UserId, before.SessionStamp));
        var after = await CreateService().SignInAsync(new SignInInput("sessions@example.com", "new-password"));
        Assert.Equal(SessionStatus.Active, await CreateService().CheckSessionAsync(after.UserId, after.SessionStamp));
    }

    [Fact]
    public async Task A_locked_account_gets_no_reset_link_and_cannot_use_one()
    {
        var user = await RegisterAsync("locked-reset@example.com", "old-password");
        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("locked-reset@example.com"));
        var issuedBeforeLock = mail.LastToken("locked-reset@example.com", ResetPath);
        await database.SqlAsync($"UPDATE Users SET IsLocked = 1 WHERE UserId = {user.UserId}");
        var mailsBefore = mail.To("locked-reset@example.com").Count();

        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("locked-reset@example.com"));

        Assert.Equal(mailsBefore, mail.To("locked-reset@example.com").Count());
        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().ResetPasswordAsync(new ResetPasswordInput(issuedBeforeLock, "new-password")));
        Assert.Equal(AuthException.TokenInvalid, ex.Code);
    }

    [Fact]
    public async Task A_locked_account_cannot_verify_or_resend_activation()
    {
        var user = await RegisterAsync("locked-verify@example.com");
        var token = mail.LastToken("locked-verify@example.com", VerifyPath);
        await database.SqlAsync($"UPDATE Users SET IsLocked = 1 WHERE UserId = {user.UserId}");

        await CreateService().ResendActivationAsync(user.UserId);

        Assert.Single(mail.To("locked-verify@example.com"));
        await Assert.ThrowsAsync<AuthException>(() => CreateService().VerifyEmailAsync(token));
    }

    [Fact]
    public async Task Password_reset_validates_the_new_password()
    {
        await RegisterAsync("weak-reset@example.com", "old-password");
        await CreateService().RequestPasswordResetAsync(new ForgotPasswordInput("weak-reset@example.com"));
        var token = mail.LastToken("weak-reset@example.com", ResetPath);

        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            CreateService().ResetPasswordAsync(new ResetPasswordInput(token, "short")));
        // A rejected attempt must not burn the link.
        await CreateService().ResetPasswordAsync(new ResetPasswordInput(token, "long-enough"));
    }
}
