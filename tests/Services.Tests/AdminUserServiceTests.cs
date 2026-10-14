using BusinessObjects;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AdminUserServiceTests(DatabaseFixture database)
{
    private readonly RecordingEmailSender _mail = new();
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private AuthService CreateAuthService() => new(
        DbContextFactory.Create(database.ConnectionString),
        _mail,
        _clock,
        new AuthLinkOptions { PublicBaseUrl = "http://localhost:5000" },
        NullLogger<AuthService>.Instance);

    private AdminUserService CreateUserService(IAuthService? authService = null) => new(
        DbContextFactory.Create(database.ConnectionString),
        authService ?? CreateAuthService(),
        _clock);

    private async Task<int> SeedAdminAsync()
    {
        var email = $"admin-{Guid.NewGuid():N}@toeic.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{email}', 0x01, N'System Admin', SYSDATETIMEOFFSET());
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<AuthUser> RegisterLearnerAsync(string email, string password = "Password@123")
    {
        await database.SqlAsync("""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            """);
        return await CreateAuthService().RegisterAsync(new RegisterInput("Test Learner", email, null, password, true));
    }

    [Fact]
    public async Task Credit_adjustments_require_a_reason_and_write_a_ledger_row()
    {
        var adminId = await SeedAdminAsync();
        var learner = await RegisterLearnerAsync($"adj-{Guid.NewGuid():N}@test.local");
        var service = CreateUserService();

        // 1. Rejects missing / empty / whitespace reason
        var exEmpty = await Assert.ThrowsAsync<ApiException>(() =>
            service.AdjustCreditsAsync(adminId, learner.UserId, delta: 5, reason: ""));
        Assert.Equal(ApiError.Codes.Validation, exEmpty.Code);

        var exWhitespace = await Assert.ThrowsAsync<ApiException>(() =>
            service.AdjustCreditsAsync(adminId, learner.UserId, delta: 5, reason: "   "));
        Assert.Equal(ApiError.Codes.Validation, exWhitespace.Code);

        // 2. Rejects zero delta
        var exZero = await Assert.ThrowsAsync<ApiException>(() =>
            service.AdjustCreditsAsync(adminId, learner.UserId, delta: 0, reason: "Zero delta"));
        Assert.Equal(ApiError.Codes.Validation, exZero.Code);

        // 3. Positive adjustment (grant) writes ledger row with reason and admin ID
        var grantReason = "Compensation for Part 3 audio disruption";
        var grantResult = await service.AdjustCreditsAsync(adminId, learner.UserId, delta: 8, reason: grantReason);

        Assert.Equal(8, grantResult.NewBalance);
        Assert.Equal(grantReason, grantResult.Reason);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var grantTxn = await db.CreditTransactions.SingleOrDefaultAsync(t => t.TxnId == grantResult.TxnId);
            Assert.NotNull(grantTxn);
            Assert.Equal(learner.UserId, grantTxn.UserId);
            Assert.Equal("admin_grant", grantTxn.Reason);
            Assert.Equal(8, grantTxn.Delta);
            Assert.Equal(adminId, grantTxn.AdminUserId);
            Assert.Equal(grantReason, grantTxn.Note);
        }

        // 4. Negative adjustment (revoke) cannot exceed current balance
        var exOverDeduct = await Assert.ThrowsAsync<ApiException>(() =>
            service.AdjustCreditsAsync(adminId, learner.UserId, delta: -10, reason: "Over deduction"));
        Assert.Equal(ApiError.Codes.InsufficientCredits, exOverDeduct.Code);

        // 5. Valid negative adjustment writes ledger row with admin_revoke
        var revokeReason = "Administrative clawback of promo";
        var revokeResult = await service.AdjustCreditsAsync(adminId, learner.UserId, delta: -3, reason: revokeReason);

        Assert.Equal(5, revokeResult.NewBalance); // 8 - 3 = 5

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var revokeTxn = await db.CreditTransactions.SingleOrDefaultAsync(t => t.TxnId == revokeResult.TxnId);
            Assert.NotNull(revokeTxn);
            Assert.Equal(learner.UserId, revokeTxn.UserId);
            Assert.Equal("admin_revoke", revokeTxn.Reason);
            Assert.Equal(-3, revokeTxn.Delta);
            Assert.Equal(adminId, revokeTxn.AdminUserId);
            Assert.Equal(revokeReason, revokeTxn.Note);
        }
    }

    [Fact]
    public async Task Lock_takes_effect_on_the_learners_next_request()
    {
        var adminId = await SeedAdminAsync();
        var email = $"lock-{Guid.NewGuid():N}@test.local";
        var password = "PassWord@123";
        var learner = await RegisterLearnerAsync(email, password);

        var authService = CreateAuthService();
        var userService = CreateUserService(authService);

        // Learner signs in and receives an active session
        var session = await authService.SignInAsync(new SignInInput(email, password));
        var statusBefore = await authService.CheckSessionAsync(session.UserId, session.SessionStamp);
        Assert.Equal(SessionStatus.Active, statusBefore);

        // Admin locks the learner account
        var lockResult = await userService.SetLockAsync(adminId, session.UserId, isLocked: true);
        Assert.True(lockResult.IsLocked);

        // The learner's NEXT request immediately checks the session and sees Locked
        var statusAfterLock = await authService.CheckSessionAsync(session.UserId, session.SessionStamp);
        Assert.Equal(SessionStatus.Locked, statusAfterLock);

        // Learner cannot sign in while locked
        var freshAuthService = CreateAuthService();
        var exSignIn = await Assert.ThrowsAsync<AuthException>(() =>
            freshAuthService.SignInAsync(new SignInInput(email, password)));
        Assert.Equal(AuthException.Locked, exSignIn.Code);

        // Admin unlocks the learner account
        var unlockResult = await userService.SetLockAsync(adminId, session.UserId, isLocked: false);
        Assert.False(unlockResult.IsLocked);

        // On the next request, the learner's session is active again
        var statusAfterUnlock = await CreateAuthService().CheckSessionAsync(session.UserId, session.SessionStamp);
        Assert.Equal(SessionStatus.Active, statusAfterUnlock);
    }

    [Fact]
    public async Task Resend_activation_uses_IEmailSender()
    {
        var email = $"resend-{Guid.NewGuid():N}@test.local";
        var learner = await RegisterLearnerAsync(email);

        // First registration sent 1 email
        Assert.Single(_mail.To(email));

        var authService = CreateAuthService();
        var userService = CreateUserService(authService);

        // Admin resends activation email
        await userService.ResendActivationAsync(learner.UserId);

        // IEmailSender should have delivered a second email with an activation link
        var sentMessages = _mail.To(email).ToList();
        Assert.Equal(2, sentMessages.Count);
        Assert.Contains("/auth/verify-email?token=", sentMessages[1].Body);

        // Verifying the account
        var token = _mail.LastToken(email, "/auth/verify-email");
        await authService.VerifyEmailAsync(token);

        // Resending to an already-verified learner throws validation error
        var exVerified = await Assert.ThrowsAsync<ApiException>(() =>
            userService.ResendActivationAsync(learner.UserId));
        Assert.Equal(ApiError.Codes.Validation, exVerified.Code);
    }

    [Fact]
    public async Task GetUsersAsync_returns_students_with_status_filtering_and_aggregates()
    {
        var adminId = await SeedAdminAsync();
        var activeEmail = $"active-{Guid.NewGuid():N}@test.local";
        var activeLearner = await RegisterLearnerAsync(activeEmail);
        var token = _mail.LastToken(activeEmail, "/auth/verify-email");
        await CreateAuthService().VerifyEmailAsync(token);

        var lockedEmail = $"locked-{Guid.NewGuid():N}@test.local";
        var lockedLearner = await RegisterLearnerAsync(lockedEmail);
        var userService = CreateUserService();
        await userService.SetLockAsync(adminId, lockedLearner.UserId, isLocked: true);

        var unverifiedEmail = $"unverified-{Guid.NewGuid():N}@test.local";
        var unverifiedLearner = await RegisterLearnerAsync(unverifiedEmail);

        // Filter active
        var activeResult = await userService.GetUsersAsync(status: "active");
        Assert.Contains(activeResult.Users, u => u.UserId == activeLearner.UserId);
        Assert.DoesNotContain(activeResult.Users, u => u.UserId == lockedLearner.UserId);
        Assert.DoesNotContain(activeResult.Users, u => u.UserId == unverifiedLearner.UserId);

        // Filter locked
        var lockedResult = await userService.GetUsersAsync(status: "locked");
        Assert.Contains(lockedResult.Users, u => u.UserId == lockedLearner.UserId);

        // Filter not_activated
        var unverifiedResult = await userService.GetUsersAsync(status: "not_activated");
        Assert.Contains(unverifiedResult.Users, u => u.UserId == unverifiedLearner.UserId);

        // Check summary counts
        Assert.True(activeResult.ActiveCount >= 1);
        Assert.True(activeResult.LockedCount >= 1);
        Assert.True(activeResult.NotActivatedCount >= 1);
    }
}
