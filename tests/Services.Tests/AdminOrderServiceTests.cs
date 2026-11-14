using BusinessObjects;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AdminOrderServiceTests(DatabaseFixture database)
{
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private AdminOrderService CreateService(IPaymentService? paymentService = null)
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var ps = paymentService ?? new PaymentService(db, new MockPaymentGateway());
        return new AdminOrderService(db, ps, _clock);
    }

    private async Task<int> SeedUserAsync(string email, string fullName = "Order Tester")
    {
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'{fullName}', SYSDATETIMEOFFSET());
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    [Fact]
    public async Task GetOrdersAsync_filters_by_query_status_gateway_and_computes_stats()
    {
        var user1 = await SeedUserAsync($"{Guid.NewGuid():N}@filter.local", "Nguyen Van A");
        var user2 = await SeedUserAsync($"{Guid.NewGuid():N}@other.local", "Tran Thi B");

        var paymentService = new PaymentService(DbContextFactory.Create(database.ConnectionString), new MockPaymentGateway());
        var order1 = await paymentService.CreateOrderAsync(user1, 5, "http://localhost:5000");
        var order2 = await paymentService.CreateOrderAsync(user2, 5, "http://localhost:5000");

        // Settle order1 as paid
        await paymentService.ApplyMockGatewayActionAsync(user1, order1.OrderCode, "success");

        var service = CreateService(paymentService);

        // Filter by user1 email
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var u1 = await db.Users.SingleAsync(u => u.UserId == user1);
            var result = await service.GetOrdersAsync(q: u1.Email);
            Assert.Contains(result.Orders, o => o.OrderCode == order1.OrderCode);
            Assert.DoesNotContain(result.Orders, o => o.OrderCode == order2.OrderCode);
        }

        // Filter by status "paid"
        var paidResult = await service.GetOrdersAsync(status: "paid");
        Assert.Contains(paidResult.Orders, o => o.OrderCode == order1.OrderCode);
        Assert.DoesNotContain(paidResult.Orders, o => o.OrderCode == order2.OrderCode);

        // Filter by gateway "mock"
        var mockResult = await service.GetOrdersAsync(gateway: "mock");
        Assert.Contains(mockResult.Orders, o => o.OrderCode == order1.OrderCode);

        // Stats reflect paid orders
        Assert.True(paidResult.PaidThisMonthCount >= 1);
        Assert.True(paidResult.PaidThisMonthAmount >= 100000);
    }

    [Fact]
    public async Task GetOrderDetailAsync_shows_every_payment_event_in_chronological_order()
    {
        var user = await SeedUserAsync($"{Guid.NewGuid():N}@events.local", "Event Learner");

        var paymentService = new PaymentService(DbContextFactory.Create(database.ConnectionString), new MockPaymentGateway());
        var order = await paymentService.CreateOrderAsync(user, 5, "http://localhost:5000");

        // Seed 3 distinct payment events with sequential timestamps
        await database.SqlAsync($"""
            INSERT INTO PaymentEvents (OrderId, Source, ReportedStatus, SignatureValid, WasApplied, Note, CreatedAt)
            VALUES ({order.OrderId}, 'return_redirect', 'pending', 0, 0, N'Browser return redirect', DATEADD(minute, -10, SYSDATETIMEOFFSET()));

            INSERT INTO PaymentEvents (OrderId, Source, ReportedStatus, SignatureValid, WasApplied, Note, CreatedAt)
            VALUES ({order.OrderId}, 'status_query', 'pending', 0, 0, N'Server status query poll', DATEADD(minute, -5, SYSDATETIMEOFFSET()));

            INSERT INTO PaymentEvents (OrderId, Source, ReportedStatus, SignatureValid, WasApplied, Note, CreatedAt)
            VALUES ({order.OrderId}, 'webhook', 'paid', 1, 1, N'Gateway webhook confirmed payment', SYSDATETIMEOFFSET());
            """);

        var service = CreateService(paymentService);
        var detail = await service.GetOrderDetailAsync(order.OrderId);

        Assert.NotNull(detail);
        Assert.Equal(order.OrderCode, detail.Order.OrderCode);
        Assert.Equal(3, detail.Events.Count);

        // Acceptance criterion: Order detail shows every payment event in chronological order
        Assert.Equal(PaymentEventSourceDto.ReturnRedirect, detail.Events[0].Source);
        Assert.Equal(GatewayStatusDto.Pending, detail.Events[0].ReportedStatus);
        Assert.False(detail.Events[0].SignatureValid);
        Assert.False(detail.Events[0].WasApplied);

        Assert.Equal(PaymentEventSourceDto.StatusQuery, detail.Events[1].Source);
        Assert.Equal(GatewayStatusDto.Pending, detail.Events[1].ReportedStatus);
        Assert.False(detail.Events[1].WasApplied);

        Assert.Equal(PaymentEventSourceDto.Webhook, detail.Events[2].Source);
        Assert.Equal(GatewayStatusDto.Paid, detail.Events[2].ReportedStatus);
        Assert.True(detail.Events[2].SignatureValid);
        Assert.True(detail.Events[2].WasApplied);

        // Check strictly ascending timestamp order
        Assert.True(detail.Events[0].CreatedAt <= detail.Events[1].CreatedAt);
        Assert.True(detail.Events[1].CreatedAt <= detail.Events[2].CreatedAt);
    }

    [Fact]
    public async Task GetOrderDetailAsync_returns_null_for_nonexistent_order()
    {
        var service = CreateService();
        var detail = await service.GetOrderDetailAsync(999999);
        Assert.Null(detail);
    }

    /// <summary>
    /// #279 / #284: the dashboard and the orders screen are one question asked twice, so they must answer
    /// with one number. The rows below are chosen so the old pair of predicates could not agree and neither
    /// matched the accountant's October: a month is cut at UTC+7 midnight, and a paid order whose payment
    /// timestamp is missing is counted once, by the day it was created.
    /// <para>
    /// #284 splits one question into two. The orders screen reports <b>gross receipts</b> - the money the
    /// gateway took, which is what an order list is a list of - and the dashboard reports <b>net</b>, which
    /// subtracts refunds. The two are asserted to agree on the paid half exactly, and the refund row below
    /// proves the difference is the refund and nothing else: a dashboard that silently went back to gross,
    /// or an orders screen that started subtracting money it does not list, fails here.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Dashboard_and_orders_screen_report_the_same_paid_total_and_differ_only_by_refunds()
    {
        var dbName = $"EEOrderAgree_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner'), ('admin', 'Admin');
                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsDeleted, CreatedAt)
                    VALUES ((SELECT RoleId FROM Roles WHERE Code='learner'), 'agree@test.local', 0x01, N'Agree User', 0, '2026-10-01 00:00:00 +00:00');
                    DECLARE @UserId INT = SCOPE_IDENTITY();
                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsDeleted, CreatedAt)
                    VALUES ((SELECT RoleId FROM Roles WHERE Code='admin'), 'agreeadmin@test.local', 0x01, N'Agree Admin', 0, '2026-10-01 00:00:00 +00:00');
                    DECLARE @AdminId INT = SCOPE_IDENTITY();

                    -- Paid 23:30 on 31 October Vietnam time: the last half hour of the month. October.
                    INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, 5, 'paid', 199000, 'DH-AGREE-0001', 30001, 'payos', '2026-10-31 16:20:00 +00:00', '2026-10-31 16:30:00 +00:00');

                    -- Paid 00:30 on 1 November Vietnam time = 17:30Z on 31 October. November, not October.
                    INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, 5, 'paid', 499000, 'DH-AGREE-0002', 30002, 'payos', '2026-10-31 17:25:00 +00:00', '2026-10-31 17:30:00 +00:00');

                    -- Paid with no payment timestamp, created 5 October: counted once, by its creation day.
                    INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, 5, 'paid', 99000, 'DH-AGREE-0003', 30003, 'mock', '2026-10-05 10:00:00 +00:00', NULL);

                    -- Paid in September: in neither screen's October.
                    INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, 5, 'paid', 299000, 'DH-AGREE-0004', 30004, 'payos', '2026-09-20 10:00:00 +00:00', '2026-09-20 10:05:00 +00:00');

                    -- One money refund settled in October. It is the whole difference between the screens.
                    INSERT INTO Refunds (UserId, Method, Reason, Status, Amount, RequestedByUserId, DecidedByUserId, DecidedAt, SettledByUserId, SettledAt, CreatedAt)
                    VALUES (@UserId, 'money', N'Service not delivered', 'settled', 100000, @AdminId, @AdminId,
                            '2026-10-10 09:00:00 +00:00', @AdminId, '2026-10-12 09:00:00 +00:00', '2026-10-10 09:00:00 +00:00');
                    """);
            }

            await using var dashboardDb = DbContextFactory.Create(connStr);
            await using var ordersDb = DbContextFactory.Create(connStr);

            var dashboard = await new AdminDashboardService(dashboardDb, clock).GetDashboardMetricsAsync();
            var orders = await new AdminOrderService(ordersDb, new PaymentService(ordersDb, new MockPaymentGateway()), clock)
                .GetOrdersAsync();

            Assert.Equal(2, dashboard.PaidOrdersThisMonthCount);
            Assert.Equal(dashboard.PaidOrdersThisMonthCount, orders.PaidThisMonthCount);

            // Gross: the gateway took 298,000. Net: 100,000 of it went back, so October kept 198,000.
            Assert.Equal(199000m + 99000m, orders.PaidThisMonthAmount);
            Assert.Equal(199000m + 99000m - 100000m, dashboard.RevenueThisMonthVnd);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }
}
