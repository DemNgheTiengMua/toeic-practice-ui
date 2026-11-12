using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #269: <b>many pool answers fit on one attempt.</b>
/// <para>
/// This file used to prove the keystone of #250 - that a pool answer names NO legacy <c>QuestionId</c>, so
/// <c>AttemptEngine</c> could stop reading <c>db.Questions</c>. #269 deleted the column and the three
/// legacy tables, so that absence is no longer a capability a test can observe: there is no column to
/// leave NULL. What survives from the file is the claim that made the shape worth having - an attempt
/// holds as many answers as its paper has positions, not one.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolAnswerShapesTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// Several pool answers on ONE attempt, each naming its own roster position. The unique index is
    /// <c>(AttemptId, ExamItemId)</c>, so what must not collide is the position - three distinct
    /// positions must all be storable, and <c>PoolAnswerPositionTests</c> proves the collision is
    /// refused.
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

                for (var i = 0; i < fixture.Items.Count; i++)
                {
                    var (examItemId, itemId, versionId) = fixture.Items[i];
                    db.AttemptAnswers.Add(new AttemptAnswer
                    {
                        AttemptId = attempt.AttemptId,
                        ExamId = fixture.ExamId,
                        ExamItemId = examItemId,
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

    // ---- fixture ---------------------------------------------------------------------------

    private sealed record Fixture(
        int UserId,
        int CertId,
        int ExamId,
        IReadOnlyList<(int ExamItemId, int ItemId, int ItemVersionId)> Items);

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
            ExpectedQuestionCount = itemCount,
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
            QuestionCount = itemCount,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var topic = new Topic
        {
            Modality = "reading", Code = "R-DET", Name = "Local factual retrieval and paraphrase matching",
        };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var items = new List<(int ExamItemId, int ItemId, int ItemVersionId)>();
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

            var binding = new ExamItem
            {
                ExamId = exam.ExamId,
                CertId = cert.CertId,
                SectionId = section.SectionId,
                ItemId = item.ItemId,
                ItemVersionId = version.ItemVersionId,
                Modality = "reading",
                DisplayOrder = i + 1,
            };
            db.ExamItems.Add(binding);
            await db.SaveChangesAsync();

            items.Add((binding.ExamItemId, item.ItemId, version.ItemVersionId));
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

        return new Fixture(user.UserId, cert.CertId, exam.ExamId, items);
    }
}
