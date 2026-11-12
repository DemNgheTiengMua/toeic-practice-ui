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

    /// <summary>A paid order whose package granted <paramref name="credits"/> credits, for
    /// <paramref name="amount"/> dong. The two ceilings read different columns: a credit refund is
    /// capped by what the order granted, a money refund by what it was paid.</summary>
    private async Task<int> SeedPaidOrderAsync(int userId, int credits, decimal amount = 199000m)
    {
        var orderCode = $"DH-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        var packageName = $"refund-pkg-{Guid.NewGuid():N}"[..20];
        var paymentNo = Random.Shared.NextInt64(1000000, 999999999);
        await database.SqlAsync($"""
            INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
            VALUES (N'{packageName}', {credits}, 199000, 1, 0, N'Features', 1);
            DECLARE @pkgId INT = (SELECT TOP 1 PackageId FROM Packages WHERE Name = N'{packageName}');

            INSERT INTO Orders (OrderCode, PaymentNo, UserId, PackageId, Amount, Status, Gateway, CreatedAt, PaidAt)
            VALUES ('{orderCode}', {paymentNo}, {userId}, @pkgId, {amount}, 'paid', 'mock',
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

    // ---- #282: the money half -------------------------------------------------------------

    [Fact]
    public async Task A_staff_member_can_request_a_money_refund_against_an_order()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var orderId = await SeedPaidOrderAsync(learner, credits: 5, amount: 199000m);
        var service = CreateService();

        var requested = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "Duplicate bank transfer", OrderId: orderId, Amount: 100000m));

        Assert.Equal(RefundStatusDto.Requested, requested.Status);
        Assert.Equal(RefundMethodDto.Money, requested.Method);
        Assert.Equal(100000m, requested.Amount);

        // The method owns the value column: a money refund carries no credit count at all.
        Assert.Equal(0, requested.CreditCount);
        Assert.Null(requested.CreditTxnId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.Refunds.SingleAsync(r => r.RefundId == requested.RefundId);
        Assert.Null(stored.CreditCount);
        Assert.Equal(100000m, stored.Amount);
    }

    [Fact]
    public async Task A_staff_member_can_request_a_money_refund_with_no_order_behind_it()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService(new RefundOptions { OrderlessCeiling = 200 });

        var requested = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "Mentor no-show, paid outside the product", Amount: 200m));

        Assert.Equal(RefundMethodDto.Money, requested.Method);
        Assert.Null(requested.OrderId);
        Assert.Null(requested.OrderCode);
        Assert.Equal(200m, requested.Amount);
    }

    /// <summary>
    /// The ceiling counts what the order was <b>paid</b>, not what it granted: a money refund returns
    /// money, so 199,000 dong is the whole ceiling however many credits the package carried.
    /// </summary>
    [Fact]
    public async Task The_order_ceiling_refuses_more_money_than_the_order_was_paid()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var orderId = await SeedPaidOrderAsync(learner, credits: 50, amount: 199000m);
        var service = CreateService();

        await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "First partial", OrderId: orderId, Amount: 150000m));

        // 150,000 + 60,000 is over the 199,000 the order was paid.
        var over = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestRefundAsync(
                staff,
                new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                    Reason: "Second partial", OrderId: orderId, Amount: 60000m)));

        Assert.Equal(ApiError.Codes.RefundExceedsOrderCeiling, over.Code);

        // The remaining 49,000 is still payable.
        var fits = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "Remaining", OrderId: orderId, Amount: 49000m));
        Assert.Equal(RefundStatusDto.Requested, fits.Status);
    }

    /// <summary>
    /// The two methods count against their own order ceiling and never against each other's: a credit
    /// refund of 5 against a 199,000-dong order does not spend a dong of the money ceiling, and the
    /// reverse is equally true. That is why the counting shape is shared but the column is the
    /// method's own - one definition of "how much of this order is already refunded" per unit.
    /// </summary>
    [Fact]
    public async Task The_credit_ceiling_and_the_money_ceiling_do_not_consume_each_other()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var orderId = await SeedPaidOrderAsync(learner, credits: 5, amount: 199000m);
        var service = CreateService();

        await service.RequestCreditRefundAsync(staff, learner, creditCount: 5, reason: "Credits back", orderId: orderId);

        // The credits are all spoken for, but not one dong of the paid amount is.
        var money = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "And the money", OrderId: orderId, Amount: 199000m));

        Assert.Equal(RefundStatusDto.Requested, money.Status);
    }

    [Fact]
    public async Task The_orderless_ceiling_refuses_a_money_refund_above_the_configured_limit()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService(new RefundOptions { OrderlessCeiling = 200 });

        var over = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestRefundAsync(
                staff,
                new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                    Reason: "No order, too much", Amount: 201m)));

        Assert.Equal(ApiError.Codes.RefundExceedsCeiling, over.Code);
        Assert.NotEqual(ApiError.Codes.RefundExceedsOrderCeiling, over.Code);
    }

    [Fact]
    public async Task Approving_a_money_refund_changes_no_credit_balance()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var orderId = await SeedPaidOrderAsync(learner, credits: 5, amount: 199000m);
        var service = CreateService();

        var requested = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "Paid by bank transfer", OrderId: orderId, Amount: 50000m));

        Assert.Equal(0, await BalanceAsync(learner));

        var approved = await service.ApproveRefundAsync(staff, requested.RefundId);

        Assert.Equal(RefundStatusDto.Approved, approved.Status);
        Assert.Equal(staff, approved.DecidedByUserId);
        Assert.NotNull(approved.DecidedAt);

        // Money is paid outside the product, so approval moves nothing: no ledger row, no balance.
        Assert.Null(approved.CreditTxnId);
        Assert.Equal(0, await BalanceAsync(learner));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.False(await db.CreditTransactions.AnyAsync(t => t.UserId == learner));
    }

    [Fact]
    public async Task Settling_an_approved_money_refund_records_the_accountant_and_the_time()
    {
        var staff = await SeedStaffAsync();
        var accountant = await SeedStaffAsync("admin");
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var requested = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "Refund promised in the complaint reply", Amount: 100m));
        await service.ApproveRefundAsync(staff, requested.RefundId);

        var settledAt = _clock.GetUtcNow();
        var settled = await service.SettleRefundAsync(accountant, requested.RefundId);

        Assert.Equal(RefundStatusDto.Settled, settled.Status);
        Assert.Equal(accountant, settled.SettledByUserId);
        Assert.Equal("Staff Member", settled.SettledByUserName);
        Assert.Equal(settledAt, settled.SettledAt);

        // Settlement is a stamp, not a movement: the balance is still untouched.
        Assert.Equal(0, await BalanceAsync(learner));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.Refunds.SingleAsync(r => r.RefundId == requested.RefundId);
        Assert.Equal("settled", stored.Status);
        Assert.Equal(accountant, stored.SettledByUserId);
        Assert.NotNull(stored.SettledAt);
    }

    [Fact]
    public async Task A_credit_refund_cannot_be_settled()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var requested = await service.RequestCreditRefundAsync(
            staff, learner, creditCount: 4, reason: "Credits, not money");
        await service.ApproveRefundAsync(staff, requested.RefundId);

        var refusal = await Assert.ThrowsAsync<RefundException>(() =>
            service.SettleRefundAsync(staff, requested.RefundId));

        // A readable code, not the CHECK constraint's 500.
        Assert.Equal(ApiError.Codes.RefundCreditCannotSettle, refusal.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.Refunds.SingleAsync(r => r.RefundId == requested.RefundId);
        Assert.Equal("approved", stored.Status);
        Assert.Null(stored.SettledAt);
    }

    [Fact]
    public async Task A_money_refund_cannot_be_settled_before_it_is_approved()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var requested = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "Not decided yet", Amount: 50m));

        var refusal = await Assert.ThrowsAsync<RefundException>(() =>
            service.SettleRefundAsync(staff, requested.RefundId));

        Assert.Equal(ApiError.Codes.RefundNotApproved, refusal.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.Refunds.SingleAsync(r => r.RefundId == requested.RefundId);
        Assert.Equal("requested", stored.Status);
        Assert.Null(stored.SettledAt);
    }

    [Fact]
    public async Task A_money_refund_must_carry_a_positive_amount()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var service = CreateService();

        var noAmount = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestRefundAsync(
                staff,
                new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                    Reason: "Nothing at all", Amount: null)));
        Assert.Equal(ApiError.Codes.RefundAmountInvalid, noAmount.Code);

        var zero = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestRefundAsync(
                staff,
                new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                    Reason: "Zero dong", Amount: 0m)));
        Assert.Equal(ApiError.Codes.RefundAmountInvalid, zero.Code);

        // A third decimal place is refused, not silently rounded: the stored amount must be the amount
        // the requester stated.
        var tooPrecise = await Assert.ThrowsAsync<RefundException>(() =>
            service.RequestRefundAsync(
                staff,
                new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                    Reason: "Sub-dong amount", Amount: 1234.567m)));
        Assert.Equal(ApiError.Codes.RefundAmountInvalid, tooPrecise.Code);
    }

    [Fact]
    public async Task The_list_carries_the_money_row_a_screen_needs()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync();
        var orderId = await SeedPaidOrderAsync(learner, credits: 5, amount: 199000m);
        var service = CreateService();

        var requested = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "Accounting queue", OrderId: orderId, Amount: 75000m));

        var list = await service.GetAdminRefundsAsync(q: "refund learner");
        var row = Assert.Single(list.Refunds, r => r.RefundId == requested.RefundId);

        Assert.Equal(RefundMethodDto.Money, row.Method);
        Assert.Equal(75000m, row.Amount);
        Assert.Equal("Refund Learner", row.LearnerName);
        Assert.StartsWith("learner-", row.LearnerEmail);
        Assert.NotNull(row.OrderCode);
        Assert.Null(row.SettledAt);
    }

    /// <summary>
    /// The list filters by status, and <c>settled</c> is a status a user now reaches (#282). Scoped to
    /// this test's own learner so the shared database's other rows can neither satisfy nor spoil it.
    /// </summary>
    [Fact]
    public async Task The_list_filters_by_status_including_settled()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync("Settled Filter Learner");
        var service = CreateService();

        var requested = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "To be settled", Amount: 100m));
        await service.ApproveRefundAsync(staff, requested.RefundId);
        await service.SettleRefundAsync(staff, requested.RefundId);

        var settled = await service.GetAdminRefundsAsync(q: "settled filter learner", status: "settled");
        Assert.Single(settled.Refunds, r => r.RefundId == requested.RefundId);

        var approved = await service.GetAdminRefundsAsync(q: "settled filter learner", status: "approved");
        Assert.DoesNotContain(approved.Refunds, r => r.RefundId == requested.RefundId);
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

    /// <summary>
    /// #283: <b>the export selects on settlement date and changes nothing.</b> The refund is requested
    /// well before the range and settled inside it, so a range that selected on the request date would
    /// find nothing and this test would fail on an empty result rather than on a wrong column.
    /// </summary>
    [Fact]
    public async Task The_export_selects_money_refunds_settled_inside_the_range_and_leaves_them_alone()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync("Export Learner");
        var orderId = await SeedPaidOrderAsync(learner, credits: 5, amount: 199000m);
        var service = CreateService();

        var requested = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "Chuyển khoản trùng", OrderId: orderId, Amount: 75000m));

        // Requested a month before the window it is paid in: the two dates must disagree for this test
        // to say anything about which one the filter reads.
        _clock.Advance(TimeSpan.FromDays(30));
        await service.ApproveRefundAsync(staff, requested.RefundId);

        var settledAt = _clock.GetUtcNow();
        await service.SettleRefundAsync(staff, requested.RefundId);

        // The reporting day is read on the accountant's calendar (UTC+7), not the server's UTC date: near
        // midnight those two disagree, and the range must be built the way the service cuts it.
        var local = settledAt.ToOffset(ReportingPeriod.UtcOffset);
        var day = DateOnly.FromDateTime(local.DateTime);
        var rows = await service.GetRefundExportRowsAsync(day, day);

        var row = Assert.Single(rows, r => r.RefundId == requested.RefundId);
        Assert.Equal(RefundMethodDto.Money, row.Method);
        Assert.Equal(75000m, row.Amount);
        Assert.Null(row.CreditCount);
        Assert.Equal("Export Learner", row.LearnerName);
        Assert.Equal("Chuyển khoản trùng", row.Reason);
        Assert.Equal("Staff Member", row.DecidingAdmin);
        Assert.NotNull(row.OrderCode);
        Assert.Equal(day, row.SettledOn);

        // The request date is the earlier one, on the same calendar as the settlement.
        Assert.Equal(DateOnly.FromDateTime(local.AddDays(-30).DateTime), row.RequestedOn);

        // Exporting again returns the same rows, and the first export did not settle, approve or
        // otherwise touch the refund: an export is a read.
        var again = await service.GetRefundExportRowsAsync(day, day);
        Assert.Equal(rows.Count, again.Count);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.Refunds.SingleAsync(r => r.RefundId == requested.RefundId);
        Assert.Equal("settled", stored.Status);
        Assert.Equal(settledAt, stored.SettledAt);
    }

    /// <summary>
    /// #283: a row is only exported once the money has moved, and a credit refund never is. Both are
    /// money facts, so neither can be relaxed to "approved" without the batch paying twice.
    /// </summary>
    [Fact]
    public async Task The_export_carries_only_settled_money_refunds()
    {
        var staff = await SeedStaffAsync();
        var learner = await SeedLearnerAsync("Not Settled Learner");
        var service = CreateService();

        var approvedOnly = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "Chưa chuyển khoản", Amount: 100m));
        await service.ApproveRefundAsync(staff, approvedOnly.RefundId);

        var credit = await service.RequestCreditRefundAsync(
            staff, learner, creditCount: 3, reason: "Bù credit");
        await service.ApproveRefundAsync(staff, credit.RefundId);

        var settledMoney = await service.RequestRefundAsync(
            staff,
            new RequestRefundRequest(learner, RefundMethodDto.Money, CreditCount: 0,
                Reason: "Đã chuyển khoản", Amount: 200m));
        await service.ApproveRefundAsync(staff, settledMoney.RefundId);
        await service.SettleRefundAsync(staff, settledMoney.RefundId);

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var rows = await service.GetRefundExportRowsAsync(today.AddDays(-1), today.AddDays(1));

        Assert.Contains(rows, r => r.RefundId == settledMoney.RefundId);
        Assert.DoesNotContain(rows, r => r.RefundId == approvedOnly.RefundId);
        Assert.DoesNotContain(rows, r => r.RefundId == credit.RefundId);
    }

    [Fact]
    public async Task An_empty_export_range_is_an_empty_list_not_a_refusal()
    {
        var service = CreateService();

        var rows = await service.GetRefundExportRowsAsync(
            new DateOnly(2001, 1, 1), new DateOnly(2001, 1, 2));

        Assert.Empty(rows);
    }
}
