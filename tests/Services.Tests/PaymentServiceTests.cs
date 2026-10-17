using BusinessObjects;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class PaymentServiceTests(DatabaseFixture database)
{
    private PaymentService CreateService(IPaymentGateway? gateway = null) =>
        new(DbContextFactory.Create(database.ConnectionString), gateway ?? new MockPaymentGateway());

    private async Task<int> SeedUserAsync(bool isVerified = true)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var verifiedSql = isVerified ? "SYSDATETIMEOFFSET()" : "NULL";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Payment Tester', {verifiedSql});
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<int> SeedPackageAsync(string name, int credits, decimal price)
    {
        await database.SqlAsync($"""
            INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
            VALUES (N'{name}', {credits}, {price}, 1, 0, N'Feature 1', 1);
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Packages.Where(p => p.Name == name).Select(p => p.PackageId).OrderByDescending(p => p).FirstAsync();
    }

    [Fact]
    public async Task Unverified_learners_are_blocked_from_checkout_preview_and_creation()
    {
        var userId = await SeedUserAsync(isVerified: false);
        var pkgId = await SeedPackageAsync("Starter 1", 5, 100000);
        var service = CreateService();

        await Assert.ThrowsAsync<EmailNotVerifiedException>(() =>
            service.GetCheckoutPreviewAsync(userId, pkgId));

        await Assert.ThrowsAsync<EmailNotVerifiedException>(() =>
            service.CreateOrderAsync(userId, pkgId, "http://localhost:5000"));
    }

    [Fact]
    public async Task Verified_learner_can_preview_checkout_with_balance_and_balance_after()
    {
        var userId = await SeedUserAsync(isVerified: true);
        var pkgId = await SeedPackageAsync("Prep Pack", 5, 199000);
        var service = CreateService();

        // Give initial credits via admin grant
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@test.local', 0x01, N'Admin', SYSDATETIMEOFFSET());
            DECLARE @adminId INT = SCOPE_IDENTITY();
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES ({userId}, 'admin_grant', 8, @adminId, N'Initial grant');
            """);

        var preview = await service.GetCheckoutPreviewAsync(userId, pkgId);

        Assert.Equal(pkgId, preview.PackageId);
        Assert.Equal("Prep Pack", preview.PackageName);
        Assert.Equal(5, preview.Credits);
        Assert.Equal(199000, preview.Price);
        Assert.Equal(8, preview.CurrentBalance);
        Assert.Equal(13, preview.BalanceAfter);
        Assert.Null(preview.OpenOrder);
    }

    [Fact]
    public async Task Creating_order_assigns_sequence_payment_no_and_order_code()
    {
        var userId = await SeedUserAsync(isVerified: true);
        var pkgId = await SeedPackageAsync("Intensive", 10, 349000);
        var service = CreateService();

        var res = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        Assert.True(res.OrderId > 0);
        Assert.StartsWith("DH-", res.OrderCode);
        Assert.True(res.PaymentNo > 0);
        Assert.Equal(349000, res.Amount);
        Assert.Equal(PaymentGatewayDto.Mock, res.Gateway);
        Assert.Equal(OrderStatusDto.Pending, res.Status);
        Assert.Contains(res.OrderCode, res.CheckoutUrl);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var order = await db.Orders.SingleAsync(o => o.OrderId == res.OrderId);
        Assert.Equal("pending", order.Status);
        Assert.Equal(res.PaymentNo, order.PaymentNo);
    }

    [Fact]
    public async Task Only_one_open_order_per_learner_blocks_second_order()
    {
        var userId = await SeedUserAsync(isVerified: true);
        var pkgId = await SeedPackageAsync("Single", 1, 49000);
        var service = CreateService();

        var first = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        var ex = await Assert.ThrowsAsync<OrderAlreadyOpenException>(() =>
            service.CreateOrderAsync(userId, pkgId, "http://localhost:5000"));

        Assert.Equal(first.OrderCode, ex.OrderCode);
        Assert.Equal("Single", ex.PackageName);
        Assert.Equal(OrderStatus.Pending, ex.Status);

        // Preview also exposes the open order
        var preview = await service.GetCheckoutPreviewAsync(userId, pkgId);
        Assert.NotNull(preview.OpenOrder);
        Assert.Equal(first.OrderCode, preview.OpenOrder.OrderCode);
    }

    [Fact]
    public async Task Orders_unpaid_after_15_minutes_become_expired()
    {
        var userId = await SeedUserAsync(isVerified: true);
        var pkgId = await SeedPackageAsync("StaleTest", 2, 80000);
        var service = CreateService();

        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        // Backdate order to 16 minutes ago
        await database.SqlAsync($"""
            UPDATE Orders SET CreatedAt = DATEADD(MINUTE, -16, SYSDATETIMEOFFSET())
            WHERE OrderId = {order.OrderId};
            """);

        await service.ExpireStaleOrdersAsync(olderThanMinutes: 15);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var updated = await db.Orders.SingleAsync(o => o.OrderId == order.OrderId);
        Assert.Equal("expired", updated.Status);

        // Learner can now create a new order without error
        var second = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");
        Assert.NotNull(second);
        Assert.NotEqual(order.OrderId, second.OrderId);
    }

    [Fact]
    public async Task Return_redirect_never_grants_credit_and_logs_return_redirect_event()
    {
        var userId = await SeedUserAsync(isVerified: true);
        var pkgId = await SeedPackageAsync("TrustTest", 5, 199000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        // Learner returns via browser redirect (gateway unconfirmed)
        var result = await service.HandleReturnRedirectAsync(userId, order.OrderCode);

        // Must NOT grant credits!
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var creditTxn = await db.CreditTransactions.FirstOrDefaultAsync(t => t.OrderId == order.OrderId);
        Assert.Null(creditTxn);

        var events = await db.PaymentEvents.Where(e => e.OrderId == order.OrderId).ToListAsync();
        var redirectEvent = events.Single(e => e.Source == "return_redirect");
        Assert.False(redirectEvent.WasApplied);
        Assert.False(redirectEvent.SignatureValid);

        // Order is reconciling because payment was unconfirmed
        Assert.Equal(OrderStatusDto.Reconciling, result.Status);
    }

    [Fact]
    public async Task Returning_learner_with_unconfirmed_payment_sees_order_as_reconciling()
    {
        var userId = await SeedUserAsync(isVerified: true);
        var pkgId = await SeedPackageAsync("ReconcileTest", 5, 199000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        var result = await service.HandleReturnRedirectAsync(userId, order.OrderCode);

        Assert.Equal(OrderStatusDto.Reconciling, result.Status);

        var queried = await service.GetPaymentResultAsync(userId, order.OrderCode);
        Assert.Equal(OrderStatusDto.Reconciling, queried.Status);
    }

    [Fact]
    public async Task Settle_is_idempotent_and_repeating_confirmation_never_credits_twice()
    {
        var userId = await SeedUserAsync(isVerified: true);
        var pkgId = await SeedPackageAsync("IdempotentTest", 5, 199000);
        var service = CreateService();

        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        // First settle via trusted source
        var settled1 = await service.SettleOrderAsync(
            order.PaymentNo,
            source: PaymentEventSource.StatusQuery,
            signatureValid: false,
            rawPayload: "{\"status\":\"paid\"}",
            note: "First status query confirmation");

        Assert.True(settled1);

        await using var db1 = DbContextFactory.Create(database.ConnectionString);
        var txns1 = await db1.CreditTransactions.Where(t => t.OrderId == order.OrderId).ToListAsync();
        Assert.Single(txns1);
        Assert.Equal(5, txns1[0].Delta);
        Assert.Equal("purchase", txns1[0].Reason);

        var events1 = await db1.PaymentEvents.Where(e => e.OrderId == order.OrderId && e.Source == "status_query").ToListAsync();
        Assert.Single(events1);
        Assert.True(events1[0].WasApplied);

        // Second settle attempt for same order (e.g. repeated callback or query)
        var settled2 = await service.SettleOrderAsync(
            order.PaymentNo,
            source: PaymentEventSource.StatusQuery,
            signatureValid: false,
            rawPayload: "{\"status\":\"paid\"}",
            note: "Repeat query");

        Assert.True(settled2);

        await using var db2 = DbContextFactory.Create(database.ConnectionString);
        var txns2 = await db2.CreditTransactions.Where(t => t.OrderId == order.OrderId).ToListAsync();
        Assert.Single(txns2); // STILL EXACTLY ONE! Never double credited.

        var balance = await db2.vw_UserCreditBalances.Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(5, balance);

        var events2 = await db2.PaymentEvents.Where(e => e.OrderId == order.OrderId && e.Source == "status_query").ToListAsync();
        Assert.Equal(2, events2.Count);
        Assert.False(events2[1].WasApplied); // Repeat was logged but not applied
    }

    [Fact]
    public async Task Credit_is_granted_only_on_trusted_source()
    {
        var userId = await SeedUserAsync(isVerified: true);
        var pkgId = await SeedPackageAsync("SourceSecurity", 5, 199000);
        var service = CreateService();

        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        // Untrusted source 1: return_redirect
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SettleOrderAsync(order.PaymentNo, source: PaymentEventSource.ReturnRedirect, signatureValid: false));

        // Untrusted source 2: webhook with signatureValid = false
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SettleOrderAsync(order.PaymentNo, source: PaymentEventSource.Webhook, signatureValid: false));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var txns = await db.CreditTransactions.Where(t => t.OrderId == order.OrderId).ToListAsync();
        Assert.Empty(txns);
    }

    [Fact]
    public async Task Wallet_balance_and_summary_are_read_from_ledger_views()
    {
        var userId = await SeedUserAsync(isVerified: true);
        var pkg1 = await SeedPackageAsync("WPkg1", 5, 199000);
        var pkg2 = await SeedPackageAsync("WPkg2", 10, 349000);
        var service = CreateService();

        // Order 1: paid
        var o1 = await service.CreateOrderAsync(userId, pkg1, "http://localhost:5000");
        await service.SettleOrderAsync(o1.PaymentNo, PaymentEventSource.StatusQuery, false);

        // Order 2: paid
        var o2 = await service.CreateOrderAsync(userId, pkg2, "http://localhost:5000");
        await service.SettleOrderAsync(o2.PaymentNo, PaymentEventSource.StatusQuery, false);

        // Seed a mock exam spend (-1 credit)
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('WTEST', N'Wallet Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Wallet Mock', 'published', 120, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
            VALUES ({userId}, @exam, @cert, 'in_progress', SYSDATETIMEOFFSET(), DATEADD(HOUR, 2, SYSDATETIMEOFFSET()));
            DECLARE @attempt INT = SCOPE_IDENTITY();
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AttemptId)
            VALUES ({userId}, 'mock_exam_start', -1, @attempt);
            """);

        var wallet = await service.GetWalletAsync(userId);

        Assert.Equal(14, wallet.Balance); // 15 - 1
        Assert.Equal(15, wallet.CreditsPurchased);
        Assert.Equal(1, wallet.CreditsUsed);
        Assert.Equal(548000, wallet.AmountPurchased);
        Assert.Equal(2, wallet.PaidOrderCount);
        Assert.Equal(2, wallet.Orders.Count);
        Assert.Equal(3, wallet.Ledger.Count); // 2 purchases + 1 mock exam start
    }

    [Fact]
    public async Task Mock_gateway_actions_simulate_success_failed_pending_and_expired()
    {
        var pkgId = await SeedPackageAsync("ActionTest", 5, 199000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        // 1. Success action
        var u1 = await SeedUserAsync(isVerified: true);
        var oSuccess = await service.CreateOrderAsync(u1, pkgId, "http://localhost:5000");
        var resSuccess = await service.ApplyMockGatewayActionAsync(u1, oSuccess.OrderCode, "success");
        Assert.Equal(OrderStatusDto.Paid, resSuccess.Status);
        Assert.Equal(5, resSuccess.Credits);

        // 2. Failed action
        var u2 = await SeedUserAsync(isVerified: true);
        var oFailed = await service.CreateOrderAsync(u2, pkgId, "http://localhost:5000");
        var resFailed = await service.ApplyMockGatewayActionAsync(u2, oFailed.OrderCode, "failed", errorCode: "24");
        Assert.Equal(OrderStatusDto.Failed, resFailed.Status);
        Assert.Equal("24", resFailed.GatewayErrorCode);

        // 3. Pending/slow gateway action
        var u3 = await SeedUserAsync(isVerified: true);
        var oPending = await service.CreateOrderAsync(u3, pkgId, "http://localhost:5000");
        var resPending = await service.ApplyMockGatewayActionAsync(u3, oPending.OrderCode, "pending");
        Assert.Equal(OrderStatusDto.Reconciling, resPending.Status);

        // 4. Expired action
        var u4 = await SeedUserAsync(isVerified: true);
        var oExpired = await service.CreateOrderAsync(u4, pkgId, "http://localhost:5000");
        var resExpired = await service.ApplyMockGatewayActionAsync(u4, oExpired.OrderCode, "expired");
        Assert.Equal(OrderStatusDto.Expired, resExpired.Status);
    }

    [Fact]
    public async Task Reconciling_order_refresh_logs_event_and_does_not_violate_status_constraint()
    {
        var pkgId = await SeedPackageAsync("ReconcileEventTest", 5, 199000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        var userId = await SeedUserAsync(isVerified: true);
        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        // Mark as reconciling
        await service.ApplyMockGatewayActionAsync(userId, order.OrderCode, "pending");

        // Learner returns to Result page multiple times while reconciling (simulating 5s refresh)
        var returnRes = await service.HandleReturnRedirectAsync(userId, order.OrderCode);
        Assert.Equal(OrderStatusDto.Reconciling, returnRes.Status);

        // Status check runs while still unresolved in mock gateway
        var statusRes = await service.GetPaymentResultAsync(userId, order.OrderCode);
        Assert.Equal(OrderStatusDto.Reconciling, statusRes.Status);

        // Verify PaymentEvents table has recorded the events without throwing SQL constraint violations
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var events = await db.PaymentEvents
            .Where(e => e.OrderId == returnRes.OrderId)
            .OrderBy(e => e.EventId)
            .ToListAsync();

        Assert.Contains(events, e => e.Source == "return_redirect" && e.ReportedStatus == "pending");
        Assert.Contains(events, e => e.Source == "status_query" && e.ReportedStatus == "pending");
    }

    [Fact]
    public async Task ExpireStaleOrdersAsync_cancels_payment_link_at_gateway_and_logs_cancel_event()
    {
        var pkgId = await SeedPackageAsync("ExpireCancelTest", 5, 199000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        var userId = await SeedUserAsync(isVerified: true);
        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        // Backdate order to 20 minutes ago
        await database.SqlAsync($"""
            UPDATE Orders
            SET CreatedAt = DATEADD(minute, -20, SYSDATETIMEOFFSET())
            WHERE OrderId = {order.OrderId};
            """);

        var expireService = CreateService(mockGateway);
        await expireService.ExpireStaleOrdersAsync(olderThanMinutes: 15);

        // Verify gateway cancellation
        Assert.True(mockGateway.IsCancelled(order.PaymentNo));

        // Verify database state
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var updated = await db.Orders.SingleAsync(o => o.OrderId == order.OrderId);
        Assert.Equal("expired", updated.Status);

        var cancelEvent = await db.PaymentEvents
            .Where(e => e.OrderId == order.OrderId && e.Source == "cancel")
            .SingleOrDefaultAsync();
        Assert.NotNull(cancelEvent);
        Assert.Equal("expired", cancelEvent.ReportedStatus);
        Assert.True(cancelEvent.WasApplied);
    }

    [Fact]
    public async Task HandleReturnRedirectAsync_with_expired_order_cancels_at_gateway()
    {
        var pkgId = await SeedPackageAsync("ReturnExpireCancelTest", 5, 199000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        var userId = await SeedUserAsync(isVerified: true);
        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        // Backdate order to 20 minutes ago
        await database.SqlAsync($"""
            UPDATE Orders
            SET CreatedAt = DATEADD(minute, -20, SYSDATETIMEOFFSET())
            WHERE OrderId = {order.OrderId};
            """);

        var returnService = CreateService(mockGateway);
        var result = await returnService.HandleReturnRedirectAsync(userId, order.OrderCode);

        Assert.Equal(OrderStatusDto.Expired, result.Status);
        Assert.True(mockGateway.IsCancelled(order.PaymentNo));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cancelEvent = await db.PaymentEvents
            .Where(e => e.OrderId == order.OrderId && e.Source == "cancel")
            .SingleOrDefaultAsync();
        Assert.NotNull(cancelEvent);
        Assert.Equal("expired", cancelEvent.ReportedStatus);
    }

    [Fact]
    public async Task GetPaymentResultAsync_with_timed_out_reconciling_order_cancels_at_gateway()
    {
        var pkgId = await SeedPackageAsync("ReconcileTimeoutCancelTest", 5, 199000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        var userId = await SeedUserAsync(isVerified: true);
        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        // Mark as reconciling and backdate
        await service.ApplyMockGatewayActionAsync(userId, order.OrderCode, "pending");
        await database.SqlAsync($"""
            UPDATE Orders
            SET CreatedAt = DATEADD(minute, -20, SYSDATETIMEOFFSET())
            WHERE OrderId = {order.OrderId};
            """);

        var resultService = CreateService(mockGateway);
        var result = await resultService.GetPaymentResultAsync(userId, order.OrderCode);

        Assert.Equal(OrderStatusDto.Expired, result.Status);
        Assert.True(mockGateway.IsCancelled(order.PaymentNo));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cancelEvent = await db.PaymentEvents
            .Where(e => e.OrderId == order.OrderId && e.Source == "cancel")
            .SingleOrDefaultAsync();
        Assert.NotNull(cancelEvent);
        Assert.Equal("expired", cancelEvent.ReportedStatus);
    }

    [Fact]
    public async Task GetCheckoutPreviewAsync_auto_expires_stale_orders_allowing_new_checkout()
    {
        var pkgId = await SeedPackageAsync("AutoExpirePreviewTest", 5, 199000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        var userId = await SeedUserAsync(isVerified: true);
        var staleOrder = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        // Backdate order to 20 minutes ago
        await database.SqlAsync($"""
            UPDATE Orders
            SET CreatedAt = DATEADD(minute, -20, SYSDATETIMEOFFSET())
            WHERE OrderId = {staleOrder.OrderId};
            """);

        // Learner visits checkout preview
        var previewService = CreateService(mockGateway);
        var preview = await previewService.GetCheckoutPreviewAsync(userId, pkgId);

        // Preview should have cleared the open order by expiring it
        Assert.Null(preview.OpenOrder);
        Assert.True(mockGateway.IsCancelled(staleOrder.PaymentNo));
    }

    [Fact]
    public async Task CancelOrderAsync_when_order_is_pending_cancels_at_gateway_and_marks_failed()
    {
        var pkgId = await SeedPackageAsync("UserCancelTest", 5, 199000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        var userId = await SeedUserAsync(isVerified: true);
        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        // Learner actively cancels order
        await service.CancelOrderAsync(userId, order.OrderCode);

        // Verify gateway was cancelled
        Assert.True(mockGateway.IsCancelled(order.PaymentNo));

        // Verify order status is failed with user_cancelled
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var updatedOrder = await db.Orders.SingleAsync(o => o.OrderId == order.OrderId);
        Assert.Equal("failed", updatedOrder.Status);
        Assert.Equal("user_cancelled", updatedOrder.GatewayErrorCode);

        // Verify user can immediately create a new order without UX_Orders_OnePendingPerUser error
        var newOrder = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");
        Assert.NotNull(newOrder);
    }

    [Fact]
    public async Task HandleWebhookAsync_with_valid_signature_settles_order_and_deposits_credits()
    {
        var pkgId = await SeedPackageAsync("WebhookSettlementTest", 10, 190000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        var userId = await SeedUserAsync(isVerified: true);
        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        var payload = $$"""
            {
              "orderCode": {{order.PaymentNo}},
              "status": "paid",
              "gatewayTxnRef": "MOCK-{{order.PaymentNo}}"
            }
            """;

        var result = await service.HandleWebhookAsync(payload, signature: "valid-sig");

        Assert.True(result.IsValid);
        Assert.Equal(order.PaymentNo, result.PaymentNo);
        Assert.Equal(GatewayStatus.Paid, result.Status);

        // Verify database: order is paid, payment event exists, credit transaction exists
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var updatedOrder = await db.Orders.SingleAsync(o => o.OrderId == order.OrderId);
        Assert.Equal("paid", updatedOrder.Status);
        Assert.NotNull(updatedOrder.PaidAt);

        var webhookEvent = await db.PaymentEvents
            .Where(e => e.OrderId == order.OrderId && e.Source == "webhook")
            .SingleOrDefaultAsync();
        Assert.NotNull(webhookEvent);
        Assert.Equal("paid", webhookEvent.ReportedStatus);
        Assert.True(webhookEvent.SignatureValid);
        Assert.True(webhookEvent.WasApplied);

        var wallet = await service.GetWalletAsync(userId);
        Assert.Equal(10, wallet.Balance);
        Assert.Equal(1, wallet.PaidOrderCount);
    }

    [Fact]
    public async Task HandleWebhookAsync_with_invalid_signature_rejects_and_does_not_settle()
    {
        var pkgId = await SeedPackageAsync("WebhookInvalidSigTest", 10, 190000);
        var mockGateway = new MockPaymentGateway();
        var service = CreateService(mockGateway);

        var userId = await SeedUserAsync(isVerified: true);
        var order = await service.CreateOrderAsync(userId, pkgId, "http://localhost:5000");

        var payload = $$"""
            {
              "orderCode": {{order.PaymentNo}},
              "status": "paid",
              "gatewayTxnRef": "MOCK-{{order.PaymentNo}}"
            }
            """;

        var result = await service.HandleWebhookAsync(payload, signature: "invalid");

        Assert.False(result.IsValid);

        // Verify database: order remains pending, no credits deposited
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbOrder = await db.Orders.SingleAsync(o => o.OrderId == order.OrderId);
        Assert.Equal("pending", dbOrder.Status);

        var wallet = await service.GetWalletAsync(userId);
        Assert.Equal(0, wallet.Balance);
        Assert.Equal(0, wallet.PaidOrderCount);
    }
}
