using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #185: an exam binds items instead of owning them. The schema test
/// <c>docs/schema-tests/domain23-examitems.sql</c> proves the constraints in T-SQL; these prove the
/// half a constraint cannot: that EF can write through the binding, and that the assembled paper
/// resolves its answer key through the pinned version rather than through the item's newest one.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExamItemBindingTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private AdminExamService CreateService() =>
        new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    /// <summary>
    /// A certificate with one reading section, one exam, and one pool item whose pinned version the
    /// paper binds. The item is authored on the pool, which is the only content model #269 leaves.
    /// </summary>
    private async Task<(int CertId, int ExamId, int ItemVersionId)> SeedBoundPaperAsync(
        string certCode, string keyLabel)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Bound Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'RDT', N'Reading', 'reading', 1, 60);
            DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'RDT');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, NavigationMode)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1, 'FULL_SANDBOX');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1), (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Bound paper', 'draft', 120, 'mock', 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);

        var position = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading",
            "The regional manager ------- the figures.", displayOrder: 1,
            keyLabel: keyLabel, explanation: "The pinned version says so.");

        return (cert.CertId, exam.ExamId, position.ItemVersionId);
    }

    /// <summary>
    /// AC1: a binding row exists per assembled item, carrying exam, section, certificate, the pinned
    /// version, display order and unit — and it saves through EF, which is what proves the
    /// <c>HasTrigger</c> declaration is present. Without it every save fails on the OUTPUT clause.
    /// </summary>
    [Fact]
    public async Task A_binding_saves_through_ef_and_carries_its_paper_position_and_unit()
    {
        var (certId, examId, _) = await SeedBoundPaperAsync(Unique("BND"), "B");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var binding = await db.ExamItems.AsNoTracking().SingleAsync(b => b.ExamId == examId);

        Assert.Equal(certId, binding.CertId);
        Assert.Equal(1, binding.DisplayOrder);
        Assert.Equal(1, binding.UnitNumber);
        Assert.Equal("reading", binding.Modality);
        Assert.True(binding.ItemVersionId > 0, "the binding must carry the pinned version id");
        Assert.True(binding.ItemId > 0, "the binding must carry the item id");

        // #185: the exam now declares a tier, defaulting to demo like an item does.
        var exam = await db.Exams.AsNoTracking().SingleAsync(e => e.ExamId == examId);
        Assert.Equal("demo", exam.ContentTier);
    }

    /// <summary>
    /// AC4, the one that has to be observed rather than asserted: the preview's answer key comes from
    /// the version the paper pinned, not from whatever the item says today.
    /// <para>
    /// <b>#269: the precedence half of this test died with the tables.</b> It used to author the paper on
    /// BOTH models with different keys, because "the pool wins over the legacy question row" is a claim
    /// about two sources and cannot be asserted with one. The legacy source is gone, so the surviving
    /// claim - the pinned version wins over the item's newest version - is the test below, which gives
    /// version 1 and version 2 different keys and different explanations and asserts the answer is
    /// version 1's. The test that lived here was named
    /// <c>The_preview_answer_key_is_the_pinned_version_not_the_legacy_question_row</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Re_versioning_the_item_leaves_the_paper_on_the_version_it_pinned()
    {
        var (_, examId, versionId) = await SeedBoundPaperAsync(Unique("RVR"), "B");

        await database.SqlAsync($"""
            UPDATE Items SET ContentTier = 'demo' WHERE ItemId = (SELECT ItemId FROM ExamItems WHERE ExamId = {examId});
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            SELECT ItemId, 2, N'The regional manager ------- the figures.',
                   N'review', N'will review', N'reviewing', N'to review', 'D',
                   N'Version two changed the key.'
            FROM Items WHERE ItemId = (SELECT ItemId FROM ExamItems WHERE ExamId = {examId});
            """);

        var preview = await CreateService().GetExamPreviewTakeAsync(examId, questionDisplayOrder: null);
        var answer = Assert.Single(preview.Answers);
        var option = preview.Take.CurrentQuestion.Options.Single(o => o.OptionId == Assert.Single(answer.CorrectOptionIds));

        Assert.Equal("B", option.Label);
        Assert.Equal("The pinned version says so.", answer.Explanation);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(versionId, await db.ExamItems.Where(b => b.ExamId == examId).Select(b => b.ItemVersionId).SingleAsync());
        Assert.Equal(2, await db.ItemVersions.CountAsync(v => v.ItemId == db.ExamItems.Where(b => b.ExamId == examId).Select(b => b.ItemId).Single()));
    }

    /// <summary>
    /// AC2/AC3, at the seam a service can see: the database refuses both, and EF surfaces the
    /// database's own error rather than writing past it. The schema test proves the exact constraint;
    /// this proves the refusal reaches a caller that used EF.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_a_cross_certificate_binding_and_ef_surfaces_it()
    {
        var (_, examId, versionId) = await SeedBoundPaperAsync(Unique("XCR"), "B");
        var other = Unique("OTH");

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{other}', N'Other Cert', 1);
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{other}');
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'LST', N'Listening', 'listening', 1);
            DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LST');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, @cert, 'listening', 'X1', N'Foreign section', 4, 1, 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        // the foreign section is the only one belonging to a certificate that is not the paper's
        var paperCertId = await db.ExamItems.Where(b => b.ExamId == examId).Select(b => b.CertId).SingleAsync();
        var foreign = await db.Sections.SingleAsync(s => s.Code == "X1" && s.CertId != paperCertId);
        var existing = await db.ExamItems.AsNoTracking().SingleAsync(b => b.ExamId == examId);

        db.ExamItems.Add(new BusinessObjects.Entities.ExamItem
        {
            ExamId = examId,
            SectionId = foreign.SectionId,
            CertId = existing.CertId,
            ItemVersionId = versionId,
            ItemId = existing.ItemId,
            Modality = "reading",
            DisplayOrder = 2,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("FK_ExamItems_SectionCert", error.InnerException?.Message ?? error.Message, StringComparison.Ordinal);
    }
}
