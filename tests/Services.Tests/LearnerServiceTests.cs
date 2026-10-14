using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class LearnerServiceTests(DatabaseFixture database)
{
    // The database is shared across the run, so every test seeds its own certificates under
    // unique codes and its own learner under a unique email.
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private LearnerService CreateService() => new(DbContextFactory.Create(database.ConnectionString));

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>A certificate with one listening section, a published mock exam and two bands.</summary>
    private async Task SeedCertificateAsync(string code, bool isActive = true)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Cert {code}', {(isActive ? 1 : 0)});
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality) VALUES (@cert, 'LIST', N'Listening', 'listening');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name)
              VALUES (SCOPE_IDENTITY(), @cert, 'listening', 'P1', N'Photographs {code}');
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low band', 0, 499, 1),
              (@cert, 'HIGH', N'High band', 500, 990, 2);
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
              VALUES (@cert, N'Mock {code}', 'published', 120, 'mock');
            """);
    }

    private Task SeedGradedAttemptAsync(int userId, string certCode, int total, string band, string startedAt) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
            VALUES ({userId}, (SELECT ExamId FROM Exams WHERE CertId = @cert), @cert, 'graded',
                    '{startedAt}', DATEADD(HOUR, 2, CAST('{startedAt}' AS DATETIMEOFFSET)),
                    DATEADD(HOUR, 1, CAST('{startedAt}' AS DATETIMEOFFSET)), {total}, '{band}');
            """);

    private Task SeedPracticeAsync(int userId, string certCode, int correct, int count) =>
        database.SqlAsync($"""
            INSERT INTO PracticeSessions (UserId, SectionId, Modality, QuestionCount, Status, CorrectCount, FinishedAt)
            VALUES ({userId}, (SELECT SectionId FROM Sections WHERE CertId = (SELECT CertId FROM Certificates WHERE Code = '{certCode}')),
                    'listening', {count}, 'finished', {correct}, SYSDATETIMEOFFSET());
            """);

    private Task SeedInProgressAttemptAsync(int userId, string certCode, string startedAt) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
            VALUES ({userId}, (SELECT ExamId FROM Exams WHERE CertId = @cert), @cert, 'in_progress',
                    '{startedAt}', DATEADD(HOUR, 2, CAST('{startedAt}' AS DATETIMEOFFSET)));
            """);

    private Task SeedInProgressPracticeAsync(int userId, string certCode, int count) =>
        database.SqlAsync($"""
            INSERT INTO PracticeSessions (UserId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt)
            VALUES ({userId}, (SELECT SectionId FROM Sections WHERE CertId = (SELECT CertId FROM Certificates WHERE Code = '{certCode}')),
                    'listening', {count}, 'in_progress', 0, SYSDATETIMEOFFSET());
            """);

    [Fact]
    public async Task Switch_persists_the_active_certificate_for_that_user()
    {
        var toeic = Unique("T");
        var ielts = Unique("I");
        await SeedCertificateAsync(toeic);
        await SeedCertificateAsync(ielts);
        var userId = await SeedLearnerAsync(toeic);

        await CreateService().SwitchCertificateAsync(userId, ielts);

        // A fresh context reads the stored choice, not anything cached by the first.
        var context = await CreateService().GetContextAsync(userId);
        Assert.Equal(ielts, context.Active.Code);
        Assert.Contains(context.Certificates, c => c.Code == toeic);
    }

    [Fact]
    public async Task Switch_matches_the_code_case_insensitively()
    {
        var code = Unique("C");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync();

        await CreateService().SwitchCertificateAsync(userId, code.ToLowerInvariant());

        Assert.Equal(code, (await CreateService().GetContextAsync(userId)).Active.Code);
    }

    [Fact]
    public async Task Switch_refuses_an_unknown_or_retired_certificate_and_keeps_the_old_choice()
    {
        var live = Unique("L");
        var retired = Unique("R");
        await SeedCertificateAsync(live);
        await SeedCertificateAsync(retired, isActive: false);
        var userId = await SeedLearnerAsync(live);

        await Assert.ThrowsAsync<CertificateUnavailableException>(
            () => CreateService().SwitchCertificateAsync(userId, retired));
        await Assert.ThrowsAsync<CertificateUnavailableException>(
            () => CreateService().SwitchCertificateAsync(userId, "NO-SUCH-CERT"));

        Assert.Equal(live, (await CreateService().GetContextAsync(userId)).Active.Code);
    }

    [Fact]
    public async Task A_learner_who_never_chose_gets_the_first_active_certificate()
    {
        await SeedCertificateAsync(Unique("D"));
        var userId = await SeedLearnerAsync();

        var context = await CreateService().GetContextAsync(userId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var active = await db.Certificates.Where(c => c.IsActive).OrderBy(c => c.CertId).Select(c => c.Code).ToListAsync();
        Assert.Equal(active[0], context.Active.Code);
        Assert.Equal(active, context.Certificates.Select(c => c.Code));
    }

    [Fact]
    public async Task Switch_refuses_a_blank_code_as_a_validation_error()
    {
        var code = Unique("V");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        await Assert.ThrowsAsync<ValidationException>(() => CreateService().SwitchCertificateAsync(userId, "  "));

        Assert.Equal(code, (await CreateService().GetContextAsync(userId)).Active.Code);
    }

    [Fact]
    public async Task The_first_fallback_is_saved_so_later_catalog_changes_do_not_move_the_learner()
    {
        var userId = await SeedLearnerAsync();
        var first = (await CreateService().GetContextAsync(userId)).Active.Code;

        // A certificate added later (with a higher id) or reordering must not change what this
        // learner sees: the fallback became their stored choice on first use.
        await SeedCertificateAsync(Unique("Z"));
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.Users.Where(u => u.UserId == userId).Select(u => u.ActiveCert!.Code).SingleAsync();

        Assert.Equal(first, stored);
    }

    [Fact]
    public async Task A_stored_choice_that_was_retired_falls_back_to_an_active_certificate()
    {
        var code = Unique("X");
        await SeedCertificateAsync(code);
        await SeedCertificateAsync(Unique("Y"));
        var userId = await SeedLearnerAsync(code);
        await database.SqlAsync($"UPDATE Certificates SET IsActive = 0 WHERE Code = '{code}';");

        var context = await CreateService().GetContextAsync(userId);

        Assert.NotEqual(code, context.Active.Code);
    }

    [Fact]
    public async Task Context_reports_the_ledger_balance_and_zero_for_an_empty_ledger()
    {
        var code = Unique("B");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        var adminId = await SeedLearnerAsync();

        Assert.Equal(0, (await CreateService().GetContextAsync(userId)).CreditBalance);

        await database.SqlAsync($"""
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note) VALUES
              ({userId}, 'admin_grant', 10, {adminId}, N'test grant');
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note) VALUES
              ({userId}, 'admin_revoke', -3, {adminId}, N'test revoke');
            """);

        Assert.Equal(7, (await CreateService().GetContextAsync(userId)).CreditBalance);
    }

    [Fact]
    public async Task Dashboard_for_a_new_learner_is_empty()
    {
        var code = Unique("N");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.True(dashboard.IsNew);
        Assert.Null(dashboard.LatestResult);
        Assert.Empty(dashboard.RecentAttempts);
        Assert.Empty(dashboard.RecentPractice);
    }

    [Fact]
    public async Task Dashboard_shows_only_the_active_certificate_history()
    {
        var toeic = Unique("T");
        var ielts = Unique("I");
        await SeedCertificateAsync(toeic);
        await SeedCertificateAsync(ielts);
        var userId = await SeedLearnerAsync(toeic);
        await SeedGradedAttemptAsync(userId, toeic, 450, "LOW", "2026-09-01T09:00:00+07:00");
        await SeedGradedAttemptAsync(userId, toeic, 620, "HIGH", "2026-09-10T09:00:00+07:00");
        await SeedPracticeAsync(userId, toeic, correct: 8, count: 10);
        await SeedGradedAttemptAsync(userId, ielts, 300, "LOW", "2026-09-20T09:00:00+07:00");

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(toeic, dashboard.Certificate.Code);
        Assert.False(dashboard.IsNew);
        Assert.Equal(2, dashboard.AttemptCount);
        Assert.Equal(1, dashboard.PracticeCount);
        // Newest first, and the IELTS attempt (the newest overall) never appears.
        Assert.Equal(new[] { 620, 450 }, dashboard.RecentAttempts.Select(a => a.TotalScore!.Value));
        Assert.All(dashboard.RecentAttempts, a => Assert.Equal($"Mock {toeic}", a.ExamName));
        Assert.Equal($"Photographs {toeic}", Assert.Single(dashboard.RecentPractice).SectionName);

        var latest = dashboard.LatestResult!;
        Assert.Equal(620, latest.TotalScore);
        Assert.Equal("HIGH", latest.BandCode);
        Assert.Equal("High band", latest.BandName);

        await CreateService().SwitchCertificateAsync(userId, ielts);
        var switched = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(300, Assert.Single(switched.RecentAttempts).TotalScore);
        Assert.Empty(switched.RecentPractice);
        Assert.Equal(300, switched.LatestResult!.TotalScore);
    }

    [Fact]
    public async Task Dashboard_keeps_other_learners_out()
    {
        var code = Unique("O");
        await SeedCertificateAsync(code);
        var mine = await SeedLearnerAsync(code);
        var theirs = await SeedLearnerAsync(code);
        await SeedGradedAttemptAsync(theirs, code, 700, "HIGH", "2026-09-05T09:00:00+07:00");
        await SeedPracticeAsync(theirs, code, correct: 5, count: 5);

        var dashboard = await CreateService().GetDashboardAsync(mine);

        Assert.True(dashboard.IsNew);
    }

    [Fact]
    public async Task Dashboard_surfaces_unfinished_attempt_and_practice()
    {
        var code = Unique("U");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        await SeedInProgressAttemptAsync(userId, code, "2026-10-01T10:00:00+07:00");
        await SeedInProgressPracticeAsync(userId, code, count: 20);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.NotNull(dashboard.UnfinishedAttempt);
        Assert.Equal("in_progress", dashboard.UnfinishedAttempt.Status);
        Assert.Equal($"Mock {code}", dashboard.UnfinishedAttempt.ExamName);

        Assert.NotNull(dashboard.UnfinishedPractice);
        Assert.Equal("in_progress", dashboard.UnfinishedPractice.Status);
        Assert.Equal($"Photographs {code}", dashboard.UnfinishedPractice.SectionName);
        Assert.Equal(20, dashboard.UnfinishedPractice.QuestionCount);
    }
}
