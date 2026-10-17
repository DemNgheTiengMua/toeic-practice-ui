using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Services;
using Services.Credits;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class PremiumPathServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private PremiumPathService CreateService(CompanionDbContext db)
    {
        var ledger = new CreditLedger(db, TimeProvider.System);
        return new PremiumPathService(db, ledger);
    }

    private async Task<int> SeedLearnerAsync(int startingCredits = 0)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner', SYSDATETIMEOFFSET());
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        if (startingCredits > 0)
        {
            await database.SqlAsync($"""
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
                  INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@test.local', 0x01, N'Admin', SYSDATETIMEOFFSET());
                DECLARE @adminId INT = SCOPE_IDENTITY();
                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
                VALUES ({userId}, 'admin_grant', {startingCredits}, @adminId, N'Test setup credits');
                """);
        }

        return userId;
    }

    private async Task<int> SeedPathFixtureAsync(int userId)
    {
        var certCode = Unique("CERT");
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Premium Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'PREM', N'Premium Skill', 'reading', 1);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, '000', N'Zero', 0, 100, 1),
              (@cert, '990', N'Max', 101, 990, 2);

            INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
            VALUES (@cert, '000', '990', N'Premium Template');
            DECLARE @template INT = SCOPE_IDENTITY();

            INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title)
            VALUES (@template, @skill, @cert, 1, N'Premium Module');
            DECLARE @module INT = SCOPE_IDENTITY();

            INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, DisplayOrder)
            VALUES (@module, @cert, @template, 'mentor', 1);

            INSERT INTO LearnerPaths (UserId, CertId, TemplateId, Status, StartedAt)
            VALUES ({userId}, @cert, @template, 'active', SYSDATETIMEOFFSET());
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.LearnerPaths.Where(p => p.UserId == userId).Select(p => p.LearnerPathId).SingleAsync();
    }

    [Fact]
    public async Task UnlockPathAsync_WithSufficientCredits_ChargesFiveCredits()
    {
        var userId = await SeedLearnerAsync(startingCredits: 10);
        var pathId = await SeedPathFixtureAsync(userId);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = CreateService(db);

        await service.UnlockPathAsync(userId, pathId);

        var balance = await db.CreditTransactions
            .Where(t => t.UserId == userId)
            .SumAsync(t => t.Delta);
        Assert.Equal(5, balance); // 10 granted - 5 charged

        var charge = await db.CreditTransactions
            .Where(t => t.UserId == userId && t.Reason == "premium_path" && t.Delta == -5)
            .SingleOrDefaultAsync();
        Assert.NotNull(charge);
    }

    [Fact]
    public async Task UnlockPathAsync_IsIdempotent_DoesNotChargeTwice()
    {
        var userId = await SeedLearnerAsync(startingCredits: 10);
        var pathId = await SeedPathFixtureAsync(userId);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = CreateService(db);

        await service.UnlockPathAsync(userId, pathId);
        await service.UnlockPathAsync(userId, pathId); // Second call should not charge

        var balance = await db.CreditTransactions
            .Where(t => t.UserId == userId)
            .SumAsync(t => t.Delta);
        Assert.Equal(5, balance); // Only charged once

        var charges = await db.CreditTransactions
            .Where(t => t.UserId == userId && t.Reason == "premium_path")
            .CountAsync();
        Assert.Equal(1, charges);
    }

    [Fact]
    public async Task UnlockPathAsync_WithInsufficientCredits_ThrowsInvalidOperation()
    {
        var userId = await SeedLearnerAsync(startingCredits: 3);
        var pathId = await SeedPathFixtureAsync(userId);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UnlockPathAsync(userId, pathId));

        Assert.Contains("Insufficient credits", ex.Message);
    }

    [Fact]
    public async Task IsPathUnlockedAsync_BeforeUnlock_ReturnsFalse()
    {
        var userId = await SeedLearnerAsync(startingCredits: 10);
        var pathId = await SeedPathFixtureAsync(userId);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = CreateService(db);

        var unlocked = await service.IsPathUnlockedAsync(userId, pathId);

        Assert.False(unlocked);
    }

    [Fact]
    public async Task IsPathUnlockedAsync_AfterUnlock_ReturnsTrue()
    {
        var userId = await SeedLearnerAsync(startingCredits: 10);
        var pathId = await SeedPathFixtureAsync(userId);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = CreateService(db);

        await service.UnlockPathAsync(userId, pathId);
        var unlocked = await service.IsPathUnlockedAsync(userId, pathId);

        Assert.True(unlocked);
    }
}
