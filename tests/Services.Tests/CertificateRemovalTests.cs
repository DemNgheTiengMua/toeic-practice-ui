using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// Removing a certificate is two different acts wearing one word, and the difference is whether anyone
/// has depended on it.
/// <para>
/// <b>The input that would pass a weaker test:</b> a certificate nobody has touched, where "delete"
/// obviously means delete. Every case below therefore seeds a certificate the way the product reaches
/// it — a learner with it active, an attempt against it, a ledger-carrying placement — because that is
/// where destroying the row would either fail on a foreign key or silently orphan a learner's history.
/// </para>
/// <para>
/// <b>Why the learner's active certificate is asserted and not assumed:</b> <c>Users.ActiveCertId</c>
/// points at the certificate being removed. A removal that leaves it there strands the learner on a
/// certificate no catalogue offers, and the failure shows up later, on a page that did nothing wrong.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CertificateRemovalTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    private static async Task<(int CertId, string Code)> AddCertificateAsync(
        CompanionDbContext db, string code, bool isActive)
    {
        var cert = new Certificate
        {
            Code = code, Name = $"Cert {code}", IsActive = isActive, ScoreAggregationRule = "sum",
            SectionNoun = "Section",
        };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();
        return (cert.CertId, cert.Code);
    }

    private async Task<int> AddLearnerOnAsync(CompanionDbContext db, int certId)
    {
        if (!await db.Roles.AnyAsync(r => r.Code == "learner"))
        {
            db.Roles.Add(new Role { Code = "learner", Name = "Learner" });
            await db.SaveChangesAsync();
        }
        var roleId = await db.Roles.Where(r => r.Code == "learner").Select(r => r.RoleId).SingleAsync();
        var email = $"{Guid.NewGuid():N}@test.local";
        db.Users.Add(new User { RoleId = roleId, Email = email, PasswordHash = [0x01], ActiveCertId = certId });
        await db.SaveChangesAsync();
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    [Fact]
    public async Task An_untouched_certificate_is_deleted_outright()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var (keepId, _) = await AddCertificateAsync(db, Unique("KEEP"), isActive: true);
        var (dropId, dropCode) = await AddCertificateAsync(db, Unique("DROP"), isActive: true);

        var result = await new CertificateService(db).RemoveCertificateAsync(dropId);

        Assert.True(result.Deleted);
        Assert.Equal(0, result.LearnersReassigned);
        Assert.Null(result.ReassignedToCode);

        await using var verify = DbContextFactory.Create(database.ConnectionString);
        Assert.False(await verify.Certificates.AnyAsync(c => c.CertId == dropId));
        Assert.True(await verify.Certificates.AnyAsync(c => c.CertId == keepId));
    }

    [Fact]
    public async Task A_certificate_with_a_learner_attempt_is_withdrawn_and_the_learner_moved()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        // Other tests share this database, so the fallback is whichever active certificate the service
        // picks first — asserted as "one of the other active ones", not as a code this test guessed.
        var (usedId, _) = await AddCertificateAsync(db, Unique("USED"), isActive: true);

        var learnerId = await AddLearnerOnAsync(db, usedId);

        // An attempt is what makes the certificate part of somebody's record, so it needs a real exam
        // row to point at — a dangling ExamId is refused before the removal is ever reached.
        var exam = new Exam
        {
            CertId = usedId, Name = $"Exam {Unique("E")}", Status = "published", DurationMinutes = 60,
            Purpose = "mock", QuestionCount = 1,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        db.ExamAttempts.Add(new ExamAttempt
        {
            UserId = learnerId, CertId = usedId, ExamId = exam.ExamId, Status = "graded",
            StartedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        });
        await db.SaveChangesAsync();

        var expectedFallback = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId != usedId && c.IsActive)
            .OrderBy(c => c.CertId)
            .Select(c => new { c.CertId, c.Code })
            .FirstAsync();

        var result = await new CertificateService(db).RemoveCertificateAsync(usedId);

        Assert.False(result.Deleted);
        Assert.Equal(1, result.LearnersReassigned);
        Assert.Equal(expectedFallback.Code, result.ReassignedToCode);

        await using var verify = DbContextFactory.Create(database.ConnectionString);
        // Withdrawn, not destroyed: the row and the attempt are both still there.
        var cert = await verify.Certificates.SingleAsync(c => c.CertId == usedId);
        Assert.False(cert.IsActive);
        Assert.True(await verify.ExamAttempts.AnyAsync(a => a.CertId == usedId));

        // And the learner is no longer pointed at it.
        var learner = await verify.Users.SingleAsync(u => u.UserId == learnerId);
        Assert.Equal(expectedFallback.CertId, learner.ActiveCertId);
    }

    [Fact]
    public async Task A_certificate_held_only_by_a_goal_is_withdrawn()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        await AddCertificateAsync(db, Unique("OTHER"), isActive: true);
        var (goalCertId, _) = await AddCertificateAsync(db, Unique("GOAL"), isActive: true);
        var learnerId = await AddLearnerOnAsync(db, goalCertId);

        // A goal alone is history: a composite key carrying CertId hangs off it. The band codes it names
        // are real rows of that certificate, because a goal cannot point at a band nobody defined.
        db.ScoreBands.AddRange(
            new ScoreBand { CertId = goalCertId, Code = "B1", Name = "Intermediate", MinTotal = 0, MaxTotal = 50, DisplayOrder = 1 },
            new ScoreBand { CertId = goalCertId, Code = "B2", Name = "Upper", MinTotal = 51, MaxTotal = 100, DisplayOrder = 2 });
        await db.SaveChangesAsync();

        db.TargetBands.Add(new TargetBand
        {
            UserId = learnerId, CertId = goalCertId, CurrentBandCode = "B1", TargetBandCode = "B2",
            SetAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await new CertificateService(db).RemoveCertificateAsync(goalCertId);

        Assert.False(result.Deleted);
        await using var verify = DbContextFactory.Create(database.ConnectionString);
        Assert.False((await verify.Certificates.SingleAsync(c => c.CertId == goalCertId)).IsActive);
    }

    [Fact]
    public async Task The_last_active_certificate_cannot_be_removed()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        // Deactivate everything else, so exactly one active certificate remains.
        var active = await db.Certificates.Where(c => c.IsActive).ToListAsync();
        Assert.NotEmpty(active);
        foreach (var cert in active.Skip(1))
        {
            cert.IsActive = false;
        }
        await db.SaveChangesAsync();

        var lastId = active[0].CertId;
        var ex = await Assert.ThrowsAsync<CertificateRemovalException>(
            () => new CertificateService(db).RemoveCertificateAsync(lastId));

        Assert.Equal(CertificateRemovalException.LastActiveCertificate, ex.Code);

        await using var verify = DbContextFactory.Create(database.ConnectionString);
        Assert.True((await verify.Certificates.SingleAsync(c => c.CertId == lastId)).IsActive);
    }
}
