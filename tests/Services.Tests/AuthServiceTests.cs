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

    private AuthService CreateService(TimeProvider? clock = null)
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new AuthService(
            db, new RecordingEmailSender(), clock ?? TimeProvider.System, new AuthLinkOptions(), NullLogger<AuthService>.Instance);
    }

    private sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = initial;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public void Advance(TimeSpan delta) => UtcNow += delta;
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

    [Fact]
    public async Task ChangePassword_bumps_SecurityStamp_and_invalidates_old_session()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "revokestamp@example.com", null, "old-password", true));
        var initialStamp = user.SessionStamp;

        Assert.Equal(SessionStatus.Active, await CreateService().CheckSessionAsync(user.UserId, initialStamp));

        await CreateService().ChangePasswordAsync(user.UserId, new ChangePasswordInput("old-password", "new-password-123"));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
        Assert.NotEqual(initialStamp, row.SecurityStamp);

        // Old stamp is now ended
        Assert.Equal(SessionStatus.Ended, await CreateService().CheckSessionAsync(user.UserId, initialStamp));
        // New stamp is active
        Assert.Equal(SessionStatus.Active, await CreateService().CheckSessionAsync(user.UserId, row.SecurityStamp));
    }

    [Fact]
    public async Task SignIn_failed_attempts_increment_and_lock_out_after_five_failures()
    {
        await SeedRolesAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var user = await CreateService(clock).RegisterAsync(new RegisterInput("Learner", "bruteforce@example.com", null, "correct-password", true));

        // 4 failed attempts
        for (var i = 1; i <= 4; i++)
        {
            var ex = await Assert.ThrowsAsync<AuthException>(() =>
                CreateService(clock).SignInAsync(new SignInInput("bruteforce@example.com", "wrong-password")));
            Assert.Equal(AuthException.InvalidCredentials, ex.Code);

            await using var db = DbContextFactory.Create(database.ConnectionString);
            var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
            Assert.Equal(i, row.FailedAttempts);
            Assert.Null(row.LockoutEnd);
        }

        // 5th failed attempt locks out for 15 minutes
        var fifthEx = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService(clock).SignInAsync(new SignInInput("bruteforce@example.com", "wrong-password")));
        Assert.Equal(AuthException.InvalidCredentials, fifthEx.Code);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
            Assert.Equal(5, row.FailedAttempts);
            Assert.NotNull(row.LockoutEnd);
            Assert.Equal(clock.GetUtcNow().AddMinutes(15), row.LockoutEnd);
        }

        // Sign-in during lockout (even with correct password) throws AuthException.Locked
        var lockedEx = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService(clock).SignInAsync(new SignInInput("bruteforce@example.com", "correct-password")));
        Assert.Equal(AuthException.Locked, lockedEx.Code);
        Assert.Contains("temporarily locked", lockedEx.Message);

        // Advance clock past 15-minute lockout
        clock.Advance(TimeSpan.FromMinutes(16));

        // Successful sign-in clears FailedAttempts and LockoutEnd
        var signedIn = await CreateService(clock).SignInAsync(new SignInInput("bruteforce@example.com", "correct-password"));
        Assert.Equal(user.UserId, signedIn.UserId);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
            Assert.Equal(0, row.FailedAttempts);
            Assert.Null(row.LockoutEnd);
        }
    }

    [Fact]
    public async Task ChangePassword_failed_attempts_increment_and_lock_out_after_five_failures()
    {
        await SeedRolesAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var user = await CreateService(clock).RegisterAsync(new RegisterInput("Learner", "pwlock@example.com", null, "initial-password", true));

        for (var i = 1; i <= 4; i++)
        {
            var ex = await Assert.ThrowsAsync<AuthException>(() =>
                CreateService(clock).ChangePasswordAsync(user.UserId, new ChangePasswordInput("wrong-current-pw", "new-password-1")));
            Assert.Equal(AuthException.InvalidCredentials, ex.Code);

            await using var db = DbContextFactory.Create(database.ConnectionString);
            var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
            Assert.Equal(i, row.FailedAttempts);
            Assert.Null(row.LockoutEnd);
        }

        // 5th failed password change attempt
        var fifthEx = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService(clock).ChangePasswordAsync(user.UserId, new ChangePasswordInput("wrong-current-pw", "new-password-1")));
        Assert.Equal(AuthException.InvalidCredentials, fifthEx.Code);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
            Assert.Equal(5, row.FailedAttempts);
            Assert.NotNull(row.LockoutEnd);
            Assert.Equal(clock.GetUtcNow().AddMinutes(15), row.LockoutEnd);
        }

        // Change password during lockout throws AuthException.Locked
        var lockedEx = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService(clock).ChangePasswordAsync(user.UserId, new ChangePasswordInput("initial-password", "new-password-1")));
        Assert.Equal(AuthException.Locked, lockedEx.Code);
        Assert.Contains("temporarily locked", lockedEx.Message);

        // Sign-in during this lockout also fails with AuthException.Locked because counter is shared
        var signinLockedEx = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService(clock).SignInAsync(new SignInInput("pwlock@example.com", "initial-password")));
        Assert.Equal(AuthException.Locked, signinLockedEx.Code);

        // Advance clock past 15-minute lockout
        clock.Advance(TimeSpan.FromMinutes(16));

        // Successful password change resets FailedAttempts and LockoutEnd
        await CreateService(clock).ChangePasswordAsync(user.UserId, new ChangePasswordInput("initial-password", "brand-new-password"));

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
            Assert.Equal(0, row.FailedAttempts);
            Assert.Null(row.LockoutEnd);
        }
    }

    [Fact]
    public async Task Expired_lockout_resets_failure_counter_on_subsequent_failed_attempt()
    {
        await SeedRolesAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var user = await CreateService(clock).RegisterAsync(new RegisterInput("Learner", "expiredlock@example.com", null, "correct-password", true));

        // Fail 5 times to trigger lockout
        for (var i = 1; i <= 5; i++)
        {
            await Assert.ThrowsAsync<AuthException>(() =>
                CreateService(clock).SignInAsync(new SignInInput("expiredlock@example.com", "wrong-password")));
        }

        // Verify locked out
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
            Assert.Equal(5, row.FailedAttempts);
            Assert.NotNull(row.LockoutEnd);
        }

        // Advance past lockout window
        clock.Advance(TimeSpan.FromMinutes(16));

        // Subsequent failure should NOT throw AuthException.Locked; it should throw InvalidCredentials and reset counter to 1
        var failEx = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService(clock).SignInAsync(new SignInInput("expiredlock@example.com", "still-wrong-password")));
        Assert.Equal(AuthException.InvalidCredentials, failEx.Code);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var row = await db.Users.SingleAsync(u => u.UserId == user.UserId);
            Assert.Equal(1, row.FailedAttempts);
            Assert.Null(row.LockoutEnd);
        }
    }

    [Fact]
    public async Task Failed_attempt_counter_is_shared_between_signin_and_changepassword()
    {
        await SeedRolesAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var user = await CreateService(clock).RegisterAsync(new RegisterInput("Learner", "sharedcounter@example.com", null, "my-pass-1234", true));

        // 3 failed sign-ins
        for (var i = 1; i <= 3; i++)
        {
            await Assert.ThrowsAsync<AuthException>(() =>
                CreateService(clock).SignInAsync(new SignInInput("sharedcounter@example.com", "wrong-pass")));
        }

        // 2 failed change password attempts -> total 5 failures
        for (var i = 4; i <= 5; i++)
        {
            await Assert.ThrowsAsync<AuthException>(() =>
                CreateService(clock).ChangePasswordAsync(user.UserId, new ChangePasswordInput("wrong-current", "new-pass-5678")));
        }

        // Now both sign-in and password change must be locked out
        var signinEx = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService(clock).SignInAsync(new SignInInput("sharedcounter@example.com", "my-pass-1234")));
        Assert.Equal(AuthException.Locked, signinEx.Code);

        var changePwEx = await Assert.ThrowsAsync<AuthException>(() =>
            CreateService(clock).ChangePasswordAsync(user.UserId, new ChangePasswordInput("my-pass-1234", "new-pass-5678")));
        Assert.Equal(AuthException.Locked, changePwEx.Code);
    }

    [Fact]
    public async Task CheckSession_with_invalid_or_oversized_stamp_returns_ended()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "stamps@example.com", null, "secure-pass-123", true));

        // Null stamp
        Assert.Equal(SessionStatus.Ended, await CreateService().CheckSessionAsync(user.UserId, null));

        // Oversized stamp (> 128 characters)
        var oversized = new string('x', 200);
        Assert.Equal(SessionStatus.Ended, await CreateService().CheckSessionAsync(user.UserId, oversized));

        // Wrong stamp
        Assert.Equal(SessionStatus.Ended, await CreateService().CheckSessionAsync(user.UserId, Guid.NewGuid().ToString()));

        // Correct stamp
        Assert.Equal(SessionStatus.Active, await CreateService().CheckSessionAsync(user.UserId, user.SessionStamp));
    }

    [Fact]
    public async Task SignIn_with_invalid_model_throws_ValidationException()
    {
        await SeedRolesAsync();
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().SignInAsync(new SignInInput("not-an-email", "pass")));
    }
}
