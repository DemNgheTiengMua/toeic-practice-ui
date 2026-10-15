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

    private async Task<int> SeedPackageAsync(string name, int credits, decimal price)
    {
        await database.SqlAsync($"""
            INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
            VALUES (N'{name}', {credits}, {price}, 1, 0, N'Feature', 1);
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Packages.Where(p => p.Name == name).Select(p => p.PackageId).OrderByDescending(p => p).FirstAsync();
    }

    [Fact]
    public async Task GetOrdersAsync_filters_by_query_status_gateway_and_computes_stats()
    {
        var user1 = await SeedUserAsync($"{Guid.NewGuid():N}@filter.local", "Nguyen Van A");
        var user2 = await SeedUserAsync($"{Guid.NewGuid():N}@other.local", "Tran Thi B");
        var pkg = await SeedPackageAsync("OrderPack", 5, 199000);

        var paymentService = new PaymentService(DbContextFactory.Create(database.ConnectionString), new MockPaymentGateway());
        var order1 = await paymentService.CreateOrderAsync(user1, pkg, "http://localhost:5000");
        var order2 = await paymentService.CreateOrderAsync(user2, pkg, "http://localhost:5000");

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
        Assert.True(paidResult.PaidThisMonthAmount >= 199000);
    }

    [Fact]
    public async Task GetOrderDetailAsync_shows_every_payment_event_in_chronological_order()
    {
        var user = await SeedUserAsync($"{Guid.NewGuid():N}@events.local", "Event Learner");
        var pkg = await SeedPackageAsync("EventPack", 5, 199000);

        var paymentService = new PaymentService(DbContextFactory.Create(database.ConnectionString), new MockPaymentGateway());
        var order = await paymentService.CreateOrderAsync(user, pkg, "http://localhost:5000");

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
}
