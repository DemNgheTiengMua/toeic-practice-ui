using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class LessonServiceTests(DatabaseFixture database)
{
    private LessonService CreateService()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new LessonService(db);
    }

    private async Task<(int certId, int skillId, int categoryId, int topicId, int lessonId, int card1Id, int card2Id, int card3Id, int card4Id)> SeedLessonFixtureAsync()
    {
        var certCode = $"CERT{Guid.NewGuid():N}"[..10];
        await database.SqlAsync($"""
            -- #240: a lesson is addressed by (skill, topic, level), so the fixture names a canonical
            -- reading topic.
            INSERT INTO Topics (Modality, Code, Name)
            SELECT v.Modality, v.Code, v.Name
            FROM (VALUES
              ('listening','L-GIST',N'Global gist'), ('listening','L-DET',N'Detail'),
              ('listening','L-INF',N'Inference'), ('listening','L-ATT',N'Attitude'),
              ('listening','L-FUNC',N'Function'), ('listening','L-ORG',N'Organisation'),
              ('reading','R-GIST',N'Global thesis'), ('reading','R-DET',N'Local detail'),
              ('reading','R-INF',N'Bridging inference'), ('reading','R-ATT',N'Authorial stance'),
              ('reading','R-COH',N'Cohesion'), ('reading','R-GRAM',N'Grammar'),
              ('speaking','S-DESC',N'Description'), ('speaking','S-INT',N'Interaction'),
              ('speaking','S-ARG',N'Argument'), ('speaking','S-SYN',N'Synthesis'),
              ('writing','W-CORR',N'Correspondence'), ('writing','W-DATA',N'Data'),
              ('writing','W-ARG',N'Argument'), ('writing','W-DISC',N'Discourse')
            ) v(Modality, Code, Name)
            WHERE NOT EXISTS (SELECT 1 FROM Topics t WHERE t.Modality = v.Modality AND t.Code = v.Code);

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Test Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO QuestionCategories (CertId, Code, Name)
            VALUES (@cert, 'GRAMMAR', N'Grammar Test');
            DECLARE @cat INT = SCOPE_IDENTITY();

            DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM');

            INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, CategoryId, Title, Summary, EstimatedMinutes, DisplayOrder)
            VALUES (@cert, @skill, 'reading', @topic, 'B1', @cat, N'Present Simple', N'Learn present simple tense', 5, 1);
            DECLARE @lesson INT = SCOPE_IDENTITY();

            INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder) VALUES
              (@lesson, @cert, 'rule', N'The Rule', N'# Present Simple\nSubject + verb', 0),
              (@lesson, @cert, 'pitfall', N'Common Mistakes', N'TRAP: Do not add -s to all subjects', 1),
              (@lesson, @cert, 'example', N'Example', N'She **walks** to school.', 2),
              (@lesson, @cert, 'micro_check', N'Check Your Understanding', N'Question content', 3);

            DECLARE @c1 INT = (SELECT CardId FROM LessonCards WHERE LessonId=@lesson AND CardType='rule');
            DECLARE @c2 INT = (SELECT CardId FROM LessonCards WHERE LessonId=@lesson AND CardType='pitfall');
            DECLARE @c3 INT = (SELECT CardId FROM LessonCards WHERE LessonId=@lesson AND CardType='example');
            DECLARE @c4 INT = (SELECT CardId FROM LessonCards WHERE LessonId=@lesson AND CardType='micro_check');

            UPDATE LessonCards SET
              QuestionPrompt = N'Which sentence is correct?',
              OptionA = N'He walk to school.',
              OptionB = N'He walks to school.',
              OptionC = N'He walking to school.',
              OptionD = N'He is walk to school.',
              CorrectOption = 'B',
              Explanation = N'Third person singular takes -s'
            WHERE CardId = @c4;
            """);

        // Query the inserted data to get IDs
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.FirstAsync(c => c.Code == certCode);
        var skill = await db.Skills.FirstAsync(s => s.CertId == cert.CertId);
        var category = await db.QuestionCategories.FirstAsync(qc => qc.CertId == cert.CertId);
        var lesson = await db.Lessons.FirstAsync(l => l.CertId == cert.CertId);
        var cards = await db.LessonCards.Where(lc => lc.LessonId == lesson.LessonId).OrderBy(lc => lc.DisplayOrder).ToListAsync();

        return (cert.CertId, skill.SkillId, category.CategoryId, lesson.TopicId, lesson.LessonId, cards[0].CardId, cards[1].CardId, cards[2].CardId, cards[3].CardId);
    }

    private record FixtureResult(int CertId, int SkillId, int CategoryId, int LessonId, int Card1Id, int Card2Id, int Card3Id, int Card4Id);

    [Fact]
    public async Task GetLessonAsync_ReturnsLessonWithCardsInDisplayOrder()
    {
        var fixture = await SeedLessonFixtureAsync();
        var service = CreateService();

        var lesson = await service.GetLessonAsync(fixture.lessonId, fixture.certId);

        Assert.NotNull(lesson);
        Assert.Equal("Present Simple", lesson.Title);
        Assert.Equal("Learn present simple tense", lesson.Summary);
        Assert.Equal(5, lesson.EstimatedMinutes);
        Assert.Equal(4, lesson.Cards.Count);
        Assert.Equal("rule", lesson.Cards[0].CardType);
        Assert.Equal("pitfall", lesson.Cards[1].CardType);
        Assert.Equal("example", lesson.Cards[2].CardType);
        Assert.Equal("micro_check", lesson.Cards[3].CardType);
    }

    [Fact]
    public async Task GetLessonAsync_ThrowsWhenLessonNotInCertificate()
    {
        var fixture = await SeedLessonFixtureAsync();
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.GetLessonAsync(fixture.lessonId, 99999));
    }

    [Fact]
    public async Task GetProgressAsync_ReturnsDefaultForNewLearner()
    {
        var fixture = await SeedLessonFixtureAsync();
        var service = CreateService();
        var userId = await SeedLearnerAsync();

        var progress = await service.GetProgressAsync(userId, fixture.lessonId);

        Assert.NotNull(progress);
        Assert.Equal(0, progress.CardsCompleted);
        Assert.False(progress.IsCompleted);
    }

    [Fact]
    public async Task RecordCardViewAsync_IncrementsCardsCompletedAndUpdatesLastViewed()
    {
        var fixture = await SeedLessonFixtureAsync();
        var service = CreateService();
        var userId = await SeedLearnerAsync();

        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card1Id);

        var progress = await service.GetProgressAsync(userId, fixture.lessonId);
        Assert.Equal(1, progress.CardsCompleted);
        Assert.Equal(fixture.card1Id, progress.LastViewedCardId);
        Assert.False(progress.IsCompleted);
    }

    [Fact]
    public async Task RecordCardViewAsync_SetsIsCompletedWhenAllCardsViewed()
    {
        var fixture = await SeedLessonFixtureAsync();
        var service = CreateService();
        var userId = await SeedLearnerAsync();

        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card1Id);
        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card2Id);
        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card3Id);
        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card4Id);

        var progress = await service.GetProgressAsync(userId, fixture.lessonId);
        Assert.Equal(4, progress.CardsCompleted);
        Assert.True(progress.IsCompleted);
        Assert.NotNull(progress.CompletedAt);
    }

    [Fact]
    public async Task RecordCardViewAsync_TimestampsFirstOpenedAtOnFirstView()
    {
        var fixture = await SeedLessonFixtureAsync();
        var service = CreateService();
        var userId = await SeedLearnerAsync();

        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card1Id);

        var progress = await service.GetProgressAsync(userId, fixture.lessonId);
        Assert.NotNull(progress.FirstOpenedAt);
        Assert.NotNull(progress.UpdatedAt);
    }

    [Fact]
    public async Task RecordCardViewAsync_OnlyIncrementsForNewCards()
    {
        var fixture = await SeedLessonFixtureAsync();
        var service = CreateService();
        var userId = await SeedLearnerAsync();

        // View cards in order: 1, 2, 3
        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card1Id);
        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card2Id);
        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card3Id);

        var progress = await service.GetProgressAsync(userId, fixture.lessonId);
        Assert.Equal(3, progress.CardsCompleted);

        // Re-view card 2 (going backward) - should NOT increment
        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card2Id);

        progress = await service.GetProgressAsync(userId, fixture.lessonId);
        Assert.Equal(3, progress.CardsCompleted);
        Assert.Equal(fixture.card2Id, progress.LastViewedCardId);
    }

    [Fact]
    public async Task RecordCardViewAsync_UpdatesUpdatedAtOnEveryView()
    {
        var fixture = await SeedLessonFixtureAsync();
        var service = CreateService();
        var userId = await SeedLearnerAsync();

        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card1Id);
        var progress1 = await service.GetProgressAsync(userId, fixture.lessonId);
        var firstUpdatedAt = progress1.UpdatedAt;

        await Task.Delay(50); // Ensure timestamp difference

        await service.RecordCardViewAsync(userId, fixture.lessonId, fixture.card2Id);
        var progress2 = await service.GetProgressAsync(userId, fixture.lessonId);

        Assert.NotNull(progress2.UpdatedAt);
        Assert.True(progress2.UpdatedAt > firstUpdatedAt);
    }

    [Fact]
    public async Task GetLessonByCategoryAsync_ReturnsFirstLessonForCategory()
    {
        var fixture = await SeedLessonFixtureAsync();
        var service = CreateService();

        var lesson = await service.GetLessonByCategoryAsync(fixture.categoryId, fixture.certId);

        Assert.NotNull(lesson);
        Assert.Equal("Present Simple", lesson.Title);
    }

    [Fact]
    public async Task GetLessonByCategoryAsync_ReturnsNullWhenNoLessonExists()
    {
        var service = CreateService();

        var lesson = await service.GetLessonByCategoryAsync(99999, 1);

        Assert.Null(lesson);
    }

    private async Task<int> SeedLearnerAsync()
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }
}
