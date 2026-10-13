using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
        return new AuthService(db);
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
        Assert.Equal("learner", user.RoleCode);

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

        Assert.Equal("learner", user.RoleCode);
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
        Assert.Equal("learner", (await CreateService().GetUserAsync(user.UserId))!.RoleCode);

        await database.SqlAsync($"""
            UPDATE Users SET RoleId = (SELECT RoleId FROM Roles WHERE Code = 'admin')
            WHERE UserId = {user.UserId};
            """);

        Assert.Equal("admin", (await CreateService().GetUserAsync(user.UserId))!.RoleCode);
    }

    [Fact]
    public async Task IsActive_flips_to_false_the_moment_the_account_is_locked()
    {
        await SeedRolesAsync();
        var user = await CreateService().RegisterAsync(new RegisterInput("Learner", "flip@example.com", null, "password-ok", true));

        Assert.True(await CreateService().IsActiveAsync(user.UserId));

        await database.SqlAsync($"UPDATE Users SET IsLocked = 1 WHERE UserId = {user.UserId}");

        Assert.False(await CreateService().IsActiveAsync(user.UserId));
    }
}
