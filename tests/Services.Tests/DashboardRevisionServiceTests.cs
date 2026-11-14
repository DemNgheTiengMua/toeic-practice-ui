using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #286 / ADR-0013: the revision is a comparable value, and these are the four cases that define
/// "comparable". A revision that never changed would satisfy "unchanged when nothing changed"; a revision
/// made of a constant would satisfy "changed by a new paid order". All four together only pass on a value
/// that genuinely derives from the rows.
/// <para>
/// The database is real - schema, triggers, check constraints and all - because the point of ADR-0013 is
/// that the rows the dashboard counts are countable from existing columns. A mocked DbContext could not
/// disagree with the real one, and a row the schema refuses would never reach the query.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DashboardRevisionServiceTests(DatabaseFixture database)
{
    private static DashboardRevisionService CreateService(CompanionDbContext db) => new(db);

    /// <summary>A learner and an admin: the minimum a paid order or a refund can hang from.</summary>
    private const string BaseFixture = """
        INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner'), ('admin', 'Admin');
        INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsLocked, EmailVerifiedAt, IsDeleted, CreatedAt)
        VALUES
          ((SELECT RoleId FROM Roles WHERE Code='learner'), 'rev@test.local', 0x01, N'Rev User', 0, '2026-10-01 00:00:00 +00:00', 0, '2026-10-01 00:00:00 +00:00'),
          ((SELECT RoleId FROM Roles WHERE Code='admin'), 'revadmin@test.local', 0x01, N'Rev Admin', 0, '2026-10-01 00:00:00 +00:00', 0, '2026-10-01 00:00:00 +00:00');
        """;

    [Fact]
    public async Task The_revision_is_unchanged_when_nothing_changed()
    {
        var dbName = $"EERevSame_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync(BaseFixture);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var first = await CreateService(db).GetRevisionAsync();
                var second = await CreateService(db).GetRevisionAsync();

                Assert.Equal(first, second);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// A paid order moves both halves of the paid figure. The pending order beside it is the control: an
    /// order that has not been paid must not move the revision, or every abandoned checkout would redraw
    /// every admin's dashboard.
    /// </summary>
    [Fact]
    public async Task A_new_paid_order_changes_the_revision()
    {
        var dbName = $"EERevPaid_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync(BaseFixture + """

                    INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt)
                    VALUES ((SELECT UserId FROM Users WHERE Email='rev@test.local'), 5,
                            'pending', 100000, 'DH-REV-0001', 41001, 'payos', '2026-10-05 10:00:00 +00:00');
                    """);
            }

            DashboardRevision before;
            await using (var db = DbContextFactory.Create(connStr))
            {
                before = await CreateService(db).GetRevisionAsync();
                Assert.Equal(0, before.PaidOrdersCount);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES ((SELECT UserId FROM Users WHERE Email='rev@test.local'), 5,
                            'paid', 250000, 'DH-REV-0002', 41002, 'payos', '2026-10-06 10:00:00 +00:00', '2026-10-06 10:00:00 +00:00');
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var after = await CreateService(db).GetRevisionAsync();

                Assert.NotEqual(before, after);
                Assert.Equal(1, after.PaidOrdersCount);
                Assert.Equal(250000m, after.PaidOrdersAmount);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// A complaint moving from <c>open</c> to <c>reviewing</c> leaves the row count identical and moves one
    /// bucket to another. That is exactly the case ADR-0013 says a total would miss and a per-status
    /// breakdown catches - and the case an update-time column would have to be added for.
    /// </summary>
    [Fact]
    public async Task A_complaint_moving_between_two_statuses_changes_the_revision()
    {
        var dbName = $"EERevComplaint_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await using (var db = DbContextFactory.Create(connStr))
            {
                // CK_Complaints_TypeMatchesSubject: a complaint must name the thing it is about, so a
                // payment_issue needs an order behind it. The fixture therefore carries one paid order.
                await db.Database.ExecuteSqlRawAsync(BaseFixture + """

                    DECLARE @UserId INT = (SELECT UserId FROM Users WHERE Email='rev@test.local');
                    INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, 5, 'paid', 100000, 'DH-REV-0003', 41003, 'payos',
                            '2026-10-07 08:00:00 +00:00', '2026-10-07 08:00:00 +00:00');

                    INSERT INTO Complaints (UserId, OrderId, Type, Reason, Status, CreatedAt)
                    VALUES (@UserId, (SELECT OrderId FROM Orders WHERE OrderCode='DH-REV-0003'),
                            'payment_issue', N'Charged twice', 'open', '2026-10-07 09:00:00 +00:00');
                    """);
            }

            DashboardRevision before;
            await using (var db = DbContextFactory.Create(connStr))
            {
                before = await CreateService(db).GetRevisionAsync();
                Assert.Equal(1, before.ComplaintsOpen);
                Assert.Equal(0, before.ComplaintsReviewing);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync("""
                    UPDATE Complaints SET Status='reviewing' WHERE Status='open';
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var after = await CreateService(db).GetRevisionAsync();

                Assert.NotEqual(before, after);
                Assert.Equal(0, after.ComplaintsOpen);
                Assert.Equal(1, after.ComplaintsReviewing);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// Settling a refund moves the bucket and the settled-money pair. The requested refund beside it shows
    /// the difference matters: a refund nobody has decided is not money that left.
    /// </summary>
    [Fact]
    public async Task A_refund_being_settled_changes_the_revision()
    {
        var dbName = $"EERevRefund_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync(BaseFixture + """

                    DECLARE @UserId INT = (SELECT UserId FROM Users WHERE Email='rev@test.local');
                    DECLARE @AdminId INT = (SELECT UserId FROM Users WHERE Email='revadmin@test.local');

                    INSERT INTO Refunds (UserId, Method, Reason, Status, Amount, RequestedByUserId, DecidedByUserId, DecidedAt, CreatedAt)
                    VALUES (@UserId, 'money', N'Not delivered', 'approved', 120000, @AdminId, @AdminId,
                            '2026-10-08 10:00:00 +00:00', '2026-10-08 10:00:00 +00:00');
                    INSERT INTO Refunds (UserId, Method, Reason, Status, Amount, RequestedByUserId, CreatedAt)
                    VALUES (@UserId, 'money', N'Waiting', 'requested', 40000, @AdminId, '2026-10-09 10:00:00 +00:00');
                    """);
            }

            DashboardRevision before;
            await using (var db = DbContextFactory.Create(connStr))
            {
                before = await CreateService(db).GetRevisionAsync();
                Assert.Equal(1, before.RefundsApproved);
                Assert.Equal(0, before.RefundsSettled);
                Assert.Equal(0, before.SettledMoneyRefundsCount);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync("""
                    DECLARE @AdminId INT = (SELECT UserId FROM Users WHERE Email='revadmin@test.local');
                    UPDATE Refunds
                    SET Status='settled', SettledByUserId=@AdminId, SettledAt='2026-10-10 10:00:00 +00:00'
                    WHERE Status='approved';
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var after = await CreateService(db).GetRevisionAsync();

                Assert.NotEqual(before, after);
                Assert.Equal(0, after.RefundsApproved);
                Assert.Equal(1, after.RefundsSettled);
                Assert.Equal(1, after.SettledMoneyRefundsCount);
                Assert.Equal(120000m, after.SettledMoneyRefundsAmount);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The empty-database arm: a fresh install must not throw, and its revision is the default value so the
    /// stream's first compare is against a well-defined zero rather than a null.
    /// </summary>
    [Fact]
    public async Task A_fresh_database_reports_the_empty_revision()
    {
        var dbName = $"EERevEmpty_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await using var db = DbContextFactory.Create(connStr);

            var revision = await CreateService(db).GetRevisionAsync();

            Assert.Equal(DashboardRevision.Empty, revision);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }
}
