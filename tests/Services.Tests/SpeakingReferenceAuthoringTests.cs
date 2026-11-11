using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #152 finding 5: <c>Questions.SampleAnswerOrImageDesc</c> was writable only from SQL. A column the
/// product cannot fill is dead weight, and every existing Read Aloud question stayed unscripted as a
/// result — the feature's own reference could not be authored by the people who author questions.
/// <para>
/// This drives the same service the studio, the API and the import manifest call, so "the admin can
/// fill it" is asserted against the code path an admin actually takes.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SpeakingReferenceAuthoringTests(DatabaseFixture database)
{
    private AdminExamService CreateService() => new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    private Task PublishForFixtureAsync(int examId) =>
        database.SqlAsync($"UPDATE Exams SET Status = 'published' WHERE ExamId = {examId};");

    /// <summary>One certificate with a speaking section and a writing section, so both halves can be checked.</summary>
    private async Task<(int CertId, int SpeakingSectionId, int WritingSectionId)> SeedAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'SPEA', N'Speaking', 'speaking', 1),
              (@cert, 'WRIT', N'Writing', 'writing', 2);
            DECLARE @sS INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'SPEA');
            DECLARE @sW INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'WRIT');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sS, @cert, 'speaking', 'S1', N'Read aloud', 1, 1),
              (@sW, @cert, 'writing', 'W1', N'Essay', 1, 2);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var sections = await db.Sections.Where(s => s.CertId == cert.CertId).ToListAsync();
        return (cert.CertId,
            sections.Single(s => s.Code == "S1").SectionId,
            sections.Single(s => s.Code == "W1").SectionId);
    }

    [Fact]
    public async Task An_admin_can_author_a_read_aloud_script_and_read_it_back()
    {
        var certCode = $"RA_{Guid.NewGuid():N}"[..18];
        var (certId, speakingSectionId, _) = await SeedAsync(certCode);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"RA Exam {Guid.NewGuid():N}"[..25], 20, ExamPurposeDto.SpeakingPractice));
        var group = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(speakingSectionId));

        var created = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "Read the announcement aloud as clearly as you can.",
            QuestionTypeDto.FreeResponse,
            PrepSeconds: 5,
            ResponseSeconds: 45,
            SampleAnswerOrImageDesc: "Attention all shoppers. The sale ends today at 9:00 PM.",
            ReferenceKind: ReferenceKindDto.Script, CefrLevelCode: "B1"));

        Assert.Equal("Attention all shoppers. The sale ends today at 9:00 PM.", created.SampleAnswerOrImageDesc);
        Assert.Equal(ReferenceKindDto.Script, created.ReferenceKind);

        // The studio reads it back, so the editor can show what was saved.
        var studio = await service.GetExamStudioAsync(exam.ExamId);
        var studioQuestion = studio.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Questions)
            .Single(q => q.QuestionId == created.QuestionId);
        Assert.Equal("Attention all shoppers. The sale ends today at 9:00 PM.", studioQuestion.SampleAnswerOrImageDesc);
        Assert.Equal(ReferenceKindDto.Script, studioQuestion.ReferenceKind);

        // And it is really in the column, not only on the DTO.
        // #272: left legacy - the subject is the LEGACY authoring service's write, so its row is what proves it.
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.Questions.AsNoTracking().SingleAsync(q => q.QuestionId == created.QuestionId);
        Assert.Equal("Attention all shoppers. The sale ends today at 9:00 PM.", stored.SampleAnswerOrImageDesc);
        Assert.Equal("script", stored.ReferenceKind);

        // An edit can replace it, and clear it.
        var updated = await service.UpdateQuestionAsync(created.QuestionId, new UpdateQuestionRequest(
            GroupId: null,
            Stem: created.Stem,
            QuestionType: QuestionTypeDto.FreeResponse,
            PrepSeconds: 5,
            ResponseSeconds: 45,
            SampleAnswerOrImageDesc: "Updated announcement text.",
            ReferenceKind: ReferenceKindDto.ImageDesc, CefrLevelCode: "B1"));
        Assert.Equal("Updated announcement text.", updated.SampleAnswerOrImageDesc);
        Assert.Equal(ReferenceKindDto.ImageDesc, updated.ReferenceKind);

        var cleared = await service.UpdateQuestionAsync(created.QuestionId, new UpdateQuestionRequest(
            GroupId: null,
            Stem: created.Stem,
            QuestionType: QuestionTypeDto.FreeResponse,
            PrepSeconds: 5,
            ResponseSeconds: 45, CefrLevelCode: "B1"));
        Assert.Null(cleared.SampleAnswerOrImageDesc);
        Assert.Null(cleared.ReferenceKind);
    }

    /// <summary>
    /// The other half of finding 4's fix: a reference is only usable when the author also says what it
    /// is. Text with no kind cannot be classified, and guessing is what produced the original bug.
    /// </summary>
    [Fact]
    public async Task A_reference_needs_both_the_text_and_what_it_is()
    {
        var certCode = $"RV_{Guid.NewGuid():N}"[..18];
        var (certId, speakingSectionId, _) = await SeedAsync(certCode);
        var service = CreateService();
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"RV Exam {Guid.NewGuid():N}"[..25], 20, ExamPurposeDto.SpeakingPractice));
        var group = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(speakingSectionId));

        var textOnly = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId, "Read aloud.", QuestionTypeDto.FreeResponse,
            ResponseSeconds: 45, SampleAnswerOrImageDesc: "Some words.", CefrLevelCode: "B1")));
        Assert.Equal(ApiError.Codes.Validation, textOnly.Code);
        Assert.Contains("both", textOnly.Message, StringComparison.OrdinalIgnoreCase);

        var kindOnly = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId, "Read aloud.", QuestionTypeDto.FreeResponse,
            ResponseSeconds: 45, ReferenceKind: ReferenceKindDto.Script, CefrLevelCode: "B1")));
        Assert.Equal(ApiError.Codes.Validation, kindOnly.Code);
    }

    /// <summary>
    /// A reference on an exact-match question is a rubric nobody will read. The author is told so at
    /// save time rather than discovering it when a learner is graded against it.
    /// </summary>
    [Fact]
    public async Task Only_a_spoken_question_can_carry_a_reference()
    {
        var certCode = $"RC_{Guid.NewGuid():N}"[..18];
        var (certId, _, writingSectionId) = await SeedAsync(certCode);
        var service = CreateService();
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"RC Exam {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.WritingPractice));
        var group = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(writingSectionId));

        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId, "Choose the correct answer.", QuestionTypeDto.InlineCompletion,
            AcceptedAnswersJson: "[\"x\"]",
            SampleAnswerOrImageDesc: "x", ReferenceKind: ReferenceKindDto.Script, CefrLevelCode: "B1")));

        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("free-response", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The import manifest can carry it too, which is how an existing exam gets its Read Aloud scripts
    /// without anyone opening the studio. A manifest that could not express the column would leave
    /// every imported Read Aloud question unscripted, exactly as they were before this fix.
    /// </summary>
    [Fact]
    public void The_import_manifest_can_express_a_script()
    {
        var root = DatabaseFixture.RepoRootPath;

        var manifest = File.ReadAllText(Path.Combine(root, "src", "Services", "Admin", "Imports", "Manifest", "ImportManifestQuestion.cs"));
        Assert.Contains("SampleAnswerOrImageDesc", manifest);
        Assert.Contains("ReferenceKind", manifest);

        // Every writer of a Question row carries the pair, so an import cannot silently drop it.
        foreach (var relative in new[]
                 {
                     Path.Combine("src", "Services", "Admin", "Imports", "QuestionImportService.Persistence.cs"),
                     Path.Combine("src", "Services", "Admin", "Imports", "ExamQuickIngestService.Commit.cs"),
                     Path.Combine("src", "Services", "Admin", "Exams", "AdminExamService.Questions.cs"),
                 })
        {
            var source = File.ReadAllText(Path.Combine(root, relative));
            Assert.Contains("SampleAnswerOrImageDesc", source);
            Assert.Contains("ReferenceKind", source);
        }

        // The plain-text importer understands a SCRIPT: line, which is how a bulk author writes one.
        var parser = File.ReadAllText(Path.Combine(root, "src", "Services", "ExamQuickIngestService.cs"));
        Assert.Contains("ScriptRegex", parser);
        Assert.Contains("IMAGE_DESC", parser);
    }
}
