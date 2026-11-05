using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CertificateServiceTests(DatabaseFixture database)
{
    private async Task<int> SeedToeicAsync()
    {
        // A compact slice of the real TOEIC L&R catalog: two skills, sections with question
        // counts, five non-overlapping bands. Mirrors what the certificates page renders.
        await database.SqlAsync("""
            INSERT INTO Certificates (Code, Name, Description, IsActive)
              VALUES ('TOEIC', N'TOEIC Listening & Reading', N'The standard workplace English test.', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1, 45),
              (@cert, 'READ', N'Reading', 'reading', 2, 75);
            DECLARE @list INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @read INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
              (@list, @cert, 'listening', 'P1', N'Photographs', 4, 6, 1),
              (@list, @cert, 'listening', 'P2', N'Question-Response', 3, 25, 2),
              (@read, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 30, 1);

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A1', N'Beginner', 120, 220, 1),
              (@cert, 'A2', N'Elementary', 225, 545, 2),
              (@cert, 'B1', N'Intermediate', 550, 780, 3),
              (@cert, 'B2', N'Upper intermediate', 785, 940, 4),
              (@cert, 'C1', N'Advanced', 945, 990, 5);
            """);
        return 0;
    }

    [Fact]
    public async Task GetCatalog_returns_active_certificates_with_skills_sections_and_bands()
    {
        await SeedToeicAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new CertificateService(db);

        var catalog = await service.GetCatalogAsync();

        var toeic = Assert.Single(catalog, c => c.Code == "TOEIC");
        Assert.Equal("TOEIC", toeic.Code);
        Assert.Equal("The standard workplace English test.", toeic.Description);

        Assert.Equal(new[] { "LIST", "READ" }, toeic.Skills.Select(s => s.Code));
        var listening = toeic.Skills[0];
        Assert.Equal(45, listening.DurationMinutes);
        Assert.Equal(new[] { "P1", "P2" }, listening.Sections.Select(s => s.Code));
        Assert.Equal(6, listening.Sections[0].ExpectedQuestionCount);

        Assert.Equal(new[] { "A1", "A2", "B1", "B2", "C1" }, toeic.Bands.Select(b => b.Code));
        Assert.Equal(120, toeic.Bands.First().MinTotal);
        Assert.Equal(990, toeic.Bands.Last().MaxTotal);
    }

    [Fact]
    public async Task GetCatalog_skips_inactive_certificates()
    {
        await database.SqlAsync("""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('DEAD', N'Retired cert', 0);
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new CertificateService(db);

        var catalog = await service.GetCatalogAsync();

        Assert.DoesNotContain(catalog, c => c.Code == "DEAD");
    }

    [Fact]
    public async Task Database_has_triggers_and_views_from_the_schema_script()
    {
        // The model relies on HasTrigger declarations matching the real database (ADR-0001);
        // this test proves the test database actually carries them.
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var triggerCount = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.triggers")
            .SingleAsync();
        // #183: trg_Topics_ClosedVocabulary, which refuses a twenty-first topic.
        // #184: trg_ItemVersions_ScoredFieldsImmutable, which refuses an in-place scored-field edit.
        // #185: trg_ExamItems_ProductionBindsProduction and trg_Exams_TierKeepsContent, the two halves
        // of INV-6. Two rather than one because a rule that fires only when a binding is written is
        // bypassed by promoting the exam instead.
        // #190: trg_Items_LifecycleTransitions and trg_Items_NoHardDelete, the six states and the
        // no-delete rule.
        // #192: trg_GradingCriteria_FrozenWhenLocked,
        // trg_GradingCriterionDescriptors_FrozenWhenLocked,
        // trg_GradingRubricLocks_CompleteCanonicalRubric and
        // trg_GradingRubricLocks_IrreversibleOnceGraded - the seven-band grid and the lock.
        Assert.Equal(37, triggerCount);

        // #192: the four new triggers by name, because a count alone cannot tell a rename from a
        // removal, and the EF model declares these names.
        foreach (var name in new[]
                 {
                     "trg_GradingCriteria_FrozenWhenLocked",
                     "trg_GradingCriterionDescriptors_FrozenWhenLocked",
                     "trg_GradingRubricLocks_CompleteCanonicalRubric",
                     "trg_GradingRubricLocks_IrreversibleOnceGraded",
                 })
        {
            var present = await db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.triggers WHERE name = {name}")
                .SingleAsync();
            Assert.Equal(1, present);
        }

        var viewCount = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.views")
            .SingleAsync();
        // #162: vw_LearnerPathIsPremium went with the premium-path feature, leaving eight views.
        Assert.Equal(8, viewCount);
    }
}
