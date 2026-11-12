#pragma warning disable EF1002, CA2100
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AdminDashboardServiceTests(DatabaseFixture database)
{
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    private AdminDashboardService CreateService(CompanionDbContext? db = null, TimeProvider? clock = null)
    {
        var context = db ?? DbContextFactory.Create(database.ConnectionString);
        return new AdminDashboardService(context, clock ?? _clock);
    }

    [Fact]
    public async Task Fresh_database_with_empty_tables_returns_zeros_and_empty_lists_without_throwing()
    {
        var dbName = $"EEDashEmpty_{Guid.NewGuid():N}"[..25];
        var freshConnStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await using var db = DbContextFactory.Create(freshConnStr);
            var service = CreateService(db);

            var result = await service.GetDashboardMetricsAsync();

            Assert.Equal(0, result.ActiveLearnersCount);
            Assert.Equal(0, result.NewLearners7DaysCount);
            Assert.Equal(0, result.CreditsUsedThisMonth);
            Assert.Equal(0m, result.CreditsPerDayAverage);
            Assert.Equal(0m, result.RevenueThisMonthVnd);
            Assert.Equal(0, result.PaidOrdersThisMonthCount);
            Assert.Equal(0m, result.RevenueTodayVnd);
            Assert.Equal(0, result.CreditsRefundedThisMonth);
            Assert.Empty(result.MostAttemptedExams);
            Assert.Equal(0, result.NeedsAttention.DraftExamsCount);
            Assert.Equal(0, result.NeedsAttention.StalePendingOrdersCount);
            Assert.Equal(0, result.NeedsAttention.OpenComplaintsCount);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Calculates_active_and_new_learners_correctly()
    {
        var dbName = $"EEDashLearners_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await using (var db = DbContextFactory.Create(connStr))
            {
                var now = _clock.GetUtcNow();

                // 1. Active & New (registered 2 days ago, verified, not locked)
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner'), ('admin', 'Admin');
                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsLocked, EmailVerifiedAt, IsDeleted, CreatedAt)
                    VALUES
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'active1@test.local', 0x01, N'Active 1', 0, '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'active2@test.local', 0x01, N'Active 2', 0, '{now.AddDays(-10):yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now.AddDays(-10):yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'unverified@test.local', 0x01, N'Unverified', 0, NULL, 0, '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'locked@test.local', 0x01, N'Locked', 1, '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'deleted@test.local', 0x01, N'Deleted', 0, '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}', 1, '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='admin'), 'admin@test.local', 0x01, N'Admin', 0, '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var service = CreateService(db);
                var result = await service.GetDashboardMetricsAsync();

                // Active: active1 and active2 (both learner, not locked, not deleted, verified)
                Assert.Equal(2, result.ActiveLearnersCount);

                // New in 7 days: active1, unverified, locked (all learners, not deleted, created within 7 days)
                Assert.Equal(3, result.NewLearners7DaysCount);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Calculates_credits_used_this_month_and_average_per_day()
    {
        var dbName = $"EEDashCredits_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            // Clock on the 10th of October
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero));
            var now = clock.GetUtcNow();

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner'), ('admin', 'Admin');
                    INSERT INTO Certificates (Code, Name, Description, IsActive) VALUES ('toeic', 'TOEIC', 'Desc', 1);
                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsLocked, EmailVerifiedAt, IsDeleted, CreatedAt)
                    VALUES
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'learner@test.local', 0x01, N'Learner', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='admin'), 'admin@test.local', 0x01, N'Admin', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    DECLARE @UserId INT = (SELECT UserId FROM Users WHERE Email='learner@test.local');
                    DECLARE @AdminId INT = (SELECT UserId FROM Users WHERE Email='admin@test.local');
                    DECLARE @CertId INT = (SELECT CertId FROM Certificates WHERE Code='toeic');

                    -- 1. Grant initial credits (Note required for admin actor)
                    INSERT INTO CreditTransactions (UserId, AdminUserId, Reason, Delta, Note, CreatedAt)
                    VALUES (@UserId, @AdminId, 'admin_grant', 100, N'Initial grant', '{now.AddDays(-8):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- 2. Mock exam attempt & mock_exam_start debit (-10) this month (5th of Oct)
                    INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, QuestionCount, IsDeleted, CreatedAt)
                    VALUES (@CertId, 'Mock 1', 'published', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @ExamId INT = SCOPE_IDENTITY();

                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
                    VALUES (@UserId, @ExamId, @CertId, 'graded', '{now.AddDays(-5):yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddDays(-5):yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @AttemptId INT = SCOPE_IDENTITY();

                    INSERT INTO CreditTransactions (UserId, AttemptId, Reason, Delta, CreatedAt)
                    VALUES (@UserId, @AttemptId, 'mock_exam_start', -10, '{now.AddDays(-5):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- 3. AI grading debit (-1) this month (8th of Oct)
                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
                    VALUES (@UserId, @ExamId, @CertId, 'graded', '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @AttemptId2 INT = SCOPE_IDENTITY();

                    INSERT INTO CreditTransactions (UserId, AttemptId, Reason, Delta, CreatedAt)
                    VALUES (@UserId, @AttemptId2, 'ai_grading', -1, '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- 4. Admin revoke (-5) this month (Note required, should be EXCLUDED from credits used)
                    INSERT INTO CreditTransactions (UserId, AdminUserId, Reason, Delta, Note, CreatedAt)
                    VALUES (@UserId, @AdminId, 'admin_revoke', -5, N'Admin revoke note', '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- 5. Mock exam attempt & debit (-8) from PREVIOUS month (20th of Sept - should be EXCLUDED)
                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
                    VALUES (@UserId, @ExamId, @CertId, 'graded', '2026-09-20 10:00:00 +00:00', '2026-09-20 12:00:00 +00:00');
                    DECLARE @AttemptIdOld INT = SCOPE_IDENTITY();

                    INSERT INTO CreditTransactions (UserId, AttemptId, Reason, Delta, CreatedAt)
                    VALUES (@UserId, @AttemptIdOld, 'mock_exam_start', -8, '2026-09-20 10:00:00 +00:00');
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var service = CreateService(db, clock);
                var result = await service.GetDashboardMetricsAsync();

                // Credits used this month: 10 + 1 = 11 (excludes 5 admin_revoke and 8 from last month)
                Assert.Equal(11, result.CreditsUsedThisMonth);

                // Average per day: day is 10, so 11 / 10 = 1.1 credits/day
                Assert.Equal(1.1m, result.CreditsPerDayAverage);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Calculates_revenue_and_paid_orders_count_this_month()
    {
        var dbName = $"EEDashRev_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
            var now = clock.GetUtcNow();

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner');
                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsLocked, EmailVerifiedAt, IsDeleted, CreatedAt)
                    VALUES ((SELECT RoleId FROM Roles WHERE Code='learner'), 'rev@test.local', 0x01, N'Rev User', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @UserId INT = SCOPE_IDENTITY();

                    INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
                    VALUES (N'Pkg1', 5, 199000, 1, 0, N'Features', 1);
                    DECLARE @PkgId INT = SCOPE_IDENTITY();

                    -- Order 1: Paid this month
                    INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, @PkgId, 'paid', 199000, 'DH-20261015-0001', 10001, 'payos', '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- Order 2: Paid this month
                    INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, @PkgId, 'paid', 499000, 'DH-20261015-0002', 10002, 'mock', '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- Order 3: Pending this month (not paid -> excluded)
                    INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, @PkgId, 'pending', 99000, 'DH-20261015-0003', 10003, 'payos', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', NULL);

                    -- Order 4: Paid in PREVIOUS month (September -> excluded)
                    INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, @PkgId, 'paid', 299000, 'DH-20260920-0001', 9001, 'payos', '2026-09-20 10:00:00 +00:00', '2026-09-20 10:05:00 +00:00');

                    -- Order 5: Paid today, so the day figure is not the month figure.
                    INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, @PkgId, 'paid', 99000, 'DH-20261015-0005', 10005, 'payos', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var service = CreateService(db, clock);
                var result = await service.GetDashboardMetricsAsync();

                Assert.Equal(199000m + 499000m + 99000m, result.RevenueThisMonthVnd);
                Assert.Equal(99000m, result.RevenueTodayVnd);
                Assert.Equal(3, result.PaidOrdersThisMonthCount);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// #284: the month figure is <b>net</b>. Revenue is paid orders minus money refunds settled in the same
    /// window, so a month whose refunds exceed its income is a loss - and it is reported as one, not
    /// clamped to zero. A clamp would hide exactly the month an owner needs to read.
    /// <para>
    /// The credit refund is the control: it moved no money, so it changes neither figure. The money refund
    /// dated in September is the second control: it was settled before this window, so it is not October's
    /// loss even though it was requested in October.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Net_revenue_is_paid_orders_minus_money_refunds_settled_in_the_window_and_may_be_negative()
    {
        var dbName = $"EEDashNet_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
            var now = clock.GetUtcNow();

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner'), ('admin', 'Admin');
                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsLocked, EmailVerifiedAt, IsDeleted, CreatedAt)
                    VALUES
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'net@test.local', 0x01, N'Net User', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='admin'), 'netadmin@test.local', 0x01, N'Net Admin', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @UserId INT = (SELECT UserId FROM Users WHERE Email='net@test.local');
                    DECLARE @AdminId INT = (SELECT UserId FROM Users WHERE Email='netadmin@test.local');

                    INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
                    VALUES (N'NetPack', 5, 100000, 1, 0, N'Features', 1);
                    DECLARE @PkgId INT = SCOPE_IDENTITY();

                    -- 100,000 dong came in on the 5th.
                    INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES (@UserId, @PkgId, 'paid', 100000, 'DH-NET-0001', 40001, 'payos', '{now.AddDays(-10):yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddDays(-10):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- And 250,000 went back out, settled today: the day is a 250,000 loss and the month is
                    -- a 150,000 loss, because 100,000 of it came in ten days earlier.
                    INSERT INTO Refunds (UserId, Method, Reason, Status, Amount, RequestedByUserId, DecidedByUserId, DecidedAt, SettledByUserId, SettledAt, CreatedAt)
                    VALUES (@UserId, 'money', N'Service not delivered', 'settled', 250000, @AdminId, @AdminId,
                            '{now.AddDays(-8):yyyy-MM-dd HH:mm:ss.fffffff zzz}', @AdminId,
                            '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddDays(-8):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- A credit refund returns credits, not money: it must move neither revenue figure.
                    INSERT INTO Refunds (UserId, Method, Reason, Status, CreditCount, RequestedByUserId, DecidedByUserId, DecidedAt, CreatedAt)
                    VALUES (@UserId, 'credit', N'Make-good for a grading fault', 'approved', 5, @AdminId, @AdminId,
                            '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- Settled in September: not October's money, whatever month it was requested in. The
                    -- request date is inside the window on purpose, so a figure that read the request would
                    -- subtract this 50,000 and land on -200,000.
                    INSERT INTO Refunds (UserId, Method, Reason, Status, Amount, RequestedByUserId, DecidedByUserId, DecidedAt, SettledByUserId, SettledAt, CreatedAt)
                    VALUES (@UserId, 'money', N'Closed before the window', 'settled', 50000, @AdminId, @AdminId,
                            '2026-10-02 10:00:00 +00:00', @AdminId, '2026-09-29 10:00:00 +00:00', '2026-10-02 10:00:00 +00:00');

                    -- Settled yesterday: October's transfer, not today's.
                    INSERT INTO Refunds (UserId, Method, Reason, Status, Amount, RequestedByUserId, DecidedByUserId, DecidedAt, SettledByUserId, SettledAt, CreatedAt)
                    VALUES (@UserId, 'money', N'Refunded yesterday', 'settled', 30000, @AdminId, @AdminId,
                            '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}', @AdminId,
                            '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var service = CreateService(db, clock);
                var result = await service.GetDashboardMetricsAsync();

                // 100,000 in, 280,000 out (250,000 today + 30,000 yesterday): the month is a 180,000 loss.
                Assert.Equal(-180000m, result.RevenueThisMonthVnd);
                // The day holds the refund and none of the income: the deeper loss of the two, which is the
                // clearest proof that the two figures read two different windows.
                Assert.Equal(-250000m, result.RevenueTodayVnd);
                Assert.Equal(1, result.PaidOrdersThisMonthCount);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The <c>refund_credit</c> ledger row is a make-good in credits. It must land in the refunded-credits
    /// figure beside a booking cancellation, and it must leave both revenue figures alone because no money
    /// moved. Counting only <c>booking_cancel</c> - what the dashboard did before #284 - reports a figure
    /// that is wrong by every credit refund ever issued.
    /// </summary>
    [Fact]
    public async Task A_credit_refund_moves_the_refunded_credits_figure_and_not_revenue()
    {
        var dbName = $"EEDashCreditRefund_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero));
            var now = clock.GetUtcNow();

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner'), ('admin', 'Admin');
                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsLocked, EmailVerifiedAt, IsDeleted, CreatedAt)
                    VALUES
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'credref@test.local', 0x01, N'Credit Refund User', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='admin'), 'credrefadmin@test.local', 0x01, N'Credit Refund Admin', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @UserId INT = (SELECT UserId FROM Users WHERE Email='credref@test.local');
                    DECLARE @AdminId INT = (SELECT UserId FROM Users WHERE Email='credrefadmin@test.local');

                    -- A 50-credit opening balance, so the positive refund rows have room to post.
                    INSERT INTO CreditTransactions (UserId, AdminUserId, Reason, Delta, Note, CreatedAt)
                    VALUES (@UserId, @AdminId, 'admin_grant', 50, N'Opening balance', '{now.AddDays(-3):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- 5 credits given back by a refund. The figure must count it; revenue must not move,
                    -- because no money left the bank for it.
                    INSERT INTO CreditTransactions (UserId, AdminUserId, Reason, Delta, Note, CreatedAt)
                    VALUES (@UserId, @AdminId, 'refund_credit', 5, N'Grading fault', '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    INSERT INTO Refunds (UserId, Method, Reason, Status, CreditCount, RequestedByUserId, DecidedByUserId, DecidedAt, CreatedAt)
                    VALUES (@UserId, 'credit', N'Grading fault', 'approved', 5, @AdminId, @AdminId,
                            '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddDays(-1):yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var service = CreateService(db, clock);
                var result = await service.GetDashboardMetricsAsync();

                Assert.Equal(5, result.CreditsRefundedThisMonth);
                Assert.Equal(0m, result.RevenueThisMonthVnd);
                Assert.Equal(0m, result.RevenueTodayVnd);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Calculates_most_attempted_exams_top_5_with_average_scores()
    {
        var dbName = $"EEDashExams_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var now = _clock.GetUtcNow();

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner');
                    INSERT INTO Certificates (Code, Name, Description, IsActive) VALUES ('toeic', 'TOEIC', 'Desc', 1);
                    DECLARE @CertId INT = SCOPE_IDENTITY();

                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsLocked, EmailVerifiedAt, IsDeleted, CreatedAt)
                    VALUES ((SELECT RoleId FROM Roles WHERE Code='learner'), 'taker@test.local', 0x01, N'Exam Taker', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @UserId INT = SCOPE_IDENTITY();

                    -- Create 6 exams
                    INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, QuestionCount, IsDeleted, CreatedAt)
                    VALUES
                      (@CertId, 'Exam A (3 attempts, avg 800)', 'published', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      (@CertId, 'Exam B (4 attempts, avg 650)', 'published', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      (@CertId, 'Exam C (2 attempts, avg 700)', 'published', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      (@CertId, 'Exam D (5 attempts, avg 900)', 'published', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      (@CertId, 'Exam E (1 attempt, avg 500)', 'published', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      (@CertId, 'Exam F (0 completed attempts)', 'published', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    DECLARE @ExamA INT = (SELECT ExamId FROM Exams WHERE Name = 'Exam A (3 attempts, avg 800)');
                    DECLARE @ExamB INT = (SELECT ExamId FROM Exams WHERE Name = 'Exam B (4 attempts, avg 650)');
                    DECLARE @ExamC INT = (SELECT ExamId FROM Exams WHERE Name = 'Exam C (2 attempts, avg 700)');
                    DECLARE @ExamD INT = (SELECT ExamId FROM Exams WHERE Name = 'Exam D (5 attempts, avg 900)');
                    DECLARE @ExamE INT = (SELECT ExamId FROM Exams WHERE Name = 'Exam E (1 attempt, avg 500)');
                    DECLARE @ExamF INT = (SELECT ExamId FROM Exams WHERE Name = 'Exam F (0 completed attempts)');

                    -- Exam D: 5 graded attempts (scores 900, 900, 900, 900, 900)
                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, TotalScore, WrittenByService, ProjectionVersion)
                    VALUES
                      (@UserId, @ExamD, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 900, 'AttemptEngine', 1),
                      (@UserId, @ExamD, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 900, 'AttemptEngine', 1),
                      (@UserId, @ExamD, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 900, 'AttemptEngine', 1),
                      (@UserId, @ExamD, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 900, 'AttemptEngine', 1),
                      (@UserId, @ExamD, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 900, 'AttemptEngine', 1);

                    -- Exam B: 4 graded attempts (scores 600, 700, 600, 700) -> avg 650
                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, TotalScore, WrittenByService, ProjectionVersion)
                    VALUES
                      (@UserId, @ExamB, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 600, 'AttemptEngine', 1),
                      (@UserId, @ExamB, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 700, 'AttemptEngine', 1),
                      (@UserId, @ExamB, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 600, 'AttemptEngine', 1),
                      (@UserId, @ExamB, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 700, 'AttemptEngine', 1);

                    -- Exam A: 3 attempts (2 graded 750, 850; 1 submitted 800) -> avg 800
                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, TotalScore, WrittenByService, ProjectionVersion)
                    VALUES
                      (@UserId, @ExamA, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 750, 'AttemptEngine', 1),
                      (@UserId, @ExamA, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 850, 'AttemptEngine', 1),
                      (@UserId, @ExamA, @CertId, 'submitted', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 800, 'AttemptEngine', 1);

                    -- Exam C: 2 attempts (avg 700)
                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, TotalScore, WrittenByService, ProjectionVersion)
                    VALUES
                      (@UserId, @ExamC, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 650, 'AttemptEngine', 1),
                      (@UserId, @ExamC, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 750, 'AttemptEngine', 1);

                    -- Exam E: 1 attempt (avg 500)
                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, TotalScore, WrittenByService, ProjectionVersion)
                    VALUES
                      (@UserId, @ExamE, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 500, 'AttemptEngine', 1);

                    -- Exam F: in_progress and abandoned (not completed -> not counted)
                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, TotalScore, WrittenByService, ProjectionVersion)
                    VALUES
                      (@UserId, @ExamF, @CertId, 'in_progress', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', NULL, 'AttemptEngine', 1),
                      (@UserId, @ExamF, @CertId, 'abandoned', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', NULL, 'AttemptEngine', 1);
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var service = CreateService(db);
                var result = await service.GetDashboardMetricsAsync();

                // Top 5 exams capped
                Assert.Equal(5, result.MostAttemptedExams.Count);

                // Rank 1: Exam D (5 attempts, avg 900)
                Assert.Equal("Exam D (5 attempts, avg 900)", result.MostAttemptedExams[0].ExamName);
                Assert.Equal(5, result.MostAttemptedExams[0].AttemptsCount);
                Assert.Equal(900m, result.MostAttemptedExams[0].AverageScore);

                // Rank 2: Exam B (4 attempts, avg 650)
                Assert.Equal("Exam B (4 attempts, avg 650)", result.MostAttemptedExams[1].ExamName);
                Assert.Equal(4, result.MostAttemptedExams[1].AttemptsCount);
                Assert.Equal(650m, result.MostAttemptedExams[1].AverageScore);

                // Rank 3: Exam A (3 attempts, avg 800)
                Assert.Equal("Exam A (3 attempts, avg 800)", result.MostAttemptedExams[2].ExamName);
                Assert.Equal(3, result.MostAttemptedExams[2].AttemptsCount);
                Assert.Equal(800m, result.MostAttemptedExams[2].AverageScore);

                // Rank 4: Exam C (2 attempts, avg 700)
                Assert.Equal("Exam C (2 attempts, avg 700)", result.MostAttemptedExams[3].ExamName);
                Assert.Equal(2, result.MostAttemptedExams[3].AttemptsCount);
                Assert.Equal(700m, result.MostAttemptedExams[3].AverageScore);

                // Rank 5: Exam E (1 attempt, avg 500)
                Assert.Equal("Exam E (1 attempt, avg 500)", result.MostAttemptedExams[4].ExamName);
                Assert.Equal(1, result.MostAttemptedExams[4].AttemptsCount);
                Assert.Equal(500m, result.MostAttemptedExams[4].AverageScore);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Calculates_needs_attention_alerts_correctly()
    {
        var dbName = $"EEDashAttention_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
            var now = clock.GetUtcNow();

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner');
                    INSERT INTO Certificates (Code, Name, Description, IsActive) VALUES ('toeic', 'TOEIC', 'Desc', 1);
                    DECLARE @CertId INT = SCOPE_IDENTITY();

                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsLocked, EmailVerifiedAt, IsDeleted, CreatedAt)
                    VALUES
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'learner1@test.local', 0x01, N'Alert User 1', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'learner2@test.local', 0x01, N'Alert User 2', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'learner3@test.local', 0x01, N'Alert User 3', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    DECLARE @UserId1 INT = (SELECT UserId FROM Users WHERE Email='learner1@test.local');
                    DECLARE @UserId2 INT = (SELECT UserId FROM Users WHERE Email='learner2@test.local');
                    DECLARE @UserId3 INT = (SELECT UserId FROM Users WHERE Email='learner3@test.local');

                    INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
                    VALUES (N'Pkg1', 5, 199000, 1, 0, N'Features', 1);
                    DECLARE @PkgId INT = SCOPE_IDENTITY();

                    -- 1. Exams: 2 draft exams, 1 published, 1 draft deleted (deleted must be excluded)
                    INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, QuestionCount, IsDeleted, CreatedAt)
                    VALUES
                      (@CertId, 'Draft 1', 'draft', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      (@CertId, 'Draft 2', 'draft', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      (@CertId, 'Published 1', 'published', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      (@CertId, 'Draft Deleted', 'draft', 120, 'mock', 'Desc', 200, 1, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @ExamId INT = (SELECT ExamId FROM Exams WHERE Name='Published 1');

                    -- 2. Orders: 2 stale pending (> 30 min, distinct users for UX_Orders_OnePendingPerUser), 1 fresh pending (< 30 min), 1 paid
                    INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway, CreatedAt, PaidAt)
                    VALUES
                      (@UserId1, @PkgId, 'pending', 199000, 'DH-STALE-001', 20001, 'payos', '{now.AddMinutes(-45):yyyy-MM-dd HH:mm:ss.fffffff zzz}', NULL),
                      (@UserId2, @PkgId, 'pending', 199000, 'DH-STALE-002', 20002, 'mock', '{now.AddMinutes(-60):yyyy-MM-dd HH:mm:ss.fffffff zzz}', NULL),
                      (@UserId3, @PkgId, 'pending', 199000, 'DH-FRESH-001', 20003, 'payos', '{now.AddMinutes(-10):yyyy-MM-dd HH:mm:ss.fffffff zzz}', NULL),
                      (@UserId1, @PkgId, 'paid', 199000, 'DH-PAID-001', 20004, 'payos', '{now.AddMinutes(-40):yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddMinutes(-39):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    -- 3. Attempts for complaints FK
                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
                    VALUES (@UserId1, @ExamId, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @AttemptId INT = SCOPE_IDENTITY();

                    -- 4. Complaints: 1 open, 1 reviewing, 1 resolved (resolved must be excluded, Type matches subject AttemptId)
                    INSERT INTO Complaints (UserId, AttemptId, Type, Reason, Status, CreatedAt, ResolvedAt)
                    VALUES
                      (@UserId1, @AttemptId, 'technical_error', N'App crashed on audio', 'open', '{now.AddHours(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}', NULL),
                      (@UserId1, @AttemptId, 'grading_issue', N'Exam timer stuck', 'reviewing', '{now.AddHours(-3):yyyy-MM-dd HH:mm:ss.fffffff zzz}', NULL),
                      (@UserId1, @AttemptId, 'technical_error', N'Fixed issue', 'resolved', '{now.AddHours(-10):yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now.AddHours(-9):yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var service = CreateService(db, clock);
                var result = await service.GetDashboardMetricsAsync();

                Assert.Equal(2, result.NeedsAttention.DraftExamsCount);
                Assert.Equal(2, result.NeedsAttention.StalePendingOrdersCount);
                Assert.Equal(2, result.NeedsAttention.OpenComplaintsCount);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Calculates_month_boundary_and_leap_year_safely()
    {
        var dbName = $"EEDashLeap_{Guid.NewGuid():N}"[..25];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            // Leap year: 2028-02-29, at 19:00 Vietnam time so the accountant's calendar agrees with the
            // instant. 23:59:59Z would be 1 March locally and this test would no longer be about February.
            var leapClock = new ManualTimeProvider(new DateTimeOffset(2028, 2, 29, 12, 0, 0, TimeSpan.Zero));
            var now = leapClock.GetUtcNow();

            await using (var db = DbContextFactory.Create(connStr))
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner'), ('admin', 'Admin');
                    INSERT INTO Certificates (Code, Name, Description, IsActive) VALUES ('toeic', 'TOEIC', 'Desc', 1);
                    DECLARE @CertId INT = SCOPE_IDENTITY();

                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, IsLocked, EmailVerifiedAt, IsDeleted, CreatedAt)
                    VALUES
                      ((SELECT RoleId FROM Roles WHERE Code='learner'), 'leap@test.local', 0x01, N'Leap User', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}'),
                      ((SELECT RoleId FROM Roles WHERE Code='admin'), 'admin@test.local', 0x01, N'Admin', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @UserId INT = (SELECT UserId FROM Users WHERE Email='leap@test.local');
                    DECLARE @AdminId INT = (SELECT UserId FROM Users WHERE Email='admin@test.local');

                    -- Fund the user first with 50 credits so debit doesn't drive balance negative
                    INSERT INTO CreditTransactions (UserId, AdminUserId, Reason, Delta, Note, CreatedAt)
                    VALUES (@UserId, @AdminId, 'admin_grant', 50, N'Fund account', '{now.AddDays(-2):yyyy-MM-dd HH:mm:ss.fffffff zzz}');

                    INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, QuestionCount, IsDeleted, CreatedAt)
                    VALUES (@CertId, 'Leap Exam', 'published', 120, 'mock', 'Desc', 200, 0, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @ExamId INT = SCOPE_IDENTITY();

                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
                    VALUES (@UserId, @ExamId, @CertId, 'graded', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}', '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    DECLARE @AttemptId INT = SCOPE_IDENTITY();

                    -- Debit 29 credits on Feb 29
                    INSERT INTO CreditTransactions (UserId, AttemptId, Reason, Delta, CreatedAt)
                    VALUES (@UserId, @AttemptId, 'mock_exam_start', -29, '{now:yyyy-MM-dd HH:mm:ss.fffffff zzz}');
                    """);
            }

            await using (var db = DbContextFactory.Create(connStr))
            {
                var service = CreateService(db, leapClock);
                var result = await service.GetDashboardMetricsAsync();

                Assert.Equal(29, result.CreditsUsedThisMonth);
                // 29 credits / 29 days in leap year Feb = 1.0 credits/day
                Assert.Equal(1.0m, result.CreditsPerDayAverage);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }
}
