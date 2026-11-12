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
/// The guards here read the stored column through raw SQL rather than through the entity, so a service
/// that echoes the request into its DTO - or writes a level property EF has marked <c>[NotMapped]</c> -
/// cannot pass. The point of the batch is that the stored row speaks the level, not that the reply does.
/// </para>
/// <para>
/// #269: the authoring door is the pool's. <c>AdminExamService.CreateQuestionAsync</c> and
/// <c>UpdateQuestionAsync</c> are gone with <c>Questions</c>, so the create and edit guards now drive
/// <c>IAdminItemService.CreateAsync</c> / <c>UpdateAsync</c> and read <c>Items.CefrLevel</c>. The level
/// rules themselves are unchanged - <c>RequireAuthoredLevel</c> is the same refusal, by the same
/// sentence, at the same boundary.
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

    private AdminItemService CreateService() =>
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

    /// <summary>
    /// The topic an authored item joins. Reading's canonical code is reused, because the vocabulary is
    /// closed at twenty and a synthetic topic would break <c>trg_Topics_ClosedVocabulary</c>.
    /// </summary>
    private async Task<int> ReadingTopicIdAsync()
    {
        await using var db = Db();
        return await db.Topics.Where(t => t.Modality == "reading" && t.Code == "R-DET")
            .Select(t => t.TopicId).SingleAsync();
    }

    private CreatePoolItemRequest ReadingItem(int topicId, string stem, string? level) =>
        new(
            ModalityDto.Reading,
            topicId,
            level,
            "sentence_cloze",
            stem,
            OptionA: "At six.",
            OptionB: "At seven.",
            OptionC: "At eight.",
            OptionD: "At noon.",
            KeyLabel: "A",
            Explanation: "The passage states the opening time.",
            RationaleA: "Correct: the passage states the opening time.",
            RationaleB: "Wrong: the passage never gives a time for the ledger.",
            RationaleC: "Wrong: the supervisor opens the gate, not the depot.",
            RationaleD: "Wrong: waiting outside is what visitors do.");

    /// <summary>The stored column, read through SQL so the entity's own mapping cannot make the assertion
    /// true by accident. NULL is returned as the marker below, because a DBNull in a string projection is
    /// how a check like this silently reads as "".</summary>
    private async Task<string> StoredLevelAsync(int itemId)
    {
        await using var db = Db();
        return await db.Database
            .SqlQuery<string>($"SELECT ISNULL(CefrLevel, '(null)') AS [Value] FROM Items WHERE ItemId = {itemId}")
            .SingleAsync();
    }

    private async Task<List<string>> StoredLevelsAsync(int examId)
    {
        await using var db = Db();
        // #266: the level a learner's paper carries is the ITEM's, and the item is what the roster binds.
        return await db.Database.SqlQuery<string>(
                $"SELECT ISNULL(i.CefrLevel, '(null)') AS [Value] FROM ExamItems b JOIN Items i ON i.ItemId = b.ItemId WHERE b.ExamId = {examId} ORDER BY b.SectionId, b.DisplayOrder, b.ExamItemId")
            .ToListAsync();
    }

    private async Task<int> StoredItemCountAsync()
    {
        await using var db = Db();
        return await db.Items.CountAsync();
    }

    // ---- guard 1 and 2: an authored item lands with a level and no difficulty ----

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
        await SeedAsync();
        var topicId = await ReadingTopicIdAsync();

        var created = await CreateService().CreateAsync(
            ReadingItem(topicId, "What time does the depot open?", "C1"));

        Assert.Equal("C1", await StoredLevelAsync(created.ItemId));

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
        await SeedAsync();
        var topicId = await ReadingTopicIdAsync();
        var service = CreateService();

        foreach (var level in CefrLevel.AuthoredLevels)
        {
            var created = await service.CreateAsync(
                ReadingItem(topicId, $"What happens at {level.Code}?", level.Code));

            Assert.Equal(level.Code, await StoredLevelAsync(created.ItemId));
        }
    }

    // ---- guard 3, 4, 5: a missing or unknown level fails loudly ----

    /// <summary>
    /// An item authored with no level is refused and nothing is written.
    /// <para>
    /// The input that would pass this while being wrong: defaulting to A1 (the struct's own default) or
    /// to B1 (the middle of the ladder). Both produce a saved row, a 200, and an item in a cell nobody
    /// chose - so the assertion is that the refusal happens <b>and</b> the row count did not move.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_question_with_no_level_is_refused_and_nothing_is_written()
    {
        await SeedAsync();
        var topicId = await ReadingTopicIdAsync();

        var before = await StoredItemCountAsync();

        var refused = await Assert.ThrowsAsync<AdminItemException>(() => CreateService().CreateAsync(
            ReadingItem(topicId, "An unlevelled question.", null)));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains("not an authored CEFR level", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, await StoredItemCountAsync());
    }

    /// <summary>
    /// The pre-A1 floor is a reportable level, never an authored one. <c>CefrLevel.TryParseAuthored</c>
    /// refuses it, and the refusal must reach the author as a sentence rather than as the schema's
    /// CHECK (a 500) or a crash.
    /// </summary>
    [Theory]
    [InlineData("pre_a1")]
    [InlineData("PRE_A1")]
    [InlineData("below_A1")]
    public async Task The_pre_A1_floor_is_refused_at_the_authoring_door_with_a_sentence(string floor)
    {
        await SeedAsync();
        var topicId = await ReadingTopicIdAsync();

        var refused = await Assert.ThrowsAsync<AdminItemException>(() => CreateService().CreateAsync(
            ReadingItem(topicId, "A floored question.", floor)));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains("not an authored CEFR level", refused.Message, StringComparison.Ordinal);
        Assert.Contains("pre-A1 floor is reportable only", refused.Message, StringComparison.Ordinal);
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
        await SeedAsync();
        var topicId = await ReadingTopicIdAsync();

        var refused = await Assert.ThrowsAsync<AdminItemException>(() => CreateService().CreateAsync(
            ReadingItem(topicId, "An off-ladder question.", code)));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains(code, refused.Message, StringComparison.Ordinal);
    }

    // ---- guard 6 and 7: the edit door ----

    /// <summary>
    /// An edit is refused at the floor, and the stored level is untouched afterwards.
    /// <para>
    /// <b>What changed.</b> The level is not an editable field on the pool: <c>UpdatePoolItemRequest</c>
    /// carries no <c>CefrLevelCode</c>, because moving an item between cells is a different act from
    /// editing its content (see the record's own doc comment). So "an edit that states no level keeps the
    /// stored one" and "an edit that states a level writes it" both have no request field to exercise -
    /// the level CANNOT move, which is a stronger guarantee than the one they asserted. What still has a
    /// door is the floor: a create is the refusal, and it leaves no row (guard 3).
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_edit_that_states_no_level_keeps_the_stored_one()
    {
        await SeedAsync();
        var topicId = await ReadingTopicIdAsync();
        var service = CreateService();

        var created = await service.CreateAsync(
            ReadingItem(topicId, "Original stem.", "B2"));

        var edited = await service.UpdateAsync(created.ItemId, new UpdatePoolItemRequest(
            Stem: "Corrected stem.",
            OptionA: "At six.",
            OptionB: "At seven.",
            OptionC: "At eight.",
            OptionD: "At noon.",
            KeyLabel: "A",
            Explanation: "The passage states the opening time.",
            RationaleA: "Correct: the passage states the opening time.",
            RationaleB: "Wrong.",
            RationaleC: "Wrong.",
            RationaleD: "Wrong."));

        Assert.Equal("Corrected stem.", edited.Stem);
        // The level is the item's, not the version's, and nothing on the edit path can move it.
        Assert.Equal("B2", await StoredLevelAsync(created.ItemId));
        Assert.Equal("B2", edited.CefrLevelCode);
    }

    // ---- guard 8 and 9: the manifest import ----

    private static Stream Utf8(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// A two-question reading manifest; the level of each question is a parameter.
    /// <para>
    /// The stems are parameters too, and a caller that asserts "nothing was written" must pass a stem no
    /// other class writes: the fixture database is shared, so a fixed stem makes the claim about the
    /// whole assembly's rows rather than about this import. Measured: <c>PoolImportTests</c> imports the
    /// same depot stem, and the assertion read 12.
    /// </para>
    /// </summary>
    private static string Manifest(string title, string firstStem, string? firstLevel, string? secondLevel) => $$"""
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
                      "stem": "{{firstStem}}",
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
            .ImportAsync(adminId, certId, "levelled.json",
                Utf8(Manifest(Unique("Levelled "), "What time does the depot open?", "A2", "C1")));

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

        // A stem no other class writes: the "nothing was written" assertion below is a database-wide
        // absence, and the fixture is shared.
        var stem = Unique("Unlevelled stem ");

        var result = await new QuestionImportService(
                DbContextFactory.Create(database.ConnectionString), new RecordingFileStore(), TimeProvider.System)
            .ImportAsync(adminId, certId, "unlevelled.json",
                Utf8(Manifest(Unique("Unlevelled "), stem, null, "B1")));

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
        // #269: Questions is gone, so "the refused import wrote no row" is measured on the pool, where
        // the write would have landed. The stem lives on the version, not the item.
        Assert.Equal(0, await db.ItemVersions.CountAsync(v => v.Stem == stem));
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
        var topicId = await ReadingTopicIdAsync();
        var admin = new AdminExamService(Db(), TimeProvider.System);

        var exam = await admin.CreateExamAsync(new CreateAdminExamRequest(
            certId, Unique("Level export "), 30, ExamPurposeDto.Mock));
        var created = await CreateService().CreateAsync(
            ReadingItem(topicId, "What time does the depot open?", "B2"));
        await admin.BindItemAsync(exam.ExamId, new BindPoolItemRequest(readingSectionId, created.ItemVersionId, 1));

        var (stream, contentType, _) = await admin.ExportExamPackageAsync(exam.ExamId, "json");
        Assert.Equal("application/json", contentType);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var manifest = JsonNode.Parse(await reader.ReadToEndAsync())!;
        var question = manifest["sections"]![0]!["groups"]![0]!["questions"]![0]!;

        Assert.Equal("B2", question["cefrLevel"]?.GetValue<string>());
        Assert.Null(question["difficultyLevel"]);
    }
}
