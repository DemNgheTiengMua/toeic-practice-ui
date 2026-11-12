using BusinessObjects;
using Contracts;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #281: <b>a credit refund reaches the learner without waiting for accounting.</b>
/// <para>
/// The assertions are all external: the status a staff member reads, the ledger row the learner's
/// balance is made of, and the error code the screen shows. Nothing here names a private method, so a
/// refactor inside the service cannot break a test that the behaviour never changed.
/// </para>
/// <para>
/// The ceiling tests are the reason this file exists rather than a schema test. The schema already
/// refuses a credit refund that also carries money (<c>CK_Refunds_MethodOwnsAmount</c>); it cannot see
/// how many credits the linked order actually granted, because a CHECK cannot read another table. So
/// the ceiling is the service's fact, and only a service test can prove it.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RefundServiceTests(DatabaseFixture database)
{
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private RefundService CreateService(RefundOptions? options = null) => new(
        DbContextFactory.Create(database.ConnectionString),
        _clock,
        options ?? new RefundOptions());

    private async Task<int> SeedStaffAsync(string role = "admin") => await SeedUserAsync(role, "Staff Member");

    private async Task<int> SeedLearnerAsync(string name = "Refund Learner") =>
        await SeedUserAsync("learner", name);

    private async Task<int> SeedUserAsync(string role, string name)
    {
        var email = $"{role}-{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = '{role}')
              INSERT INTO Roles (Code, Name) VALUES ('{role}', N'{role}');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = '{role}'), '{email}', 0x01, N'{name}', SYSDATETIMEOFFSET());
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>A paid order whose package granted <paramref name="credits"/> credits.</summary>
    private async Task<int> SeedPaidOrderAsync(int userId, int credits)
    {
        var orderCode = $"DH-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        var packageName = $"refund-pkg-{Guid.NewGuid():N}"[..20];
        var paymentNo = Random.Shared.NextInt64(1000000, 999999999);
        await database.SqlAsync($"""
            INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
            VALUES (N'{packageName}', {credits}, 199000, 1, 0, N'Features', 1);
            DECLARE @pkgId INT = (SELECT TOP 1 PackageId FROM Packages WHERE Name = N'{packageName}');

            INSERT INTO Orders (OrderCode, PaymentNo, UserId, PackageId, Amount, Status, Gateway, CreatedAt, PaidAt)
            VALUES ('{orderCode}', {paymentNo}, {userId}, @pkgId, 199000, 'paid', 'mock',
                    DATEADD(day, -1, SYSDATETIMEOFFSET()), DATEADD(day, -1, SYSDATETIMEOFFSET()));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Orders.Where(o => o.OrderCode == orderCode).Select(o => o.OrderId).SingleAsync();
    }

    private async Task<int> BalanceAsync(int userId)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.vw_UserCreditBalances
            .Where(b => b.UserId == userId)
            .Select(b => b.Balance)
            .SingleOrDefaultAsync() ?? 0;
    }
    [Fact]
    public async Task A_staff_member_can_request_a_credit_refund_and_sees_it_as_requested()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var requested = await service.RequestCreditRefundAsync(
            staff,
            learner,
            creditCount: 5,
            reason: "Mentor did not attend");

        Assert.Equal(RefundStatusDto.Requested, requested.Status);
        Assert.Equal(RefundMethodDto.Credit, requested.Method);
        Assert.Equal(5, requested.CreditCount);
        Assert.Equal(staff, requested.RequestedByUserId);
        Assert.Null(requested.DecidedByUserId);
        Assert.Null(requested.DecidedAt);

        // The list screen is the evidence a staff member can actually find it again. Scoped to this
        // test's own learner: the suite shares one database, so an unfiltered count would depend on
        // which tests ran first.
        var list = await service.GetAdminRefundsAsync(q: "refund learner");
        var row = Assert.Single(list.Refunds, r => r.RefundId == requested.RefundId);
        Assert.Equal(RefundStatusDto.Requested, row.Status);
        Assert.Equal("Refund Learner", row.LearnerName);
        Assert.True(list.RequestedCount >= 1);
    }

    [Fact]
    public async Task Approving_a_credit_refund_posts_a_refund_credit_row_and_moves_the_balance()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var requested = await service.RequestCreditRefundAsync(
            staff,
            learner, creditCount: 7, reason: "Double charge on grading");

        Assert.Equal(0, await BalanceAsync(learner));

        var approved = await service.ApproveRefundAsync(staff, requested.RefundId);

        Assert.Equal(RefundStatusDto.Approved, approved.Status);
        Assert.Equal(staff, approved.DecidedByUserId);
        Assert.NotNull(approved.DecidedAt);
        Assert.NotNull(approved.CreditTxnId);

        // The learner-visible consequence: the balance moved inside the same request.
        Assert.Equal(7, await BalanceAsync(learner));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var txn = await db.CreditTransactions.SingleAsync(t => t.TxnId == approved.CreditTxnId!.Value);
        Assert.Equal("refund_credit", txn.Reason);
        Assert.Equal(7, txn.Delta);
        Assert.Equal(learner, txn.UserId);
        Assert.Equal(staff, txn.AdminUserId);
        Assert.NotNull(txn.Note);

        // And the ledger's own reason is what the learner reads, not the code.
        Assert.Equal(
            DisplayLabels.LabelFor(CreditReasonDto.RefundCredit, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance),
            "Refund credits");
    }

    [Fact]
    public async Task Rejecting_requires_a_reason_and_posts_nothing()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var requested = await service.RequestCreditRefundAsync(
            staff,
            learner, creditCount: 3, reason: "Suspected double charge");

        var noReason = await Assert.ThrowsAsync<RefundException>(() =>
            service.RejectRefundAsync(staff, requested.RefundId, "   "));
        Assert.Equal(ApiError.Codes.RefundReasonRequired, noReason.Code);

        var rejected = await service.RejectRefundAsync(staff, requested.RefundId, "The order was never paid.");

        Assert.Equal(RefundStatusDto.Rejected, rejected.Status);
        Assert.Equal(staff, rejected.DecidedByUserId);
        Assert.NotNull(rejected.DecidedAt);

        // Nothing was paid: no ledger row, so the balance is untouched.
        Assert.Equal(0, await BalanceAsync(learner));
        Assert.Null(rejected.CreditTxnId);
    }

    [Fact]
    public async Task The_order_ceiling_refuses_more_credits_than_the_order_granted()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var orderId = await SeedPaidOrderAsync(learner, credits: 5);
        var service = CreateService();

        var first = await service.RequestCreditRefundAsync(
            staff,
            learner, creditCount: 3, reason: "First partial refund", orderId: orderId);
        Assert.Equal(RefundStatusDto.Requested, first.Status);

        // 3 + 3 = 6 against an order that granted 5, so the second request is refused.
        var over = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestCreditRefundAsync(
                staff,
                learner, creditCount: 3, reason: "Second partial refund", orderId: orderId));

        Assert.Equal(ApiError.Codes.RefundExceedsOrderCeiling, over.Code);
        Assert.NotEqual(ApiError.Codes.RefundExceedsCeiling, over.Code);

        // A request that fits the remaining 2 credits is still accepted.
        var fits = await service.RequestCreditRefundAsync(
            staff,
            learner, creditCount: 2, reason: "Remaining balance", orderId: orderId);
        Assert.Equal(RefundStatusDto.Requested, fits.Status);
    }

    /// <summary>
    /// The ceiling is cumulative, not per request: 3 + 3 against an order that granted 5 is refused at
    /// the second request, because a <c>requested</c> refund is a promise the order's credits are
    /// spoken for. A rejected one returns nothing and releases its claim.
    /// </summary>
    [Fact]
    public async Task The_order_ceiling_is_cumulative_and_a_rejected_request_releases_its_claim()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var orderId = await SeedPaidOrderAsync(learner, credits: 5);
        var service = CreateService();

        var first = await service.RequestCreditRefundAsync(
            staff, learner, creditCount: 3, reason: "First", orderId: orderId);
        var second = await service.RequestCreditRefundAsync(
            staff, learner, creditCount: 2, reason: "Second", orderId: orderId);

        // 3 + 2 = 5 uses the whole order up; one more credit cannot be promised.
        var refusal = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestCreditRefundAsync(
                staff, learner, creditCount: 1, reason: "One more", orderId: orderId));
        Assert.Equal(ApiError.Codes.RefundExceedsOrderCeiling, refusal.Code);

        // Rejecting the second returns its 2 credits to the ceiling.
        await service.RejectRefundAsync(staff, second.RefundId, "Duplicate of the first request.");

        var third = await service.RequestCreditRefundAsync(
            staff, learner, creditCount: 2, reason: "Replacement", orderId: orderId);
        Assert.Equal(RefundStatusDto.Requested, third.Status);

        // Only the approved one ever moved a balance.
        await service.ApproveRefundAsync(staff, first.RefundId);
        Assert.Equal(3, await BalanceAsync(learner));
    }

    [Fact]
    public async Task The_orderless_ceiling_refuses_a_request_above_the_configured_limit()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService(new RefundOptions { OrderlessCeiling = 200 });

        var atCeiling = await service.RequestCreditRefundAsync(
            staff,
            learner, creditCount: 200, reason: "Service outage compensation");
        Assert.Equal(200, atCeiling.CreditCount);

        var over = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestCreditRefundAsync(
                staff,
                learner, creditCount: 201, reason: "One too many"));

        Assert.Equal(ApiError.Codes.RefundExceedsCeiling, over.Code);
        Assert.NotEqual(ApiError.Codes.RefundExceedsOrderCeiling, over.Code);
    }

    [Fact]
    public async Task A_decided_refund_cannot_be_decided_again()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var approved = await service.RequestCreditRefundAsync(
            staff, learner, creditCount: 4, reason: "Approved case");
        await service.ApproveRefundAsync(staff, approved.RefundId);

        var approveTwice = await Assert.ThrowsAsync<RefundException>(() =>
            service.ApproveRefundAsync(staff, approved.RefundId));
        Assert.Equal(ApiError.Codes.RefundAlreadyDecided, approveTwice.Code);

        var rejectAfter = await Assert.ThrowsAsync<RefundException>(() =>
            service.RejectRefundAsync(staff, approved.RefundId, "Changed my mind"));
        Assert.Equal(ApiError.Codes.RefundAlreadyDecided, rejectAfter.Code);

        // The first decision stands, and the credits are posted exactly once.
        Assert.Equal(4, await BalanceAsync(learner));
    }

    [Fact]
    public async Task A_money_refund_cannot_be_created_through_this_screen()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var refusal = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestRefundAsync(
                staff,
                new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 5, Reason: "Bank transfer")));

        Assert.Equal(ApiError.Codes.RefundMoneyNotSupported, refusal.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.False(await db.Refunds.AnyAsync(r => r.UserId == learner));
    }

    [Fact]
    public async Task A_reason_is_mandatory_and_credits_must_be_positive()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var noReason = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestCreditRefundAsync(staff, learner, creditCount: 5, reason: "  "));
        Assert.Equal(ApiError.Codes.RefundReasonRequired, noReason.Code);

        var noCredits = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestCreditRefundAsync(staff, learner, creditCount: 0, reason: "Zero credits"));
        Assert.Equal(ApiError.Codes.RefundCreditCountInvalid, noCredits.Code);
    }

    [Fact]
    public async Task The_detail_screen_shows_who_requested_and_who_decided()
    {
        var requester = await SeedStaffAsync();
        var decider = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var requested = await service.RequestCreditRefundAsync(
            requester, learner, creditCount: 2, reason: "Grading failure");
        await service.ApproveRefundAsync(decider, requested.RefundId);

        var detail = await service.GetAdminRefundDetailAsync(requested.RefundId);

        Assert.NotNull(detail);
        Assert.Equal(requester, detail!.RequestedByUserId);
        Assert.Equal(decider, detail.DecidedByUserId);
        Assert.NotNull(detail.DecidedAt);
        Assert.Equal(2, detail.GrantedCredits);
    }
}
