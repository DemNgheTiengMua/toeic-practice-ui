using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #246.2: <b>completed mock attempts are preserved and tagged with their epoch, and are read-only
/// thereafter.</b>
/// <para>
/// The ticket asks for two things of a preserved row, and the second is the one that is easy to fake.
/// A tag is a label; "read-only" is a rule. D23 states why: a preserved attempt's score was produced by
/// a scoring model this build no longer has, so any recomputation would produce a different number from
/// the same answers and the learner's record would carry two answers forever. This file proves the rule
/// is the database's, not a convention a service remembers.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> A test that only asserts the tag round-trips
/// passes on a schema where every write is allowed. Each refusal below names the error number, so a
/// trigger that refused with a different reason - or refused everything, which is how the first draft of
/// this trigger broke ten other schema tests - would not satisfy it.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreservedAttemptTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task A_preserved_attempt_carries_its_epoch_and_cannot_be_written_again()
    {
        var name = $"EE246_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var (userId, examId, certId) = await SeedAsync(connectionString);

            // 1. The import's own act: tag a finished attempt. This is the ONE permitted write to a
            //    preserved row, and it must succeed - otherwise the cutover cannot happen at all.
            int attemptId;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                var attempt = new BusinessObjects.Entities.ExamAttempt
                {
                    UserId = userId,
                    ExamId = examId,
                    CertId = certId,
                    Status = "graded",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
                    SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                    TotalScore = 720,
                    BandCode = "B1",
                    WrittenByService = DerivedFactWriters.AttemptScoreAndBand,
                    ProjectionVersion = 1,
                };
                db.ExamAttempts.Add(attempt);
                await db.SaveChangesAsync();
                attemptId = attempt.AttemptId;
            }

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
                attempt.EpochTag = "legacy_v1";
                await db.SaveChangesAsync();
            }

            await using (var verify = DbContextFactory.Create(connectionString))
            {
                var stored = await verify.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
                Assert.Equal("legacy_v1", stored.EpochTag);
                // The score is the one the old model produced, untouched.
                Assert.Equal(720, stored.TotalScore);
            }

            // 2. Every later write is refused, by the database, with its own error number.
            await using (var db = DbContextFactory.Create(connectionString))
            {
                var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
                attempt.TotalScore = 800;

                var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                // THROW's number lands on SqlException.Number, not inside the message text - asserting
                // the text would pass on any refusal that happened to quote the sentence.
                var sql = Assert.IsType<Microsoft.Data.SqlClient.SqlException>(refused.InnerException);
                Assert.Equal(50029, sql.Number);
                Assert.Contains("read-only", sql.Message, StringComparison.OrdinalIgnoreCase);
            }

            // 3. And a preserved attempt cannot be deleted either: the learner keeps the sitting.
            await using (var db = DbContextFactory.Create(connectionString))
            {
                var deleted = await Assert.ThrowsAnyAsync<Microsoft.Data.SqlClient.SqlException>(() =>
                    db.Database.ExecuteSqlRawAsync($"DELETE FROM ExamAttempts WHERE AttemptId = {attemptId}"));
                Assert.Equal(50029, deleted.Number);
                Assert.Contains("cannot be deleted", deleted.Message, StringComparison.OrdinalIgnoreCase);
            }

            // 4. An UNTAGGED attempt is still an ordinary row: deletion works, which is what every
            //    fixture cleanup and every abandoned-sitting cleanup depends on. Without this the
            //    trigger would pass by refusing everything - which is exactly how its first draft
            //    broke ten schema tests.
            int ordinaryId;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                var attempt = new BusinessObjects.Entities.ExamAttempt
                {
                    UserId = userId,
                    ExamId = examId,
                    CertId = certId,
                    Status = "abandoned",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
                };
                db.ExamAttempts.Add(attempt);
                await db.SaveChangesAsync();
                ordinaryId = attempt.AttemptId;
            }

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var removed = await db.ExamAttempts.Where(a => a.AttemptId == ordinaryId)
                    .ExecuteDeleteAsync();
                Assert.Equal(1, removed);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>The tag is a closed set: a typo must not become a new epoch nobody knows the meaning of.</summary>
    [Fact]
    public async Task An_epoch_tag_outside_the_known_set_is_refused()
    {
        var name = $"EE246B_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var (userId, examId, certId) = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var attempt = new BusinessObjects.Entities.ExamAttempt
            {
                UserId = userId,
                ExamId = examId,
                CertId = certId,
                Status = "graded",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
                SubmittedAt = DateTimeOffset.UtcNow,
                EpochTag = "legacy_v2",
            };
            db.ExamAttempts.Add(attempt);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ----------------------------------------------------------------------------

    private static async Task<(int UserId, int ExamId, int CertId)> SeedAsync(string connectionString)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"C{Guid.NewGuid():N}"[..12];
        var cert = new BusinessObjects.Entities.Certificate { Code = code, Name = $"Cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new BusinessObjects.Entities.Skill
        {
            CertId = cert.CertId,
            Code = "READ",
            Name = "Reading",
            Modality = "reading",
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new BusinessObjects.Entities.Section
        {
            CertId = cert.CertId,
            SkillId = skill.SkillId,
            Modality = "reading",
            Code = "P5",
            Name = "Sentences",
            ExpectedQuestionCount = 1,
            DisplayOrder = 1,
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var exam = new BusinessObjects.Entities.Exam
        {
            CertId = cert.CertId,
            Name = $"Exam {code}",
            Status = "published",
            DurationMinutes = 30,
            Purpose = "mock",
            QuestionCount = 1,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var role = new BusinessObjects.Entities.Role { Code = $"learner{Guid.NewGuid():N}"[..20], Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new BusinessObjects.Entities.User
        {
            RoleId = role.RoleId,
            Email = $"{Guid.NewGuid():N}@preserved.test",
            PasswordHash = [1],
            FullName = "Preserved learner",
            ActiveCertId = cert.CertId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (user.UserId, exam.ExamId, cert.CertId);
    }
}
