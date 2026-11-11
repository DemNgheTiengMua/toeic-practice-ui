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
/// resolves its answer key through the pinned version rather than through the legacy question row.
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
    /// The same paper authored on BOTH models, with different keys.
    /// <para>
    /// <b>#269: this fixture stays on legacy rows on purpose, and it is the paper's own test that needs
    /// them.</b> <c>The_preview_answer_key_is_the_pinned_version_not_the_legacy_question_row</c> asserts
    /// that the pool path WINS over the legacy one, which is a statement about two sources - authoring
    /// only the pool would leave one source and the assertion could no longer fail. The legacy reads it
    /// contrasts against still exist until the three tables are dropped, so this is the only proof they
    /// do not take precedence. It is deleted with them, not migrated before them.
    /// </para>
    /// </summary>
    private async Task<(int ExamId, string PinnedExplanation)> SeedPaperOnBothModelsAsync(
        string certCode, string keyLabel, string legacyKeyLabel)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Bound Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'RDT', N'Reading', 'reading', 1, 60);
            DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'RDT');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, NavigationMode)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1, 'FULL_SANDBOX');
            DECLARE @sec INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1), (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Bound paper', 'draft', 120, 'mock', 1);
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- the legacy shape the sitting projection still reads, with its own key
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, Explanation, DisplayOrder)
            VALUES (@grp, @exam, @sec, @skill, N'The regional manager ------- the figures.', 'A2', 'mcq',
                    N'Legacy explanation.', 1);
            DECLARE @q INT = SCOPE_IDENTITY();

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q, 'mcq', 'A', N'review', 0),
              (@q, 'mcq', 'B', N'will review', {(legacyKeyLabel == "B" ? 1 : 0)}),
              (@q, 'mcq', 'C', N'reviewing', {(legacyKeyLabel == "C" ? 1 : 0)}),
              (@q, 'mcq', 'D', N'to review', 0);

            -- the pool item and the pinned version, with its own key. The topic is shared across the
            -- whole fixture, so it is reused rather than re-inserted.
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GRAM', N'Morphosyntactic structure and lexical precision');
            DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-GRAM');

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', '{certCode}');
            DECLARE @item INT = SCOPE_IDENTITY();

            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@item, 1, N'The regional manager ------- the figures.',
                    N'review', N'will review', N'reviewing', N'to review', '{keyLabel}',
                    N'The pinned version says so.');
            DECLARE @ver INT = SCOPE_IDENTITY();

            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
            VALUES (@exam, @sec, @cert, @ver, @item, 'reading', 1, 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var exam = await db.Exams.SingleAsync(e => e.Cert.Code == certCode);
        return (exam.ExamId, "The pinned version says so.");
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
    /// the version the paper pinned, not from the legacy question row. The fixture gives the two
    /// different keys and different explanations, so only one path can produce the expected answer.
    /// <para>
    /// #269: this is the test that keeps its legacy rows - it is a claim about precedence between two
    /// sources, so it needs both. See <see cref="SeedPaperOnBothModelsAsync"/>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_preview_answer_key_is_the_pinned_version_not_the_legacy_question_row()
    {
        var (examId, pinnedExplanation) = await SeedPaperOnBothModelsAsync(Unique("PVW"), keyLabel: "C", legacyKeyLabel: "B");

        var preview = await CreateService().GetExamPreviewTakeAsync(examId, questionDisplayOrder: null);

        Assert.Equal(1, preview.Take.TotalQuestions);
        var answer = Assert.Single(preview.Answers);

        var correct = Assert.Single(answer.CorrectOptionIds);
        var option = preview.Take.CurrentQuestion.Options.Single(o => o.OptionId == correct);
        Assert.Equal("C", option.Label);
        Assert.Equal(pinnedExplanation, answer.Explanation);
    }

    /// <summary>
    /// The other direction, so the test above is not passing by accident: with the same fixture and
    /// the legacy key agreeing, a re-versioned item does not move the paper. The binding pinned
    /// version 1, so version 2's new key changes a *future* paper, never this one.
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
