using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// Tier 2: Boundary & Corner Cases Test Suite for Services & Persistence.
/// Exercises extreme inputs, invalid IDs, trigger check constraints,
/// append-only ledger invariants, and Vietnamese Unicode roundtrips in the database.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class Tier2ServicesBoundaryTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private AttemptEngine CreateEngine()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new AttemptEngine(db);
    }

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null)
    {
        var email = $"{Guid.NewGuid():N}@boundary.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Boundary Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    [Fact]
    public async Task CreateAttempt_With_NonExistent_ExamId_Throws_DbUpdateException()
    {
        var learnerId = await SeedLearnerAsync();
        var engine = CreateEngine();

        // Foreign key FK_Attempts_ExamCert fails on non-existent exam ID
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() =>
            engine.CreateAttemptAsync(learnerId, certId: 99999, examId: 99999, durationMinutes: 120));

        Assert.NotNull(ex.InnerException);
        Assert.IsType<SqlException>(ex.InnerException);
    }

    [Fact]
    public async Task SaveDraft_With_NonExistent_AttemptId_Throws_AttemptEngineException_NotFound()
    {
        var learnerId = await SeedLearnerAsync();
        var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveDraftAsync(attemptId: 999999, learnerId, questionId: 1, responseText: "Draft text"));

        Assert.Equal(AttemptEngineException.AttemptNotFound, ex.Code);
    }

    [Fact]
    public async Task CreditTransactions_Trigger_Blocks_Negative_Balance()
    {
        var learnerId = await SeedLearnerAsync();

        // Attempting to deduct credits when balance is 0 must fail via trg_CreditTransactions_NoNegativeBalance
        await Assert.ThrowsAsync<SqlException>(async () =>
        {
            await using var conn = new SqlConnection(database.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"""
                INSERT INTO CreditTransactions (UserId, Reason, Delta, Note)
                VALUES ({learnerId}, 'mock_exam_start', -5, N'Illegal debit on zero balance');
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        });
    }

    [Fact]
    public async Task CreditTransactions_Trigger_Blocks_Update_And_Delete_Enforcing_AppendOnly()
    {
        var learnerId = await SeedLearnerAsync();

        // Grant 10 credits
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@test.local', 0x01, N'Admin');
            DECLARE @adminId INT = SCOPE_IDENTITY();
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES ({learnerId}, 'admin_grant', 10, @adminId, N'Initial grant');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var txn = await db.CreditTransactions.Where(t => t.UserId == learnerId).FirstAsync();

        // Attempting UPDATE must fail via trg_CreditTransactions_NoRewrite or trg_CreditTransactions_AppendOnly
        await Assert.ThrowsAsync<SqlException>(async () =>
        {
            await using var conn = new SqlConnection(database.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"UPDATE CreditTransactions SET Delta = 20 WHERE TxnId = {txn.TxnId};", conn);
            await cmd.ExecuteNonQueryAsync();
        });

        // Attempting DELETE must fail via trg_CreditTransactions_AppendOnly
        await Assert.ThrowsAsync<SqlException>(async () =>
        {
            await using var conn = new SqlConnection(database.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"DELETE FROM CreditTransactions WHERE TxnId = {txn.TxnId};", conn);
            await cmd.ExecuteNonQueryAsync();
        });
    }

    [Fact]
    public async Task Vietnamese_Diacritical_Text_Stored_In_Database_Preserves_Fidelity()
    {
        var certCode = Unique("VN");
        var vietnameseExamName = "Kỳ thi thử TOEIC Nâng Cao: Phần Nghe & Đọc Hiểu 2026 (Có giải thích chi tiết)";

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Chứng chỉ {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Exams (CertId, Purpose, Name, DurationMinutes, Status, IsDeleted)
            VALUES (@cert, 'mock', N'{vietnameseExamName}', 120, 'published', 0);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);

        Assert.Equal(vietnameseExamName, exam.Name);

        // Assert zero mojibake corruption
        string[] mojibakeMarkers = ["Ã‚", "Ã¡", "Ã\xa0", "Ã£", "Ã©", "Ã¨", "áº", "á»", "Æ°", "Äƒ", "Ä‘"];
        foreach (var marker in mojibakeMarkers)
        {
            Assert.DoesNotContain(marker, exam.Name);
        }
    }
}
