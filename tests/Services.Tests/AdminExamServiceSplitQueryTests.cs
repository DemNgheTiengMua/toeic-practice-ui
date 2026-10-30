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
        var certCode = $"SPLIT_{Guid.NewGuid():N}"[..18];
        int examId;

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

        await using (var seed = DbContextFactory.Create(database.ConnectionString))
        {
            var service = new AdminExamService(seed, TimeProvider.System);
            var cert = await seed.Certificates.SingleAsync(c => c.Code == certCode);
            var sectionId = (await seed.Sections.SingleAsync(s => s.CertId == cert.CertId)).SectionId;

            var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
                cert.CertId, $"Split {Guid.NewGuid():N}"[..25], 40, ExamPurposeDto.Mock));
            examId = exam.ExamId;

            // Two groups, each with a question carrying two options: the shape that used to
            // explode into groups x questions x options rows.
            var g1 = await service.CreateQuestionGroupAsync(examId, new CreateQuestionGroupRequest(sectionId));
            var g2 = await service.CreateQuestionGroupAsync(examId, new CreateQuestionGroupRequest(sectionId, DisplayOrder: 2));
            foreach (var group in new[] { g1, g2 })
            {
                await service.CreateQuestionAsync(examId, new CreateQuestionRequest(
                    group.GroupId, "Pick one", 1, QuestionTypeDto.Mcq,
                    Options:
                    [
                        new AdminQuestionOptionInput(null, "A", "first", IsCorrect: true, null),
                        new AdminQuestionOptionInput(null, "B", "second", IsCorrect: false, null),
                    ]));
            }
        }

        await using var db = CreateStrictDb();
        var audit = new AdminExamService(db, TimeProvider.System);

        // Throws InvalidOperationException citing 20504 if the split-query call is ever removed.
        var result = await audit.AuditExamPublishReadinessAsync(examId);

        Assert.NotNull(result);
    }
}
