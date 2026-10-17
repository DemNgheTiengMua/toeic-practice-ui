using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AdminLessonServiceTests(DatabaseFixture database)
{
    private AdminLessonService CreateService(RecordingFileStore? store = null)
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new AdminLessonService(db, store ?? new RecordingFileStore(), TimeProvider.System);
    }

    private async Task<(int certId, int skillId, int categoryId)> SeedCertAndSkillAsync()
    {
        var certCode = $"AL{Guid.NewGuid():N}"[..10];
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Admin Lesson Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO QuestionCategories (CertId, Code, Name)
            VALUES (@cert, 'GRAMMAR', N'Grammar Practice');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.FirstAsync(c => c.Code == certCode);
        var skill = await db.Skills.FirstAsync(s => s.CertId == cert.CertId);
        var category = await db.QuestionCategories.FirstAsync(c => c.CertId == cert.CertId);

        return (cert.CertId, skill.SkillId, category.CategoryId);
    }

    [Fact]
    public async Task CreateLessonAsync_SavesLessonAndCards_WithNormalizedContiguousDisplayOrder()
    {
        var (certId, skillId, categoryId) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var request = new AdminLessonUpsertDto(
            certId,
            skillId,
            categoryId,
            "Mastering Modals",
            "A comprehensive guide to modal verbs in English.",
            10,
            1,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "rule", "Rule 1", "Modals take base form.", DisplayOrder: 10),
                new(null, "pitfall", "Common Trap", "Don't use 'to' after modals.", DisplayOrder: 25),
                new(null, "micro_check", "Check", "Choose the correct modal.",
                    QuestionPrompt: "You _______ wear a helmet when cycling.",
                    OptionA: "must", OptionB: "must to", OptionC: "should to", OptionD: "ought",
                    CorrectOption: "A",
                    Explanation: "Must is followed directly by the base verb without 'to'.",
                    Rationale: "Options B and C incorrectly include 'to'.",
                    DisplayOrder: 99)
            }
        );

        var result = await service.CreateLessonAsync(request);

        Assert.NotNull(result);
        Assert.True(result.LessonId > 0);
        Assert.Equal("Mastering Modals", result.Title);
        Assert.Equal(10, result.EstimatedMinutes);
        Assert.Equal(3, result.Cards.Count);

        // Verify contiguous display order starting at 0
        Assert.Equal(0, result.Cards[0].DisplayOrder);
        Assert.Equal("rule", result.Cards[0].CardType);
        Assert.Equal(1, result.Cards[1].DisplayOrder);
        Assert.Equal("pitfall", result.Cards[1].CardType);
        Assert.Equal(2, result.Cards[2].DisplayOrder);
        Assert.Equal("micro_check", result.Cards[2].CardType);
        Assert.Equal("A", result.Cards[2].CorrectOption);
        Assert.Equal("Options B and C incorrectly include 'to'.", result.Cards[2].Rationale);
    }

    [Fact]
    public async Task GetLessonsAsync_FiltersByCertIdAndSkillId()
    {
        var (certId, skillId, categoryId) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var created = await service.CreateLessonAsync(new AdminLessonUpsertDto(
            certId,
            skillId,
            categoryId,
            "Lesson Filter Test",
            "Summary",
            5,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "rule", "Rule", "Content", DisplayOrder: 0)
            }
        ));

        var list = await service.GetLessonsAsync(certId, skillId);

        Assert.NotEmpty(list);
        var item = list.FirstOrDefault(l => l.LessonId == created.LessonId);
        Assert.NotNull(item);
        Assert.Equal("Lesson Filter Test", item.Title);
        Assert.Equal(1, item.CardCount);

        var emptyList = await service.GetLessonsAsync(certId, 99999);
        Assert.Empty(emptyList);
    }

    [Fact]
    public async Task GetLessonAsync_ReturnsNullWhenNotFound()
    {
        var service = CreateService();
        var result = await service.GetLessonAsync(999999);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateLessonAsync_ThrowsWhenCertNotFound()
    {
        var service = CreateService();
        var request = new AdminLessonUpsertDto(
            999999,
            1,
            null,
            "Missing Cert",
            null,
            5,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "rule", "R", "C", DisplayOrder: 0)
            }
        );

        var ex = await Assert.ThrowsAsync<AdminLessonException>(() => service.CreateLessonAsync(request));
        Assert.Equal(ApiError.Codes.CertificateUnavailable, ex.Code);
        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task CreateLessonAsync_ThrowsWhenSkillDoesNotBelongToCert()
    {
        var (cert1, _, _) = await SeedCertAndSkillAsync();
        var (_, skill2, _) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var request = new AdminLessonUpsertDto(
            cert1,
            skill2, // Skill belongs to cert2!
            null,
            "Cross Cert Skill",
            null,
            5,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "rule", "R", "C", DisplayOrder: 0)
            }
        );

        var ex = await Assert.ThrowsAsync<AdminLessonException>(() => service.CreateLessonAsync(request));
        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("Skill does not belong", ex.Message);
    }

    [Fact]
    public async Task CreateLessonAsync_ThrowsWhenCardTypeInvalid()
    {
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var request = new AdminLessonUpsertDto(
            certId,
            skillId,
            null,
            "Invalid Card Type",
            null,
            5,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "invalid_type", "Title", "Content", DisplayOrder: 0)
            }
        );

        var ex = await Assert.ThrowsAsync<AdminLessonException>(() => service.CreateLessonAsync(request));
        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("CardType must be", ex.Message);
    }

    [Fact]
    public async Task CreateLessonAsync_ThrowsWhenMicroCheckIncomplete()
    {
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var request = new AdminLessonUpsertDto(
            certId,
            skillId,
            null,
            "Incomplete MicroCheck",
            null,
            5,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "micro_check", "Check", "Content",
                    QuestionPrompt: null, OptionA: "A", OptionB: "B", OptionC: "C", OptionD: "D",
                    CorrectOption: "X", Explanation: null, DisplayOrder: 0)
            }
        );

        var ex = await Assert.ThrowsAsync<AdminLessonException>(() => service.CreateLessonAsync(request));
        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("QuestionPrompt is required", ex.Message);
    }

    [Fact]
    public async Task UpdateLessonAsync_UpdatesMetadataAndReplacesCards_WhilePreservingLearnerProgress()
    {
        var (certId, skillId, categoryId) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var created = await service.CreateLessonAsync(new AdminLessonUpsertDto(
            certId,
            skillId,
            categoryId,
            "Original Title",
            "Original Summary",
            5,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "rule", "Card 1", "Content 1", DisplayOrder: 0)
            }
        ));

        // Seed a learner progress record pointing to this card
        var cardId = created.Cards[0].CardId;
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
                INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Learner 1');
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT INTO LearnerLessonProgress (UserId, LessonId, CertId, LastViewedCardId, CardsCompleted, TotalCards)
            VALUES (@u, {created.LessonId}, {certId}, {cardId}, 1, 1);
            """);

        // Now update lesson with new cards
        var updateRequest = new AdminLessonUpsertDto(
            certId,
            skillId,
            categoryId,
            "Updated Title",
            "Updated Summary",
            12,
            3,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "example", "New Example", "Ex content", DisplayOrder: 0),
                new(null, "pitfall", "New Pitfall", "Trap content", DisplayOrder: 1)
            }
        );

        var updated = await service.UpdateLessonAsync(created.LessonId, updateRequest);

        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("Updated Summary", updated.Summary);
        Assert.Equal(12, updated.EstimatedMinutes);
        Assert.Equal(3, updated.DisplayOrder);
        Assert.Equal(2, updated.Cards.Count);

        // Verify progress record was preserved but LastViewedCardId nulled out cleanly
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var progress = await db.LearnerLessonProgresses.FirstOrDefaultAsync(p => p.LessonId == created.LessonId);
        Assert.NotNull(progress);
        Assert.Null(progress.LastViewedCardId);
    }

    [Fact]
    public async Task DeleteLessonAsync_RemovesLessonAndCards()
    {
        var (certId, skillId, categoryId) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var created = await service.CreateLessonAsync(new AdminLessonUpsertDto(
            certId,
            skillId,
            categoryId,
            "To Delete",
            null,
            5,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "rule", "Card", "Content", DisplayOrder: 0)
            }
        ));

        await service.DeleteLessonAsync(created.LessonId);

        var retrieved = await service.GetLessonAsync(created.LessonId);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteLessonAsync_BlockedWhenReferencedByPathModuleItem()
    {
        var (certId, skillId, categoryId) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var created = await service.CreateLessonAsync(new AdminLessonUpsertDto(
            certId,
            skillId,
            categoryId,
            "Referenced Lesson",
            null,
            5,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "rule", "Card", "Content", DisplayOrder: 0)
            }
        ));

        // Create ScoreBands, PathTemplate, PathModule, and PathModuleItem referencing RefLessonId
        await database.SqlAsync($"""
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
            ({certId}, 'B1', 'Band 1', 0, 500, 1),
            ({certId}, 'B2', 'Band 2', 501, 990, 2);

            INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name) VALUES ({certId}, 'B1', 'B2', N'Path Temp');
            DECLARE @pt INT = SCOPE_IDENTITY();

            INSERT INTO PathModules (TemplateId, SkillId, CertId, Title, DisplayOrder) VALUES (@pt, {skillId}, {certId}, N'Mod 1', 1);
            DECLARE @pm INT = SCOPE_IDENTITY();

            INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefLessonId, DisplayOrder)
            VALUES (@pm, {certId}, @pt, 'lesson', {created.LessonId}, 1);
            """);

        var ex = await Assert.ThrowsAsync<AdminLessonException>(() => service.DeleteLessonAsync(created.LessonId));
        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("Cannot delete lesson because it is referenced", ex.Message);
    }

    [Fact]
    public async Task ImportLessonsAsync_JsonManifest_CreatesNewLessons()
    {
        var (certId, _, _) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var manifestJson = """
        {
            "lessons": [
                {
                    "title": "JSON Import Lesson 1",
                    "skill": "reading",
                    "categoryCode": "GRAMMAR",
                    "summary": "Imported via JSON",
                    "estimatedMinutes": 8,
                    "displayOrder": 2,
                    "cards": [
                        {
                            "type": "rule",
                            "title": "Rule Alpha",
                            "contentMarkdown": "Alpha markdown content."
                        },
                        {
                            "type": "micro_check",
                            "title": "Check Alpha",
                            "contentMarkdown": "Answer this",
                            "questionPrompt": "What is A?",
                            "optionA": "Alpha",
                            "optionB": "Beta",
                            "optionC": "Gamma",
                            "optionD": "Delta",
                            "correctOption": "A",
                            "explanation": "Because it is alpha.",
                            "rationale": "Beta is wrong."
                        }
                    ]
                }
            ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(manifestJson));
        var result = await service.ImportLessonsAsync(certId, overwrite: false, ms, "manifest.json");

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(2, result.TotalCardsImported);

        var lessons = await service.GetLessonsAsync(certId);
        var imported = lessons.FirstOrDefault(l => l.Title == "JSON Import Lesson 1");
        Assert.NotNull(imported);
        Assert.Equal(8, imported.EstimatedMinutes);
        Assert.Equal(2, imported.CardCount);
    }

    [Fact]
    public async Task ImportLessonsAsync_FailsWhenDuplicateExistsAndOverwriteIsFalse()
    {
        var (certId, skillId, categoryId) = await SeedCertAndSkillAsync();
        var service = CreateService();

        await service.CreateLessonAsync(new AdminLessonUpsertDto(
            certId,
            skillId,
            categoryId,
            "Existing Unique Title",
            null,
            5,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "rule", "Card", "Content", DisplayOrder: 0)
            }
        ));

        var manifestJson = """
        {
            "lessons": [
                {
                    "title": "Existing Unique Title",
                    "skill": "reading",
                    "cards": [
                        { "type": "rule", "title": "R", "contentMarkdown": "C" }
                    ]
                }
            ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(manifestJson));
        var ex = await Assert.ThrowsAsync<AdminLessonException>(() =>
            service.ImportLessonsAsync(certId, overwrite: false, ms, "manifest.json"));

        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task ImportLessonsAsync_UpdatesExistingWhenOverwriteIsTrue()
    {
        var (certId, skillId, categoryId) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var existing = await service.CreateLessonAsync(new AdminLessonUpsertDto(
            certId,
            skillId,
            categoryId,
            "Overwrite Target",
            "Initial Summary",
            5,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "rule", "Initial Card", "Old content", DisplayOrder: 0)
            }
        ));

        var manifestJson = """
        {
            "lessons": [
                {
                    "title": "Overwrite Target",
                    "skill": "reading",
                    "summary": "New Overwritten Summary",
                    "estimatedMinutes": 15,
                    "cards": [
                        { "type": "pitfall", "title": "New Card 1", "contentMarkdown": "New content 1" },
                        { "type": "example", "title": "New Card 2", "contentMarkdown": "New content 2" }
                    ]
                }
            ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(manifestJson));
        var result = await service.ImportLessonsAsync(certId, overwrite: true, ms, "manifest.json");

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(2, result.TotalCardsImported);

        var updated = await service.GetLessonAsync(existing.LessonId);
        Assert.NotNull(updated);
        Assert.Equal("New Overwritten Summary", updated.Summary);
        Assert.Equal(15, updated.EstimatedMinutes);
        Assert.Equal(2, updated.Cards.Count);
        Assert.Equal("pitfall", updated.Cards[0].CardType);
        Assert.Equal("example", updated.Cards[1].CardType);
    }

    [Fact]
    public async Task ImportLessonsAsync_ZipPackage_ExtractsAndSavesMediaAndRewritesMarkdown()
    {
        var (certId, _, _) = await SeedCertAndSkillAsync();
        var store = new RecordingFileStore();
        var service = CreateService(store);

        var manifestJson = """
        {
            "lessons": [
                {
                    "title": "Zip Lesson With Media",
                    "skill": "reading",
                    "cards": [
                        {
                            "type": "example",
                            "title": "Diagram Card",
                            "contentMarkdown": "See the flow below:\n![Flowchart](media/diagram.png)"
                        }
                    ]
                }
            ]
        }
        """;

        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            var mediaEntry = archive.CreateEntry("media/diagram.png");
            using (var stream = mediaEntry.Open())
            {
                var fakeImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                stream.Write(fakeImageBytes);
            }
        }
        zipMs.Position = 0;

        var result = await service.ImportLessonsAsync(certId, overwrite: false, zipMs, "package.zip");

        Assert.Equal(1, result.CreatedCount);
        Assert.NotEmpty(store.Files);

        var lessons = await service.GetLessonsAsync(certId);
        var created = lessons.First(l => l.Title == "Zip Lesson With Media");
        var detail = await service.GetLessonAsync(created.LessonId);

        Assert.NotNull(detail);
        Assert.Single(detail.Cards);
        // The media/diagram.png reference should be replaced with the saved /api/media/ path
        Assert.Contains("/api/media/", detail.Cards[0].ContentMarkdown);
        Assert.DoesNotContain("media/diagram.png", detail.Cards[0].ContentMarkdown);
    }

    [Fact]
    public async Task ExportLessonAsync_ProducesValidLessonManifestMatchingSchema()
    {
        var (certId, skillId, categoryId) = await SeedCertAndSkillAsync();
        var service = CreateService();

        var created = await service.CreateLessonAsync(new AdminLessonUpsertDto(
            certId,
            skillId,
            categoryId,
            "Exportable Lesson",
            "Summary to export",
            7,
            0,
            new List<AdminLessonCardUpsertDto>
            {
                new(null, "rule", "Rule Card", "Markdown text", DisplayOrder: 0),
                new(null, "micro_check", "Check Card", "Prompt text",
                    QuestionPrompt: "Which one?", OptionA: "Option 1", OptionB: "Option 2", OptionC: "Option 3", OptionD: "Option 4",
                    CorrectOption: "C", Explanation: "Expl", Rationale: "Rat", DisplayOrder: 1)
            }
        ));

        var manifest = await service.ExportLessonAsync(created.LessonId);

        Assert.NotNull(manifest);
        Assert.Single(manifest.Lessons);
        var item = manifest.Lessons[0];
        Assert.Equal("Exportable Lesson", item.Title);
        Assert.Equal(7, item.EstimatedMinutes);
        Assert.Equal(2, item.Cards.Count);
        Assert.Equal("rule", item.Cards[0].Type);
        Assert.Equal("micro_check", item.Cards[1].Type);
        Assert.Equal("C", item.Cards[1].CorrectOption);
        Assert.Equal("Rat", item.Cards[1].Rationale);
    }

    [Fact]
    public void GetTemplate_ReturnsValidSampleLessonWithAllFourCardTypes()
    {
        var service = CreateService();
        var template = service.GetTemplate();

        Assert.NotNull(template);
        Assert.Single(template.Lessons);
        var lesson = template.Lessons[0];
        Assert.Equal(4, lesson.Cards.Count);

        var cardTypes = lesson.Cards.Select(c => c.Type).ToHashSet();
        Assert.Contains("rule", cardTypes);
        Assert.Contains("pitfall", cardTypes);
        Assert.Contains("example", cardTypes);
        Assert.Contains("micro_check", cardTypes);
    }
}
