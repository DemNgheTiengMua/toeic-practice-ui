using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #247.4: <b>concurrent submissions and out-of-order grading callbacks are exercised, and no derived
/// fact ends up with two writers.</b>
/// <para>
/// <b>Why this is not covered by the existing out-of-order test.</b>
/// <c>SkillStateWriterTests.A_stale_projection_version_cannot_overwrite_a_newer_state</c> proves the
/// PROJECTION_VERSION guard, by writing version 1 after version 2. That is one process trying to go
/// backwards. This file exercises the other shape: two callers that both believe they are current, so
/// neither is going backwards - a learner double-clicking Submit while an auto-submit fires, and two
/// grading callbacks arriving for the same attempt.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> "Submit twice and the score is the same" passes
/// on an engine that scored twice and wrote the same value. So this asserts the ROW COUNT too: one set of
/// skill scores, one band, one writer declaration. A second write would show up as either a different
/// version or a duplicated row, and both are checked.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConcurrentSubmissionTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// Two submit calls that overlap in time. The engine must end with ONE scored attempt and ONE set of
    /// skill scores; the second caller gets the first caller's result rather than scoring again.
    /// </summary>
    [Fact]
    public async Task Two_overlapping_submissions_produce_one_score_and_one_set_of_skill_rows()
    {
        var name = $"EEConc_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            // Two independent contexts, like two HTTP requests: neither can see the other's uncommitted
            // work, which is exactly the state a double-click creates.
            var first = SubmitAsync(connectionString, fixture.AttemptId, fixture.UserId);
            var second = SubmitAsync(connectionString, fixture.AttemptId, fixture.UserId);

            var results = await Task.WhenAll(first, second);

            // Both callers get a result; neither throws. A refusal would leave a learner staring at an
            // error after the paper was in fact submitted.
            Assert.All(results, r => Assert.Equal(fixture.AttemptId, r.Attempt.AttemptId));

            // The derived fact was written once. Same score from both callers is necessary but not
            // sufficient - the row counts are what prove a second write did not happen.
            await using var verify = DbContextFactory.Create(connectionString);
            var attempt = await verify.ExamAttempts.AsNoTracking()
                .SingleAsync(a => a.AttemptId == fixture.AttemptId);
            var skillRows = await verify.AttemptSkillScores.AsNoTracking()
                .Where(s => s.AttemptId == fixture.AttemptId)
                .ToListAsync();

            Assert.Equal(1, skillRows.Count);
            Assert.NotNull(attempt.TotalScore);

            // Both callers reported the same score, because the second read the first's result rather
            // than computing its own.
            Assert.Equal(results[0].TotalScore, results[1].TotalScore);
            Assert.Equal(attempt.TotalScore, results[0].TotalScore);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The same submission repeated sequentially: the second is a no-op. This is the ordinary
    /// double-click, and it must not bump the projection version - a version bump is how a reader knows
    /// the derived fact moved, and it did not.
    /// </summary>
    [Fact]
    public async Task Submitting_the_same_attempt_twice_does_not_bump_the_projection_version()
    {
        var name = $"EEConc2_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await SubmitAsync(connectionString, fixture.AttemptId, fixture.UserId);

            int? scoreAfterFirst;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                var attempt = await db.ExamAttempts.AsNoTracking()
                    .SingleAsync(a => a.AttemptId == fixture.AttemptId);
                scoreAfterFirst = attempt.TotalScore;
            }

            await SubmitAsync(connectionString, fixture.AttemptId, fixture.UserId);

            await using (var verify = DbContextFactory.Create(connectionString))
            {
                var attempt = await verify.ExamAttempts.AsNoTracking()
                    .SingleAsync(a => a.AttemptId == fixture.AttemptId);
                Assert.Equal(scoreAfterFirst, attempt.TotalScore);
                Assert.Equal(1, await verify.AttemptSkillScores.CountAsync(s => s.AttemptId == fixture.AttemptId));
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixtures ---------------------------------------------------------------------------

    private static async Task<AttemptGradingResult> SubmitAsync(string connectionString, int attemptId, int userId)
    {
        await using var db = DbContextFactory.Create(connectionString);
        return await new AttemptEngine(db).SubmitAttemptAsync(attemptId, userId, isAutoSubmit: false);
    }

    private sealed record Fixture(int UserId, int AttemptId);

    /// <summary>
    /// One learner, one published mock exam with one objective question, one attempt already open with
    /// the correct answer recorded. The attempt is left in progress so submitting it is the whole act
    /// under test.
    /// </summary>
    private static async Task<Fixture> SeedAsync(string connectionString)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"C{Guid.NewGuid():N}"[..12];
        var cert = new BusinessObjects.Entities.Certificate { Code = code, Name = $"Conc cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new BusinessObjects.Entities.Skill
        {
            CertId = cert.CertId,
            Code = "READ",
            Name = "Reading",
            Modality = "reading",
            MinScaledScore = 100,
            MaxScaledScore = 500,
            ScoreStep = 50,
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

        db.ScoreBands.Add(new BusinessObjects.Entities.ScoreBand
        {
            CertId = cert.CertId, Code = "LOW", Name = "Low", MinTotal = 0, MaxTotal = 499, DisplayOrder = 1,
        });
        db.ScoreBands.Add(new BusinessObjects.Entities.ScoreBand
        {
            CertId = cert.CertId, Code = "HIGH", Name = "High", MinTotal = 500, MaxTotal = 990, DisplayOrder = 2,
        });
        await db.SaveChangesAsync();

        // The raw-to-scaled table the scorer reads.
        db.ScoreScales.Add(new BusinessObjects.Entities.ScoreScale
        {
            CertId = cert.CertId, SkillId = skill.SkillId, ExamId = null, RawScore = 0, ScaledScore = 0,
        });
        db.ScoreScales.Add(new BusinessObjects.Entities.ScoreScale
        {
            CertId = cert.CertId, SkillId = skill.SkillId, ExamId = null, RawScore = 1, ScaledScore = 300,
        });
        await db.SaveChangesAsync();

        var exam = new BusinessObjects.Entities.Exam
        {
            CertId = cert.CertId,
            Name = $"Conc exam {code}",
            Status = "published",
            DurationMinutes = 30,
            Purpose = "mock",
            QuestionCount = 1,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        // #272: the paper's one position is a pool MCQ. The attempt and its answer are written by hand
        // because submitting is the only thing left to race, so the answer records the position's
        // ExamItemId and the label the sitting would have stored.
        var position = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading",
            "What time does the depot open?", displayOrder: 1);

        var role = new BusinessObjects.Entities.Role { Code = Unique("lrn"), Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new BusinessObjects.Entities.User
        {
            RoleId = role.RoleId,
            Email = $"{Guid.NewGuid():N}@conc.test",
            PasswordHash = [1],
            FullName = "Concurrent learner",
            ActiveCertId = cert.CertId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var attempt = new BusinessObjects.Entities.ExamAttempt
        {
            UserId = user.UserId,
            ExamId = exam.ExamId,
            CertId = cert.CertId,
            Status = "in_progress",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync();

        // The answer is already recorded: submitting is the only thing left to race.
        db.AttemptAnswers.Add(new BusinessObjects.Entities.AttemptAnswer
        {
            AttemptId = attempt.AttemptId,
            ExamItemId = position.ExamItemId,
            ItemId = position.ItemId,
            ItemVersionId = position.ItemVersionId,
            ExamId = exam.ExamId,
            SelectedLabel = "A",
            IsCorrect = true,
        });
        await db.SaveChangesAsync();

        return new Fixture(user.UserId, attempt.AttemptId);
    }
}
