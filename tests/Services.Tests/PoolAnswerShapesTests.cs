using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250: <b>a pool answer names no legacy question.</b>
/// <para>
/// <b>Why this is the keystone of the ticket.</b> <c>AttemptAnswers.QuestionId</c> was NOT NULL with a
/// foreign key to <c>Questions</c>, so every pool answer had to invent a question id - and that is
/// precisely why <c>AttemptEngine</c> could never reach zero reads of <c>db.Questions</c>, and why this
/// ticket could not drop the three legacy tables. The column is nullable now; this file proves the
/// shape is actually reachable, because a nullable column that no code can write is not a capability.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> Asserting "the row saved" passes on a schema
/// where the CHECK still required a question. So the assertions are on the STORED row: QuestionId is
/// NULL, ItemId and ItemVersionId are set, and the roster position the answer belongs to exists. The
/// refusal case is proved too - a row naming NEITHER shape is refused by name, because such a row
/// cannot be rendered, scored or reviewed.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolAnswerShapesTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// A pool answer is written with <c>QuestionId = NULL</c> and a pinned item version, and the database
    /// accepts it.
    /// </summary>
    [Fact]
    public async Task A_pool_answer_is_stored_with_no_legacy_question()
    {
        var name = $"EE250s_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using (var db = DbContextFactory.Create(connectionString))
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

                // The whole point: no QuestionId at all.
                db.AttemptAnswers.Add(new AttemptAnswer
                {
                    AttemptId = attempt.AttemptId,
                    ExamId = fixture.ExamId,
                    QuestionId = null,
                    ItemId = fixture.ItemId,
                    ItemVersionId = fixture.ItemVersionId,
                    SelectedLabel = "A",
                    IsCorrect = true,
                });
                await db.SaveChangesAsync();
            }

            await using var read = DbContextFactory.Create(connectionString);
            var stored = await read.AttemptAnswers.AsNoTracking().SingleAsync();

            Assert.Null(stored.QuestionId);
            Assert.Equal(fixture.ItemId, stored.ItemId);
            Assert.Equal(fixture.ItemVersionId, stored.ItemVersionId);
            Assert.Equal("A", stored.SelectedLabel);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Several pool answers on ONE attempt: the filtered unique index must not treat every NULL
    /// QuestionId as a duplicate. This is the defect that made the index necessary rather than
    /// cosmetic - `UNIQUE (AttemptId, QuestionId)` over nullable rows admits exactly one.
    /// </summary>
    [Fact]
    public async Task One_attempt_can_hold_many_pool_answers()
    {
        var name = $"EE250m_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString, itemCount: 3);

            await using (var db = DbContextFactory.Create(connectionString))
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

                foreach (var (itemId, versionId) in fixture.Items)
                {
                    db.AttemptAnswers.Add(new AttemptAnswer
                    {
                        AttemptId = attempt.AttemptId,
                        ExamId = fixture.ExamId,
                        QuestionId = null,
                        ItemId = itemId,
                        ItemVersionId = versionId,
                        SelectedLabel = "A",
                        IsCorrect = true,
                    });
                }

                await db.SaveChangesAsync();
            }

            await using var read = DbContextFactory.Create(connectionString);
            Assert.Equal(3, await read.AttemptAnswers.CountAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// A row that names neither shape is refused by the constraint's own name. It cannot be rendered,
    /// scored or reviewed: nothing identifies what was asked.
    /// </summary>
    [Fact]
    public async Task An_answer_naming_neither_a_question_nor_a_version_is_refused()
    {
        var name = $"EE250n_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
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

            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = attempt.AttemptId,
                ExamId = fixture.ExamId,
                QuestionId = null,
                ItemId = null,
                ItemVersionId = null,
                SelectedLabel = "A",
            });

            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("CK_AttemptAnswers_HasShape", refused.InnerException?.Message ?? string.Empty,
                StringComparison.Ordinal);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// A legacy answer still works unchanged: the column is nullable, not replaced.
    /// </summary>
    [Fact]
    public async Task A_legacy_answer_still_names_its_question_and_no_version()
    {
        var name = $"EE250l_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using (var db = DbContextFactory.Create(connectionString))
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

                db.AttemptAnswers.Add(new AttemptAnswer
                {
                    AttemptId = attempt.AttemptId,
                    ExamId = fixture.ExamId,
                    QuestionId = fixture.QuestionId,
                    ItemId = null,
                    ItemVersionId = null,
                    SelectedOptionId = fixture.CorrectOptionId,
                    IsCorrect = true,
                });
                await db.SaveChangesAsync();
            }

            await using var read = DbContextFactory.Create(connectionString);
            var stored = await read.AttemptAnswers.AsNoTracking().SingleAsync();
            Assert.Equal(fixture.QuestionId, stored.QuestionId);
            Assert.Null(stored.ItemVersionId);
            Assert.True(stored.IsCorrect);
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
        int QuestionId,
        int CorrectOptionId,
        int ItemId,
        int ItemVersionId,
        IReadOnlyList<(int ItemId, int ItemVersionId)> Items);

    private static async Task<Fixture> SeedAsync(string connectionString, int itemCount = 1)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"C{Guid.NewGuid():N}"[..12];
        var cert = new Certificate { Code = code, Name = $"Shape cert {code}", IsActive = true };
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
            ExpectedQuestionCount = 1,
            DisplayOrder = 1,
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId,
            Name = $"Shape exam {code}",
            Status = "published",
            DurationMinutes = 30,
            Purpose = "mock",
            QuestionCount = 1,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        // One legacy question, so the legacy shape can be proved beside the pool one.
        // #272: this row STAYS legacy because it is the subject of
        // A_legacy_answer_still_names_its_question_and_no_version - the test asserts that the nullable
        // column still admits the shape the pool replaced, which needs a row in that shape.
        var group = new QuestionGroup
        {
            ExamId = exam.ExamId, CertId = cert.CertId, SectionId = section.SectionId,
            DisplayOrder = 1, UnitNumber = 1,
        };
        db.QuestionGroups.Add(group);
        await db.SaveChangesAsync();

        var question = new Question
        {
            GroupId = group.GroupId, ExamId = exam.ExamId, SectionId = section.SectionId,
            SkillId = skill.SkillId, Stem = "Legacy stem", CefrLevel = CefrLevel.B1,
            QuestionType = "mcq", DisplayOrder = 1,
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();

        var option = new QuestionOption
        {
            QuestionId = question.QuestionId, QuestionType = "mcq", Label = "A", Text = "Right", IsCorrect = true,
        };
        db.QuestionOptions.Add(option);
        await db.SaveChangesAsync();

        var topic = new Topic
        {
            Modality = "reading", Code = "R-DET", Name = "Local factual retrieval and paraphrase matching",
        };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var items = new List<(int ItemId, int ItemVersionId)>();
        for (var i = 0; i < itemCount; i++)
        {
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
                Stem = $"Pool stem {i + 1}",
                OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d",
                KeyLabel = "A",
                Explanation = "The passage states it.",
            };
            db.ItemVersions.Add(version);
            await db.SaveChangesAsync();

            db.ExamItems.Add(new ExamItem
            {
                ExamId = exam.ExamId,
                CertId = cert.CertId,
                SectionId = section.SectionId,
                ItemId = item.ItemId,
                ItemVersionId = version.ItemVersionId,
                Modality = "reading",
                DisplayOrder = i + 1,
            });
            await db.SaveChangesAsync();

            items.Add((item.ItemId, version.ItemVersionId));
        }

        var role = new Role { Code = Unique("lrn"), Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new User
        {
            RoleId = role.RoleId,
            Email = $"{Guid.NewGuid():N}@shape.test",
            PasswordHash = [1],
            FullName = "Shape learner",
            ActiveCertId = cert.CertId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new Fixture(
            user.UserId,
            cert.CertId,
            exam.ExamId,
            question.QuestionId,
            option.OptionId,
            items[0].ItemId,
            items[0].ItemVersionId,
            items);
    }
}
