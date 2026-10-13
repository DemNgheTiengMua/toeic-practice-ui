using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AuthServiceTests(DatabaseFixture database)
{
    private async Task SeedRolesAsync()
    {
        // The database is shared across the run; the catalog tests may have seeded roles already.
        await database.SqlAsync("""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Administrator');
            """);
    }

    private AuthService CreateService()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new AuthService(
            db, new RecordingEmailSender(), TimeProvider.System, new AuthLinkOptions(), NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task Register_creates_a_learner_with_hashed_password_and_terms_timestamp()
    {
        await SeedRolesAsync();
        var service = CreateService();

        var user = await service.RegisterAsync(new RegisterInput(
            "Nguyen Van A", "A@Example.com", "+84 901 234 567", "s3cret-pass", TermsAccepted: true));

        Assert.True(user.UserId > 0);
        Assert.Equal("a@example.com", user.Email);
        Assert.Equal(Roles.Learner, user.Role);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
        Assert.Equal("Nguyen Van A", row.FullName);
        Assert.Equal("+84 901 234 567", row.Phone);
        Assert.NotNull(row.TermsAcceptedAt);
        Assert.False(row.IsLocked);
        // Framework password hasher: format v1 marker in the decoded payload, and the password verifies.
        var base64 = Convert.ToBase64String(row.PasswordHash);
        Assert.Equal(0x01, Convert.FromBase64String(base64)[0]);
        var hasher = new PasswordHasher<DataAccess.Scaffold.User>();
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(row, base64, "s3cret-pass"));
    }

    [Fact]
    public async Task Register_refuses_duplicate_live_email_case_insensitively()
    {
        await SeedRolesAsync();
        var service = CreateService();
        await service.RegisterAsync(new RegisterInput("First", "dup@example.com", null, "password-one", true));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().RegisterAsync(new RegisterInput("Second", "DUP@example.com", null, "password-two", true)));

        Assert.Equal(AuthException.DuplicateEmail, ex.Code);
    }

    [Fact]
    public async Task Register_validates_input_with_data_annotations()
    {
        await SeedRolesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().RegisterAsync(new RegisterInput("", "not-an-email", null, "short", true)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().RegisterAsync(new RegisterInput("Name", "ok@example.com", null, "long-enough", false)));
    }

    [Fact]
    public async Task SignIn_returns_the_user_and_stamps_last_login()
    {
        await SeedRolesAsync();
        await CreateService().RegisterAsync(new RegisterInput("Learner", "signin@example.com", null, "right-password", true));

        var user = await CreateService().SignInAsync(new SignInInput("SIGNIN@example.com", "right-password"));

        Assert.Equal(Roles.Learner, user.Role);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
        Assert.NotNull(row.LastLoginAt);
    }

    [Fact]
    public async Task SignIn_refuses_wrong_password_and_unknown_email_identically()
    {
        await SeedRolesAsync();
        await CreateService().RegisterAsync(new RegisterInput("Learner", "wrongpw@example.com", null, "right-password", true));

        var wrong = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().SignInAsync(new SignInInput("wrongpw@example.com", "wrong-password")));
        var unknown = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().SignInAsync(new SignInInput("ghost@example.com", "right-password")));

        Assert.Equal(AuthException.InvalidCredentials, wrong.Code);
        Assert.Equal(AuthException.InvalidCredentials, unknown.Code);
        Assert.Equal(wrong.Message, unknown.Message);
    }

    [Fact]
    public async Task SignIn_refuses_a_locked_account()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "lock@example.com", null, "password-ok", true));
        await database.SqlAsync($"UPDATE Users SET IsLocked = 1 WHERE UserId = {user.UserId}");

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().SignInAsync(new SignInInput("lock@example.com", "password-ok")));

        Assert.Equal(AuthException.Locked, ex.Code);
    }

    [Fact]
    public async Task GetUser_returns_the_live_role_not_a_snapshot()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "live@example.com", null, "password-ok", true));
        Assert.Equal(Roles.Learner, (await CreateService().GetUserAsync(user.UserId))!.Role);

        await database.SqlAsync($"""
            UPDATE Users SET RoleId = (SELECT RoleId FROM Roles WHERE Code = 'admin')
            WHERE UserId = {user.UserId};
            """);

        Assert.Equal(Roles.Admin, (await CreateService().GetUserAsync(user.UserId))!.Role);
    }

    [Fact]
    public async Task CheckSession_reports_locked_the_moment_the_account_is_locked()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "flip@example.com", null, "password-ok", true));

        Assert.Equal(SessionStatus.Active, await CreateService().CheckSessionAsync(user.UserId, user.SessionStamp));

        await database.SqlAsync($"UPDATE Users SET IsLocked = 1 WHERE UserId = {user.UserId}");

        Assert.Equal(SessionStatus.Locked, await CreateService().CheckSessionAsync(user.UserId, user.SessionStamp));
    }

    [Fact]
    public async Task CheckSession_refuses_a_missing_or_mismatched_stamp()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "stamp@example.com", null, "password-ok", true));

        Assert.Equal(SessionStatus.Ended, await CreateService().CheckSessionAsync(user.UserId, null));
        Assert.Equal(SessionStatus.Ended, await CreateService().CheckSessionAsync(user.UserId, "not-the-stamp"));
        Assert.Equal(SessionStatus.Ended, await CreateService().CheckSessionAsync(user.UserId + 10_000, user.SessionStamp));
    }

    [Fact]
    public async Task Profile_defaults_to_ho_chi_minh_and_updates_name_phone_zone()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "profile@example.com", null, "password-ok", true));

        var initial = await CreateService().GetProfileAsync(user.UserId);
        Assert.Equal("Asia/Ho_Chi_Minh", initial.TimeZoneId);

        var updated = await CreateService().UpdateProfileAsync(
            user.UserId, new UpdateProfileInput("  Nguyen Van B  ", " +84 912 000 000 ", "Asia/Tokyo"));

        Assert.Equal("Nguyen Van B", updated.FullName);
        Assert.Equal("+84 912 000 000", updated.Phone);
        Assert.Equal("Asia/Tokyo", updated.TimeZoneId);
        // The read-back agrees with the update result — the change is persisted, not just returned.
        Assert.Equal(updated, await CreateService().GetProfileAsync(user.UserId));
    }

    [Fact]
    public async Task UpdateProfile_refuses_a_zone_outside_the_shortlist()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "zone@example.com", null, "password-ok", true));

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().UpdateProfileAsync(user.UserId, new UpdateProfileInput("Name", null, "America/Sao_Paulo")));
    }

    [Fact]
    public async Task ChangePassword_requires_the_current_password()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "pw@example.com", null, "old-password", true));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().ChangePasswordAsync(user.UserId, new ChangePasswordInput("not-the-password", "new-password-1")));
        Assert.Equal(AuthException.InvalidCredentials, ex.Code);

        // The old password still works — a refused change must not half-apply.
        await CreateService().SignInAsync(new SignInInput("pw@example.com", "old-password"));
    }

    [Fact]
    public async Task ChangePassword_swaps_the_password()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "swap@example.com", null, "old-password", true));

        await CreateService().ChangePasswordAsync(user.UserId, new ChangePasswordInput("old-password", "new-password-1"));

        await Assert.ThrowsAsync<AuthException>(() =>
            CreateService().SignInAsync(new SignInInput("swap@example.com", "old-password")));
        await CreateService().SignInAsync(new SignInInput("swap@example.com", "new-password-1"));
    }

    [Fact]
    public async Task ChangePassword_validates_the_new_password()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "weak@example.com", null, "old-password", true));

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().ChangePasswordAsync(user.UserId, new ChangePasswordInput("old-password", "short")));
    }
}
