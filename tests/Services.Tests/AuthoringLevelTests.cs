using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #229 batch two: every authoring and import path reads and writes the level.
/// <para>
/// The guards here read the <c>Questions</c> columns through raw SQL rather than through the entity, so
/// a service that echoes the request into its DTO - or writes a level property EF has marked
/// <c>[NotMapped]</c> - cannot pass. The point of the batch is that the stored row speaks the level,
/// not that the reply does.
/// </para>
/// <para>
/// The other half was the difficulty. #231 dropped the column, so its absence is a schema fact now
/// (domain33-contract-retirement.sql) rather than something a write-level assertion can observe.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuthoringLevelTests(DatabaseFixture database)
{
    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    private AdminExamService CreateService() =>
        new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>One certificate with one section per modality, plus an admin for the import doors.</summary>
    private async Task<(int CertId, int AdminId, int ReadingSectionId, int WritingSectionId)> SeedAsync()
    {
        var code = Unique("LV");
        var email = $"level-{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin') INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{email}', 0x01, N'Level Admin', SYSDATETIMEOFFSET());

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Level cert', 1);
            DECLARE @c INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@c, 'READ', N'Reading', 'reading', 1), (@c, 'WRIT', N'Writing', 'writing', 2);
            DECLARE @r INT = (SELECT SkillId FROM Skills WHERE CertId = @c AND Code = 'READ');
            DECLARE @w INT = (SELECT SkillId FROM Skills WHERE CertId = @c AND Code = 'WRIT');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
              (@r, @c, 'reading', 'P7', N'Reading Comprehension', 4, 2, 1),
              (@w, @c, 'writing', 'W2', N'Respond to a written request', 4, 1, 1);

            -- The topic vocabulary is a closed set of twenty, so a canonical code is reused rather than
            -- invented; a synthetic one would break trg_Topics_ClosedVocabulary.
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching');
            """); 

        await using var db = Db();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var adminId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
        var reading = await db.Sections.SingleAsync(s => s.CertId == certId && s.Code == "P7");
        var writing = await db.Sections.SingleAsync(s => s.CertId == certId && s.Code == "W2");
        return (certId, adminId, reading.SectionId, writing.SectionId);
    }

    private async Task<(int ExamId, int GroupId)> SeedExamAsync(int certId, int sectionId)
    {
        var service = CreateService();
        var exam = await service.CreateExamAsync(
            new CreateAdminExamRequest(certId, Unique("Level exam "), 30, ExamPurposeDto.Mock));
        var group = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(sectionId));
        return (exam.ExamId, group.GroupId);
    }

    private static CreateQuestionRequest ReadingQuestion(int groupId, string stem, string? level) =>
        new(
            GroupId: groupId,
            Stem: stem,
            QuestionType: QuestionTypeDto.Mcq,
            CefrLevelCode: level,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "At six.", true, null),
                new AdminQuestionOptionInput(null, "B", "At seven.", false, null),
            ]);

    /// <summary>
    /// The stored column, read through SQL so the entity's own mapping cannot make the assertion true
    /// by accident. NULL is returned as the marker below, because a DBNull in a string projection is
    /// how a check like this silently reads as "".
    /// </summary>
    private async Task<(string Level, int Rows)> StoredLevelAsync(int questionId)
    {
        await using var db = Db();
        // SqlQuery (not SqlQueryRaw) so the id is a parameter, not spliced text.
        var level = await db.Database
            .SqlQuery<string>($"SELECT ISNULL(CefrLevel, '(null)') AS [Value] FROM Questions WHERE QuestionId = {questionId}")
            .SingleAsync();
        return (level, 1);
    }

    private async Task<List<string>> StoredLevelsAsync(int examId)
    {
        await using var db = Db();
        return await db.Database.SqlQuery<string>(
                $"SELECT ISNULL(CefrLevel, '(null)') AS [Value] FROM Questions WHERE ExamId = {examId} ORDER BY SectionId, DisplayOrder, QuestionId")
            .ToListAsync();
    }

    // ---- guard 1 and 2: an authored question lands with a level and no difficulty ----

    /// <summary>
    /// The stored row carries the level the author chose.
    /// <para>
    /// The input that would pass this while being wrong: a service that returns the level in its DTO
    /// and leaves the row unlevelled - every reply-level assertion passes and the row still lands in
    /// whatever cell a default means. That is why the assertion is on the column.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_authored_question_lands_with_the_chosen_level()
    {
        var (certId, _, readingSectionId, _) = await SeedAsync();
        var (examId, groupId) = await SeedExamAsync(certId, readingSectionId);

        var created = await CreateService().CreateQuestionAsync(
            examId, ReadingQuestion(groupId, "What time does the depot open?", "C1"));

        var (level, _) = await StoredLevelAsync(created.QuestionId);
        Assert.Equal("C1", level);

        // And the reply says the same thing, because the editor reads it back to show the author.
        Assert.Equal("C1", created.CefrLevelCode);
    }

    /// <summary>
    /// Every level on the ladder is storable. A mapping that only handled the four that happen to
    /// appear in the seed would pass the test above and fail the first C2 item.
    /// </summary>
    [Fact]
    public async Task Every_authored_level_reaches_the_row_unchanged()
    {
        var (certId, _, readingSectionId, _) = await SeedAsync();
        var (examId, groupId) = await SeedExamAsync(certId, readingSectionId);
        var service = CreateService();

        foreach (var level in CefrLevel.AuthoredLevels)
        {
            var created = await service.CreateQuestionAsync(
                examId, ReadingQuestion(groupId, $"What happens at {level.Code}?", level.Code));

            var (stored, _) = await StoredLevelAsync(created.QuestionId);
            Assert.Equal(level.Code, stored);
        }
    }

    // ---- guard 3, 4, 5: a missing or unknown level fails loudly ----

    /// <summary>
    /// A question authored with no level is refused and nothing is written.
    /// <para>
    /// The input that would pass this while being wrong: defaulting to A1 (the struct's own default) or
    /// to B1 (the middle of the ladder). Both produce a saved row, a 200, and an item in a cell nobody
    /// chose - so the assertion is that the refusal happens <b>and</b> the row count did not move.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_question_with_no_level_is_refused_and_nothing_is_written()
    {
        var (certId, _, readingSectionId, _) = await SeedAsync();
        var (examId, groupId) = await SeedExamAsync(certId, readingSectionId);

        var before = await StoredLevelsAsync(examId);

        var refused = await Assert.ThrowsAsync<AdminExamException>(() => CreateService().CreateQuestionAsync(
            examId, ReadingQuestion(groupId, "An unlevelled question.", null)));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains("cefrLevelCode", refused.Message, StringComparison.Ordinal);
        Assert.Contains("A1, A2, B1, B2, C1, C2", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, await StoredLevelsAsync(examId));
    }

    /// <summary>
    /// The pre-A1 floor is a reportable level, never an authored one. <c>CefrLevel.TryParseAuthored</c>
    /// refuses it, and the refusal must reach the author as a sentence rather than as the schema's
    /// CHECK (a 500) or a crash.
    /// <para>
    /// The input that would pass this while being wrong: <c>CefrLevel.TryParse</c>, which accepts the
    /// floor - the row then fails CK_Questions_Level at save and the admin reads a constraint error.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("pre_a1")]
    [InlineData("PRE_A1")]
    [InlineData("below_A1")]
    public async Task The_pre_A1_floor_is_refused_at_the_authoring_door_with_a_sentence(string floor)
    {
        var (certId, _, readingSectionId, _) = await SeedAsync();
        var (examId, groupId) = await SeedExamAsync(certId, readingSectionId);

        var refused = await Assert.ThrowsAsync<AdminExamException>(() => CreateService().CreateQuestionAsync(
            examId, ReadingQuestion(groupId, "A floored question.", floor)));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains("not an authored CEFR level", refused.Message, StringComparison.Ordinal);
        Assert.Contains("pre-A1 floor is reportable only", refused.Message, StringComparison.Ordinal);
        Assert.Empty(await StoredLevelsAsync(examId));
    }

    /// <summary>
    /// A code off the ladder is refused by name. The input that would pass this while being wrong is a
    /// service that "normalises" B3 to B2 or refuses only on length - the row would land at the wrong
    /// band and the author would never know.
    /// </summary>
    [Theory]
    [InlineData("B3")]
    [InlineData("Z9")]
    [InlineData("A1x")]
    public async Task A_level_off_the_authored_ladder_is_refused(string code)
    {
        var (certId, _, readingSectionId, _) = await SeedAsync();
        var (examId, groupId) = await SeedExamAsync(certId, readingSectionId);

        var refused = await Assert.ThrowsAsync<AdminExamException>(() => CreateService().CreateQuestionAsync(
            examId, ReadingQuestion(groupId, "An off-ladder question.", code)));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains(code, refused.Message, StringComparison.Ordinal);
        Assert.Empty(await StoredLevelsAsync(examId));
    }

    // ---- guard 6 and 7: the edit door ----

    /// <summary>
    /// An edit that states no level keeps the stored one.
    /// <para>
    /// The input that would pass this while being wrong: an edit that assigns
    /// <c>question.CefrLevel = Parse(request.CefrLevelCode)</c> unconditionally. The stem is saved, the
    /// response is a 200, and the item has silently lost its level - which is a worse version of
    /// defaulting it, because the level that was right is now gone.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_edit_that_states_no_level_keeps_the_stored_one()
    {
        var (certId, _, readingSectionId, _) = await SeedAsync();
        var (examId, groupId) = await SeedExamAsync(certId, readingSectionId);
        var service = CreateService();

        var created = await service.CreateQuestionAsync(
            examId, ReadingQuestion(groupId, "Original stem.", "B2"));

        var edited = await service.UpdateQuestionAsync(created.QuestionId, new UpdateQuestionRequest(
            GroupId: groupId,
            Stem: "Corrected stem.",
            QuestionType: QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "At six.", true, null),
                new AdminQuestionOptionInput(null, "B", "At seven.", false, null),
            ]));

        var (level, _) = await StoredLevelAsync(edited.QuestionId);
        Assert.Equal("B2", level);
        Assert.Equal("Corrected stem.", edited.Stem);
    }

    /// <summary>An edit that states a level writes it.</summary>
    [Fact]
    public async Task An_edit_that_states_a_level_writes_it()
    {
        var (certId, _, readingSectionId, _) = await SeedAsync();
        var (examId, groupId) = await SeedExamAsync(certId, readingSectionId);
        var service = CreateService();

        var created = await service.CreateQuestionAsync(
            examId, ReadingQuestion(groupId, "Original stem.", "A2"));

        var edited = await service.UpdateQuestionAsync(created.QuestionId, new UpdateQuestionRequest(
            GroupId: groupId,
            Stem: "Levelled up.",
            QuestionType: QuestionTypeDto.Mcq,
            CefrLevelCode: "C2",
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "At six.", true, null),
                new AdminQuestionOptionInput(null, "B", "At seven.", false, null),
            ]));

        var (level, _) = await StoredLevelAsync(edited.QuestionId);
        Assert.Equal("C2", level);
    }

    /// <summary>The edit door refuses the floor with the same sentence the create door does.</summary>
    [Fact]
    public async Task An_edit_cannot_author_at_the_floor()
    {
        var (certId, _, readingSectionId, _) = await SeedAsync();
        var (examId, groupId) = await SeedExamAsync(certId, readingSectionId);
        var service = CreateService();

        var created = await service.CreateQuestionAsync(
            examId, ReadingQuestion(groupId, "Original stem.", "B1"));

        var refused = await Assert.ThrowsAsync<AdminExamException>(() => service.UpdateQuestionAsync(
            created.QuestionId,
            new UpdateQuestionRequest(
                GroupId: groupId,
                Stem: "Floored.",
                QuestionType: QuestionTypeDto.Mcq,
                CefrLevelCode: "pre_a1",
                Options:
                [
                    new AdminQuestionOptionInput(null, "A", "At six.", true, null),
                    new AdminQuestionOptionInput(null, "B", "At seven.", false, null),
                ])));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Equal("B1", (await StoredLevelAsync(created.QuestionId)).Level);
    }

    // ---- guard 8 and 9: the manifest import ----

    private static Stream Utf8(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    /// <summary>A two-question reading manifest; the level of each question is a parameter.</summary>
    private static string Manifest(string title, string? firstLevel, string? secondLevel) => $$"""
        {
          "version": "1.0",
          "exam": {
            "title": "{{title}}",
            "purpose": "mock",
            "durationMinutes": 30,
            "questionCount": 2
          },
          "sections": [
            {
              "sectionCode": "P7",
              "groups": [
                {
                  "displayOrder": 1,
                  "cefrLevel": "B1",
                  "passage": "The depot opens at six.",
                  "questions": [
                    {
                      "stem": "What time does the depot open?",
                      "cefrLevel": {{(firstLevel is null ? "null" : $"\"{firstLevel}\"")}},
                      "questionType": "mcq",
                      "explanation": "The passage states the opening time.",
                      "options": [
                        { "label": "A", "text": "At six.", "isCorrect": true },
                        { "label": "B", "text": "At seven.", "isCorrect": false },
                        { "label": "C", "text": "At eight.", "isCorrect": false },
                        { "label": "D", "text": "At noon.", "isCorrect": false }
                      ]
                    },
                    {
                      "stem": "Who opens the depot?",
                      "cefrLevel": {{(secondLevel is null ? "null" : $"\"{secondLevel}\"")}},
                      "questionType": "mcq",
                      "explanation": "Only the supervisor opens the gate.",
                      "options": [
                        { "label": "A", "text": "Visitors.", "isCorrect": false },
                        { "label": "B", "text": "The supervisor.", "isCorrect": true },
                        { "label": "C", "text": "Nobody.", "isCorrect": false },
                        { "label": "D", "text": "The postman.", "isCorrect": false }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """; 

    /// <summary>
    /// An imported question lands on its legacy row with the level the file states.
    /// <para>
    /// The input that would pass this while being wrong: an import that writes the level to the pool
    /// <c>Items</c> row only (which #189 already did) and leaves <c>Questions</c> unlevelled. Every
    /// pool assertion in <c>PoolImportTests</c> passes; the paper still renders an unlevelled item.
    /// #231 deleted the difficulty column, so its absence is now a schema fact rather than a write
    /// this test can observe.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_imported_question_lands_with_a_level_and_no_difficulty()
    {
        var (certId, adminId, _, _) = await SeedAsync();

        var result = await new QuestionImportService(
                DbContextFactory.Create(database.ConnectionString), new RecordingFileStore(), TimeProvider.System)
            .ImportAsync(adminId, certId, "levelled.json", Utf8(Manifest(Unique("Levelled "), "A2", "C1")));

        Assert.Equal(ImportBatchStatusDto.Done, result.Status);

        var stored = await StoredLevelsAsync(result.ExamId!.Value);
        Assert.Equal(2, stored.Count);
        Assert.Equal("A2", stored[0]);
        Assert.Equal("C1", stored[1]);
    }

    /// <summary>
    /// A manifest question with no level is refused by name, and nothing is invented in its place.
    /// <para>
    /// The input that would pass this while being wrong: a fallback to the group's <c>cefrLevel</c> or
    /// to a constant. Both produce a never-empty level column, so any assertion of the shape "the
    /// level is not null" passes. The assertions are therefore the refusal message and the batch's own
    /// failed status, plus nothing written.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_manifest_question_with_no_level_is_refused()
    {
        var (certId, adminId, _, _) = await SeedAsync();

        var result = await new QuestionImportService(
                DbContextFactory.Create(database.ConnectionString), new RecordingFileStore(), TimeProvider.System)
            .ImportAsync(adminId, certId, "unlevelled.json",
                Utf8(Manifest(Unique("Unlevelled "), null, "B1")));

        Assert.Equal(ImportBatchStatusDto.Failed, result.Status);
        Assert.Null(result.ExamId);

        await using var db = Db();
        var errors = await db.ImportLogs.AsNoTracking()
            .Where(l => l.BatchId == result.BatchId && l.Level == "error")
            .Select(l => l.Message)
            .ToListAsync();

        var error = Assert.Single(errors);
        Assert.Contains("Section P7, group 1, question 1", error, StringComparison.Ordinal);
        Assert.Contains("cefrLevel", error, StringComparison.Ordinal);
        Assert.Contains("A1-C2", error, StringComparison.Ordinal);
        // D1 is an absolute, and the message has to say so where the author will read it.
        Assert.Contains("no difficulty-to-level mapping", error, StringComparison.Ordinal);
        Assert.Equal(0, await db.Questions.CountAsync(q => q.Stem == "What time does the depot open?" && q.CefrLevel == null));
    }

    /// <summary>
    /// The generated manifest template is a file an author imports, so its placeholders must carry a
    /// level; a template that cannot be imported is a broken starting point.
    /// <para>
    /// The input that would pass this while being wrong: placeholders that set only a level field the
    /// importer then ignores, which serialises to a structurally valid file whose every question is
    /// refused at import.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_generated_manifest_template_carries_a_level_on_every_placeholder()
    {
        var (certId, _, _, _) = await SeedAsync();

        var (stream, _, _) = await new QuestionImportService(
                DbContextFactory.Create(database.ConnectionString), new RecordingFileStore(), TimeProvider.System)
            .GenerateManifestTemplateAsync(certId);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var manifest = JsonNode.Parse(await reader.ReadToEndAsync())!;

        var questions = manifest["sections"]!.AsArray()
            .SelectMany(section => section!["groups"]!.AsArray())
            .SelectMany(group => group!["questions"]!.AsArray())
            .ToList();

        Assert.NotEmpty(questions);
        Assert.All(questions, question =>
            Assert.True(CefrLevel.TryParseAuthored(question!["cefrLevel"]?.GetValue<string>(), out _),
                $"template placeholder has no authored level: {question!.ToJsonString()}"));
    }

    // ---- guard 10: export writes the level ----

    /// <summary>
    /// The export carries the level, and does not carry a difficulty standing in for one.
    /// <para>
    /// The input that would pass this while being wrong: the writer emitting <c>cefrLevel</c> from the
    /// pool but still emitting <c>difficultyLevel</c> from a number somewhere. Since #231 there is no
    /// such field at all, so the assertion is the stronger one: the emitted JSON names no such member.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_export_writes_the_level_and_no_difficulty()
    {
        var (certId, _, readingSectionId, _) = await SeedAsync();
        var (examId, groupId) = await SeedExamAsync(certId, readingSectionId);

        await CreateService().CreateQuestionAsync(
            examId, ReadingQuestion(groupId, "What time does the depot open?", "B2"));

        var (stream, contentType, _) = await CreateService().ExportExamPackageAsync(examId, "json");
        Assert.Equal("application/json", contentType);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var manifest = JsonNode.Parse(await reader.ReadToEndAsync())!;
        var question = manifest["sections"]![0]!["groups"]![0]!["questions"]![0]!;

        Assert.Equal("B2", question["cefrLevel"]?.GetValue<string>());
        Assert.Null(question["difficultyLevel"]);
    }
}
