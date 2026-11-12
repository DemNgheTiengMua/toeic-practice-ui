using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 criterion 1, the enabling step: <b>an answer can name the paper position it was recorded
/// against, so the sitting projection has an identity for a pool question.</b>
/// <para>
/// <b>Why a position and not the item version.</b> <c>ExamItems</c> says a paper may bind one version at
/// two positions — "the same item on two papers, or twice on one paper, gets two positions". So
/// <c>(ItemId, ItemVersionId)</c> does not say <i>which</i> question of the paper was answered. Without a
/// position column two answers to a repeated item are indistinguishable and the navigator cannot mark
/// the right cell as answered. The position is the answer's identity, exactly as <c>QuestionId</c> is on
/// a legacy paper.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> "The row saved" passes on a schema where the
/// column is a plain integer with no rule. So the second case proves the unique index: two answers to
/// the SAME roster position are refused, while two answers to two positions holding the SAME version are
/// accepted — which is the distinction the position exists to make.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolAnswerPositionTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>An answer records the roster position, with no legacy question at all.</summary>
    [Fact]
    public async Task A_pool_answer_names_the_roster_position_it_was_recorded_against()
    {
        var name = $"EE250p_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var attempt = await NewAttemptAsync(db, fixture);
                db.AttemptAnswers.Add(new AttemptAnswer
                {
                    AttemptId = attempt.AttemptId,
                    ExamId = fixture.ExamId,
                    ItemId = fixture.ItemId,
                    ItemVersionId = fixture.ItemVersionId,
                    ExamItemId = fixture.ExamItemId,
                    SelectedLabel = "A",
                    IsCorrect = true,
                });
                await db.SaveChangesAsync();
            }

            await using var read = DbContextFactory.Create(connectionString);
            var stored = await read.AttemptAnswers.AsNoTracking().SingleAsync();
            Assert.Equal(fixture.ExamItemId, stored.ExamItemId);
            Assert.Equal(fixture.ItemVersionId, stored.ItemVersionId);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The distinction the column exists for: one paper may hold the SAME version twice, and those are
    /// two answerable questions. A unique index on the item version would refuse the second answer.
    /// </summary>
    [Fact]
    public async Task One_version_bound_twice_is_two_answerable_positions()
    {
        var name = $"EE250t_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            // Two roster rows, one version: the repeated-item case.
            var fixture = await SeedAsync(connectionString, rosterRows: 2, sharedVersion: true);

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var attempt = await NewAttemptAsync(db, fixture);
                for (var i = 0; i < fixture.ExamItemIds.Count; i++)
                {
                    db.AttemptAnswers.Add(new AttemptAnswer
                    {
                        AttemptId = attempt.AttemptId,
                        ExamId = fixture.ExamId,
                        ItemId = fixture.ItemId,
                        ItemVersionId = fixture.ItemVersionId,
                        ExamItemId = fixture.ExamItemIds[i],
                        SelectedLabel = i == 0 ? "A" : "B",
                        IsCorrect = i == 0,
                    });
                }

                await db.SaveChangesAsync();
            }

            await using var read = DbContextFactory.Create(connectionString);
            Assert.Equal(2, await read.AttemptAnswers.CountAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>One answer per position, refused by the filtered unique index's own name.</summary>
    [Fact]
    public async Task Two_answers_to_the_same_position_are_refused()
    {
        var name = $"EE250d2_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var attempt = await NewAttemptAsync(db, fixture);

            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = attempt.AttemptId,
                ExamId = fixture.ExamId,
                ItemId = fixture.ItemId,
                ItemVersionId = fixture.ItemVersionId,
                ExamItemId = fixture.ExamItemId,
                SelectedLabel = "A",
            });
            await db.SaveChangesAsync();

            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = attempt.AttemptId,
                ExamId = fixture.ExamId,
                ItemId = fixture.ItemId,
                ItemVersionId = fixture.ItemVersionId,
                ExamItemId = fixture.ExamItemId,
                SelectedLabel = "B",
            });

            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("UX_AttemptAnswers_ExamItem", refused.InnerException?.Message ?? string.Empty,
                StringComparison.Ordinal);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ---------------------------------------------------------------------------

    private sealed record Fixture(
        int UserId,
        int CertId,
        int ExamId,
        int ItemId,
        int ItemVersionId,
        int ExamItemId,
        IReadOnlyList<int> ExamItemIds);

    private static async Task<ExamAttempt> NewAttemptAsync(CompanionDbContext db, Fixture fixture)
    {
        var attempt = new ExamAttempt
        {
            UserId = fixture.UserId,
            CertId = fixture.CertId,
            ExamId = fixture.ExamId,
            Status = "in_progress",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt;
    }

    private static async Task<Fixture> SeedAsync(
        string connectionString, int rosterRows = 1, bool sharedVersion = false)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"C{Guid.NewGuid():N}"[..12];
        var cert = new Certificate { Code = code, Name = $"Position cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId,
            SkillId = skill.SkillId,
            Modality = "reading",
            Code = "P5",
            Name = "Incomplete Sentences",
            OptionCount = 4,
            ExpectedQuestionCount = rosterRows,
            DisplayOrder = 1,
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId,
            Name = $"Position exam {code}",
            Status = "published",
            DurationMinutes = 30,
            Purpose = "mock",
            QuestionCount = rosterRows,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var topic = new Topic
        {
            Modality = "reading", Code = "R-DET", Name = "Local factual retrieval and paraphrase matching",
        };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var item = new Item
        {
            Modality = "reading",
            TopicId = topic.TopicId,
            CefrLevel = CefrLevel.B1,
            ItemType = "sentence_cloze",
            ContentTier = "demo",
            LifecycleStatus = "active",
            RenderFormat = "mcq",
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var version = new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = "The depot opens _____ six.",
            OptionA = "at", OptionB = "on", OptionC = "in", OptionD = "by",
            KeyLabel = "A",
            Explanation = "A clock time takes 'at'.",
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync();

        var examItemIds = new List<int>();
        for (var i = 0; i < rosterRows; i++)
        {
            // sharedVersion: every roster row pins the SAME version, which is the repeated-item case.
            var versionForRow = sharedVersion ? version : version;
            var roster = new ExamItem
            {
                ExamId = exam.ExamId,
                CertId = cert.CertId,
                SectionId = section.SectionId,
                ItemId = item.ItemId,
                ItemVersionId = versionForRow.ItemVersionId,
                Modality = "reading",
                DisplayOrder = i + 1,
            };
            db.ExamItems.Add(roster);
            await db.SaveChangesAsync();
            examItemIds.Add(roster.ExamItemId);
        }

        var role = new Role { Code = Unique("lrn"), Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new User
        {
            RoleId = role.RoleId,
            Email = $"{Guid.NewGuid():N}@position.test",
            PasswordHash = [1],
            FullName = "Position learner",
            ActiveCertId = cert.CertId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new Fixture(
            user.UserId,
            cert.CertId,
            exam.ExamId,
            item.ItemId,
            version.ItemVersionId,
            examItemIds[0],
            examItemIds);
    }
}
