using System.Text;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// F5: the readiness audit loads two collection navigations (QuestionGroups, then their option
/// navigations). Unsplitting that is a Cartesian product, which EF warns about (20504) and which
/// multiplies rows by every option added. The guard below turns that warning into a failure, so
/// dropping .AsSplitQuery() breaks a test instead of quietly reintroducing the latency.
/// The studio, question list and both exports load the same groups → questions → options graph
/// (plus categories) and are guarded the same way.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminExamServiceSplitQueryTests(DatabaseFixture database)
{
    /// <summary>The app's DbContext stack, except a multi-collection include is fatal.</summary>
    private CompanionDbContext CreateStrictDb()
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer(database.ConnectionString)
            .ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;
        return new CompanionDbContext(options);
    }

    [Fact]
    public async Task The_readiness_audit_splits_its_collection_includes()
    {
        var examId = await SeedExamWithTwoGroupsAsync();

        await using var db = CreateStrictDb();
        var audit = new AdminExamService(db, TimeProvider.System);

        // Throws InvalidOperationException citing 20504 if the split-query call is ever removed.
        var result = await audit.AuditExamPublishReadinessAsync(examId);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task The_exam_studio_splits_its_collection_includes_and_keeps_every_question_and_option()
    {
        var examId = await SeedExamWithTwoGroupsAsync();

        await using var db = CreateStrictDb();
        var studio = await new AdminExamService(db, TimeProvider.System).GetExamStudioAsync(examId);

        var groups = studio.Sections.SelectMany(s => s.Groups).ToList();
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(new[] { "A", "B" }, Assert.Single(g.Questions).Options.Select(o => o.Label)));
    }

    [Fact]
    public async Task The_question_list_splits_its_collection_includes_and_keeps_every_question_and_option()
    {
        var examId = await SeedExamWithTwoGroupsAsync();

        await using var db = CreateStrictDb();
        var list = await new AdminExamService(db, TimeProvider.System).GetExamQuestionsAsync(examId);

        Assert.Equal(2, list.Groups.Count);
        Assert.All(list.Groups, g => Assert.Equal(new[] { "A", "B" }, Assert.Single(g.Questions).Options.Select(o => o.Label)));
    }

    [Fact]
    public async Task Both_exam_exports_split_their_collection_includes()
    {
        var examId = await SeedExamWithTwoGroupsAsync();

        await using var db = CreateStrictDb();
        var fromStudio = await new AdminExamService(db, TimeProvider.System).ExportExamPackageAsync(examId, "json");
        var fromImports = await new QuestionImportService(db, new RecordingFileStore(), TimeProvider.System).ExportExamPackageAsync(examId, "json");

        foreach (var (stream, _, _) in new[] { fromStudio, fromImports })
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var manifest = await reader.ReadToEndAsync();
            Assert.Contains("first", manifest);
            Assert.Contains("second", manifest);
        }
    }

    /// <summary>
    /// Two groups, each with a question carrying two options: the shape that used to explode into
    /// groups x questions x options rows.
    /// </summary>
    private async Task<int> SeedExamWithTwoGroupsAsync()
    {
        var certCode = $"SPLIT_{Guid.NewGuid():N}"[..18];

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            DECLARE @certStr VARCHAR(10) = CAST(@cert AS VARCHAR(10));

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'SL_S_' + @certStr, N'Listening', 'listening', 1);

            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@sL, @cert, 'listening', 'SL_' + @certStr, N'Listening Part 1', 1);
            """);

        await using var seed = DbContextFactory.Create(database.ConnectionString);
        var service = new AdminExamService(seed, TimeProvider.System);
        var cert = await seed.Certificates.SingleAsync(c => c.Code == certCode);
        var sectionId = (await seed.Sections.SingleAsync(s => s.CertId == cert.CertId)).SectionId;

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            cert.CertId, $"Split {Guid.NewGuid():N}"[..25], 40, ExamPurposeDto.Mock));

        var g1 = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(sectionId));
        var g2 = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(sectionId, DisplayOrder: 2));
        foreach (var group in new[] { g1, g2 })
        {
            await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
                group.GroupId, "Pick one", 1, QuestionTypeDto.Mcq,
                Options:
                [
                    new AdminQuestionOptionInput(null, "A", "first", IsCorrect: true, null),
                    new AdminQuestionOptionInput(null, "B", "second", IsCorrect: false, null),
                ]));
        }

        return exam.ExamId;
    }
}
