using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #189: import paths write the pool.
/// <para>
/// These drive the real services against the real schema, and then read the pool tables directly, so
/// no test can pass by trusting a projection the service itself produced. The legacy <c>Questions</c>
/// table is counted alongside: an import that only wrote the pool would be the opposite mistake, and
/// this ticket is the expand half - both shapes are written and both stay readable until #231.
/// </para>
/// <para>
/// <c>topicCode</c>/<c>cefrLevel</c> replace the legacy <c>categoryCode</c>/<c>difficultyLevel</c>
/// where a pool row is expected, because D1 forbids a difficulty-to-level mapping and D3 replaces
/// the categories. The legacy codes are still exercised where the test is about the legacy shape.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolImportTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    private QuestionImportService CreateService(RecordingFileStore? store = null) =>
        new(DbContextFactory.Create(database.ConnectionString), store ?? new RecordingFileStore(), TimeProvider.System);

    private ExamQuickIngestService CreateQuickIngest() =>
        new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    /// <summary>
    /// A certificate shaped like the four skills, one section each, plus an admin. Reading sections
    /// are named P5/P7 because the sample manifests use those codes (ADR-0007).
    /// </summary>
    private async Task<(int CertId, int AdminId, int ReadingSectionId, int ListeningSectionId, int WritingSectionId)> SeedAsync()
    {
        var code = Unique("PL");
        var email = $"pool-import-{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin') INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{email}', 0x01, N'Pool Import Admin', SYSDATETIMEOFFSET());

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Pool import cert', 1);
            DECLARE @c INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@c, 'LIST', N'Listening', 'listening', 1, 45),
              (@c, 'READ', N'Reading', 'reading', 2, 45),
              (@c, 'WRIT', N'Writing', 'writing', 3, 60);
            DECLARE @l INT = (SELECT SkillId FROM Skills WHERE CertId = @c AND Code = 'LIST');
            DECLARE @r INT = (SELECT SkillId FROM Skills WHERE CertId = @c AND Code = 'READ');
            DECLARE @w INT = (SELECT SkillId FROM Skills WHERE CertId = @c AND Code = 'WRIT');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
              (@l, @c, 'listening', 'P1', N'Photographs', 4, 1, 1),
              (@r, @c, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1),
              (@r, @c, 'reading', 'P7', N'Reading Comprehension', 4, 2, 2),
              (@w, @c, 'writing', 'W2', N'Respond to a written request', 4, 1, 1);
            INSERT INTO QuestionCategories (CertId, Code, Name) VALUES
              (@c, 'grammar_prep', N'Preposition'), (@c, 'reading_inference', N'Inference');

            -- The topic vocabulary is the canonical twenty and the Services fixture database loads
            -- the schema only, so the topics these tests name are inserted here by their own guard.
            -- Canonical codes keep the closed set closed: a subset of twenty can never break the
            -- trigger, and a synthetic code would.
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-INF')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-INF', N'Bridging inference and logical deduction');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GRAM', N'Morphosyntactic structure and lexical precision');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-GIST')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GIST', N'Global thesis, purpose, paragraph main idea');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'listening' AND Code = 'L-GIST')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('listening', 'L-GIST', N'Global gist, purpose, macro-topic');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'writing' AND Code = 'W-CORR')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('writing', 'W-CORR', N'Transactional correspondence and register control');
            """); 

        await using var db = Db();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var adminId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
        var reading = await db.Sections.SingleAsync(s => s.CertId == certId && s.Code == "P7");
        var listening = await db.Sections.SingleAsync(s => s.CertId == certId && s.Code == "P1");
        var writing = await db.Sections.SingleAsync(s => s.CertId == certId && s.Code == "W2");
        return (certId, adminId, reading.SectionId, listening.SectionId, writing.SectionId);
    }

    private static Stream Utf8(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// A manifest with one passage and two children on it - the shape that must import as one
    /// stimulus and two children. Every field the pool needs is stated, because a pool write with a
    /// guessed level or topic is exactly what D1 and D3 forbid.
    /// </summary>
    private static string TwoChildManifest(string title) => $$"""
        {
          "version": "1.0",
          "exam": {
            "title": "{{title}}",
            "purpose": "mock",
            "deliveryMode": "strict_exam",
            "durationMinutes": 30,
            "sourceName": "Pool import test",
            "questionCount": 2
          },
          "sections": [
            {
              "sectionCode": "P7",
              "groups": [
                {
                  "displayOrder": 1,
                  "cefrLevel": "B1",
                  "domainContext": "workplace",
                  "passage": "The depot opens at six. Staff who arrive before then sign the night ledger.",
                  "questions": [
                    {
                      "displayOrder": 1,
                      "stem": "What time does the depot open?",
                      "difficultyLevel": 3,
                      "cefrLevel": "B1",
                      "topicCode": "R-DET",
                      "questionType": "mcq",
                      "explanation": "The first sentence states the opening time.",
                      "evidenceStart": 4,
                      "evidenceEnd": 21,
                      "evidenceUnit": "char",
                      "evidenceQuote": "opens at six",
                      "options": [
                        { "label": "A", "text": "At six.", "isCorrect": true },
                        { "label": "B", "text": "At seven.", "isCorrect": false },
                        { "label": "C", "text": "At eight.", "isCorrect": false },
                        { "label": "D", "text": "At noon.", "isCorrect": false }
                      ]
                    },
                    {
                      "displayOrder": 2,
                      "stem": "Who signs the night ledger?",
                      "difficultyLevel": 3,
                      "cefrLevel": "B1",
                      "topicCode": "R-INF",
                      "questionType": "mcq",
                      "explanation": "Only staff who arrive early sign it.",
                      "options": [
                        { "label": "A", "text": "Visitors.", "isCorrect": false },
                        { "label": "B", "text": "Staff arriving before six.", "isCorrect": true },
                        { "label": "C", "text": "The supervisor.", "isCorrect": false },
                        { "label": "D", "text": "Nobody.", "isCorrect": false }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """; 

    private async Task<(int PoolItems, int PoolStimuli, int PoolBindings)> CountPoolAsync(int examId)
    {
        await using var db = Db();
        var items = await db.ExamItems.Where(b => b.ExamId == examId).Select(b => b.ItemId).Distinct().ToListAsync();
        var stimuli = await db.Items.Where(i => items.Contains(i.ItemId) && i.StimulusId != null)
            .Select(i => i.StimulusId).Distinct().CountAsync();
        return (items.Count, stimuli, await db.ExamItems.CountAsync(b => b.ExamId == examId));
    }

    // ---- AC1: all three import routes produce pool items and versions with no exam reference ----

    /// <summary>
    /// AC1 through the manifest door: two questions, one shared passage, and both pool rows carry no
    /// exam column because <c>Items</c> has none (#184). The versions are checked, not only the items:
    /// an item with no version is a row the pool cannot serve.
    /// </summary>
    [Fact]
    public async Task Manifest_import_writes_pool_items_and_versions_with_no_exam_reference()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var before = await CountLegacyQuestionsAsync();

        var result = await CreateService().ImportAsync(adminId, certId, "two-child.json", Utf8(TwoChildManifest(Unique("Pool Import "))));

        Assert.Equal(ImportBatchStatusDto.Done, result.Status);
        Assert.NotNull(result.ExamId);

        await using var db = Db();
        var bindings = await db.ExamItems.AsNoTracking().Where(b => b.ExamId == result.ExamId).ToListAsync();
        Assert.Equal(2, bindings.Count);

        var itemIds = bindings.Select(b => b.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.ItemId)).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("demo", i.ContentTier));
        Assert.All(items, i => Assert.Equal("draft", i.LifecycleStatus));
        Assert.All(items, i => Assert.Equal("reading", i.Modality));
        Assert.All(items, i => Assert.Equal(CefrLevel.B1, i.CefrLevel));

        var versionIds = bindings.Select(b => b.ItemVersionId).Distinct().ToList();
        var versions = await db.ItemVersions.AsNoTracking().Where(v => versionIds.Contains(v.ItemVersionId)).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.All(versions, v => Assert.Equal(1, v.VersionNumber));
        Assert.Contains(versions, v => v.Stem == "What time does the depot open?" && v.KeyLabel == "A");
        Assert.Contains(versions, v => v.Stem == "Who signs the night ledger?" && v.KeyLabel == "B");

        // CNT-11: the evidence span survives the write, which is what makes review able to jump to it.
        var timed = versions.Single(v => v.Stem == "What time does the depot open?");
        Assert.Equal((4, 21, "char"), (timed.EvidenceStart, timed.EvidenceEnd, timed.EvidenceUnit));
        Assert.Equal("opens at six", timed.EvidenceQuote);

        // The legacy rows are still written: this is expand, not contract.
        Assert.Equal(before + 2, await CountLegacyQuestionsAsync());
    }

    /// <summary>
    /// AC1 through the bulk JSON door - the same service, but this is the path a 300-item file takes,
    /// and the batch is what an admin actually reads. It must report what reached the pool.
    /// </summary>
    [Fact]
    public async Task Bulk_json_upload_writes_pool_stock_and_says_so_in_the_batch_log()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();

        var result = await CreateService().ImportManifestAsync(adminId, certId, TwoChildManifest(Unique("Bulk Import ")));

        Assert.Equal(ImportBatchStatusDto.Done, result.Status);

        await using var db = Db();
        var logs = await db.ImportLogs.AsNoTracking().Where(l => l.BatchId == result.BatchId).ToListAsync();
        Assert.Contains(logs, l => l.Level == "info" && l.Message.Contains("2 pool items") && l.Message.Contains("1 shared stimuli"));
    }

    /// <summary>
    /// AC1 through the quick-ingest door. A paste is text, so the level and topic come with the
    /// commit rather than from the text - the parser cannot read a CEFR level, and D1 forbids
    /// deriving one from the difficulty number.
    /// </summary>
    [Fact]
    public async Task Quick_ingest_commit_writes_pool_items_and_versions_with_no_exam_reference()
    {
        var (certId, _, readingSectionId, _, _) = await SeedAsync();
        await using var setup = Db();
        var exam = await new AdminExamService(setup, TimeProvider.System).CreateExamAsync(
            new CreateAdminExamRequest(certId, Unique("Quick pool "), 30, ExamPurposeDto.Mock));
        var examId = exam.ExamId;

        var questions = new List<QuickIngestParsedQuestionDto>
        {
            Question(1, "The depot opens at six.", "What time does the depot open?", "B1", "R-DET"),
            Question(2, null, "Who signs the night ledger?", "B1", "R-INF"),
        };

        var result = await CreateQuickIngest().CommitStagedAsync(
            examId,
            new QuickIngestCommitRequest(readingSectionId, "append", questions, CefrLevel: "B1"));

        Assert.True(result.Success);
        Assert.Equal(2, result.QuestionsCommitted);
        Assert.Equal(2, result.PoolItemsWritten);
        Assert.Equal(1, result.PoolStimuliWritten);
        Assert.Equal(2, result.PoolItemsBound);

        await using var db = Db();
        var bindings = await db.ExamItems.AsNoTracking().Where(b => b.ExamId == examId).ToListAsync();
        Assert.Equal(2, bindings.Count);

        var items = await db.Items.AsNoTracking()
            .Where(i => bindings.Select(b => b.ItemId).Contains(i.ItemId))
            .ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("demo", i.ContentTier));

        var detId = await TopicIdAsync("R-DET");
        var infId = await TopicIdAsync("R-INF");
        Assert.Contains(items, i => i.TopicId == detId);
        Assert.Contains(items, i => i.TopicId == infId);
    }

    /// <summary>
    /// A commit that states no level is refused by name, before anything is written.
    /// <para>
    /// <b>Changed by #229.</b> This test used to assert the opposite: a commit with no level wrote the
    /// legacy rows and warned. That was the right shape for #189's expand half, but #229 makes the
    /// level the only levelling fact, and AC2 says an imported item lands <em>with a level</em>. An
    /// unlevelled legacy row is what #231 cannot migrate, so the commit is now refused.
    /// </para>
    /// <para>
    /// The input that would pass this while being wrong: falling back to the paste's own level, to the
    /// section's, or to a constant - each produces a committed row and no error.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Quick_ingest_without_a_level_is_refused_and_names_what_is_missing()
    {
        var (certId, _, readingSectionId, _, _) = await SeedAsync();
        await using var setup = Db();
        var exam = await new AdminExamService(setup, TimeProvider.System).CreateExamAsync(
            new CreateAdminExamRequest(certId, Unique("Quick no level "), 30, ExamPurposeDto.Mock));

        var before = await CountLegacyQuestionsAsync();

        var refused = await Assert.ThrowsAsync<AdminExamException>(() => CreateQuickIngest().CommitStagedAsync(
            exam.ExamId,
            new QuickIngestCommitRequest(readingSectionId, "append", [Question(1, null, "What time?", null, null)])));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains("cefrLevel", refused.Message, StringComparison.Ordinal);
        Assert.Contains("A1, A2, B1, B2, C1, C2", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, await CountLegacyQuestionsAsync());
    }

    // ---- AC2: a stimulus and several children is one stimulus, not duplicated passages ----

    /// <summary>
    /// AC2, the criterion that is easy to fake: a passage with two children on it must be exactly one
    /// <c>Stimuli</c> row that two items point at. Counting items would pass a build that copied the
    /// text per child, so the assertion is on the distinct stimulus count and on the item rows both
    /// naming the same id.
    /// </summary>
    [Fact]
    public async Task A_passage_with_two_children_imports_as_one_stimulus_and_two_children()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();

        var result = await CreateService().ImportAsync(adminId, certId, "two-child.json", Utf8(TwoChildManifest(Unique("Shared passage "))));

        await using var db = Db();
        var bindings = await db.ExamItems.AsNoTracking().Where(b => b.ExamId == result.ExamId).ToListAsync();
        var items = await db.Items.AsNoTracking().Where(i => bindings.Select(b => b.ItemId).Contains(i.ItemId)).ToListAsync();

        var stimulusId = Assert.Single(items.Select(i => i.StimulusId).Distinct());
        Assert.NotNull(stimulusId);
        Assert.Equal(2, items.Count);

        var stimulus = await db.Stimuli.AsNoTracking().SingleAsync(s => s.StimulusId == stimulusId);
        Assert.Equal("B1", stimulus.CefrLevel.Code);
        Assert.Equal("workplace", stimulus.DomainContext);
        Assert.Contains("night ledger", stimulus.Passage!, StringComparison.Ordinal);

        // Not one row per child: the whole point of a shared passage.
        Assert.Equal(1, await db.Stimuli.CountAsync(s => s.StimulusId == stimulusId));
    }

    /// <summary>
    /// Two groups in one file are two stimuli even when nothing else separates them, because a group
    /// is the unit that shares a passage. Merging them would hide a real distinction.
    /// </summary>
    [Fact]
    public async Task Two_groups_are_two_stimuli_even_with_identical_text()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var manifest = JsonNode.Parse(TwoChildManifest(Unique("Two groups ")))!;
        var firstGroup = manifest["sections"]![0]!["groups"]![0]!;

        var secondGroup = firstGroup.DeepClone();
        secondGroup["displayOrder"] = 2;
        secondGroup["questions"]![0]!["displayOrder"] = 3;
        secondGroup["questions"]![1]!["displayOrder"] = 4;
        manifest["sections"]![0]!["groups"]!.AsArray().Add(secondGroup);

        var result = await CreateService().ImportAsync(adminId, certId, "two-groups.json", Utf8(manifest.ToJsonString()));

        await using var db = Db();
        var items = await db.Items.AsNoTracking()
            .Where(i => db.ExamItems.Any(b => b.ExamId == result.ExamId && b.ItemId == i.ItemId))
            .ToListAsync();

        Assert.Equal(4, items.Count);
        Assert.Equal(2, items.Select(i => i.StimulusId).Distinct().Count());
    }

    // ---- AC3: export round-trips ----

    /// <summary>
    /// AC3, the criterion that needs a test which can actually fail: export what was imported, import
    /// the export, and compare the item and version content field by field - stem, all four options,
    /// the key, the explanation, the evidence span and the stimulus. A count-only assertion would
    /// pass a build that dropped every option.
    /// </summary>
    [Fact]
    public async Task Exporting_what_was_imported_and_importing_it_again_reproduces_the_item_and_version()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var service = CreateService();

        var first = await service.ImportAsync(adminId, certId, "roundtrip.json", Utf8(TwoChildManifest(Unique("Round trip "))));
        Assert.Equal(ImportBatchStatusDto.Done, first.Status);

        var original = await ReadPoolAsync(first.ExamId!.Value);
        Assert.Equal(2, original.Count);

        var (stream, contentType, _) = await service.ExportExamPackageAsync(first.ExamId.Value, "json");
        Assert.Equal("application/json", contentType);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var exported = await reader.ReadToEndAsync();
        var exportedNode = JsonNode.Parse(exported)!;
        exportedNode["exam"]!["title"] = Unique("Round trip again ");

        var second = await ImportJsonAsync(service, adminId, certId, exportedNode);
        Assert.Equal(ImportBatchStatusDto.Done, second.Status);
        Assert.Equal(0, second.ErrorCount);

        var reimported = await ReadPoolAsync(second.ExamId!.Value);

        // Field by field, because a count would pass a build that lost every option.
        Assert.Equal(original.Count, reimported.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Content, reimported[i].Content);
            Assert.Equal(original[i].Version, reimported[i].Version);
            Assert.Equal(original[i].Stimulus, reimported[i].Stimulus);
        }

        // #229: the level survives the round trip on both shapes - the pool item and the legacy paper
        // row a re-import also writes. The observed file states B1 on both children of one group, so a
        // writer that dropped the level on either side lands here rather than passing on a count.
        Assert.All(reimported, slice => Assert.Contains("|B1|", slice.Content, StringComparison.Ordinal));
        Assert.Equal(
            original.Select(slice => slice.Content),
            reimported.Select(slice => slice.Content));

        // The legacy row, which is what #229 migrated and what #231 will read until it is dropped.
        await using var db = Db();
        var legacy = await db.Questions.AsNoTracking()
            .Where(q => q.ExamId == second.ExamId)
            .ToListAsync();
        Assert.Equal(2, legacy.Count);
        Assert.All(legacy, q => Assert.Equal(CefrLevel.B1, q.CefrLevel));
        Assert.All(legacy, q => Assert.Null(q.DifficultyLevel));
    }

    /// <summary>
    /// The round-trip through the studio's export door too: two exporters that disagree is how #152's
    /// spoken reference survived one path and vanished through the other. Both must produce a file
    /// that re-imports to the same content.
    /// </summary>
    [Fact]
    public async Task Both_export_doors_produce_the_same_reimportable_manifest()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();

        var imported = await CreateService().ImportAsync(adminId, certId, "both-doors.json", Utf8(TwoChildManifest(Unique("Both doors "))));

        var fromImports = await ReadJsonAsync(await CreateService().ExportExamPackageAsync(imported.ExamId!.Value, "json"));
        await using var db = Db();
        var fromStudio = await ReadJsonAsync(await new AdminExamService(db, TimeProvider.System)
            .ExportExamPackageAsync(imported.ExamId.Value, "json"));

        Assert.Equal(fromImports, fromStudio);
    }

    // ---- Constraints that bite ----

    /// <summary>
    /// A legacy category code maps to a canonical topic, and the mapping is stated rather than
    /// guessed: <c>grammar_prep</c> is R-GRAM (morphosyntactic structure) and
    /// <c>reading_inference</c> is R-INF (bridging inference). A code with no mapping and an invented
    /// topic both land the file's question in the legacy shape only, with a message naming why.
    /// </summary>
    [Fact]
    public async Task A_legacy_category_code_maps_to_a_canonical_topic_and_an_unknown_one_does_not()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var manifest = JsonNode.Parse(TwoChildManifest(Unique("Category map ")))!;
        var questions = manifest["sections"]![0]!["groups"]![0]!["questions"]!.AsArray();

        // The first child names no topic, only a legacy code the mapping knows.
        questions[0]!["topicCode"] = null;
        questions[0]!["categoryCode"] = "grammar_prep";
        // The second names no topic at all, which falls back to its type's own topic.
        questions[1]!["topicCode"] = null;

        var result = await ImportJsonAsync(CreateService(), adminId, certId, manifest);
        Assert.Equal(ImportBatchStatusDto.Done, result.Status);
        Assert.Equal(2, result.QuestionCount);

        await using var db = Db();
        var items = await db.Items.AsNoTracking()
            .Where(i => db.ExamItems.Any(b => b.ExamId == result.ExamId && b.ItemId == i.ItemId))
            .ToListAsync();
        var topics = await db.Topics.AsNoTracking().ToListAsync();

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => topics.Single(t => t.TopicId == i.TopicId).Code == "R-GRAM");
        // passage_comprehension's own topic is detail retrieval.
        Assert.Contains(items, i => topics.Single(t => t.TopicId == i.TopicId).Code == "R-DET");
    }

    /// <summary>
    /// An invented topic code is refused with the canonical list, never clamped to a nearby one. D3's
    /// vocabulary is closed: a silently substituted topic makes "your weakest topic" a claim about
    /// the wrong construct.
    /// </summary>
    [Fact]
    public async Task An_invented_topic_code_is_refused_with_the_canonical_list()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var manifest = JsonNode.Parse(TwoChildManifest(Unique("Bad topic ")))!;
        manifest["sections"]![0]!["groups"]![0]!["questions"]![0]!["topicCode"] = "R-ANYTHING";

        var result = await ImportJsonAsync(CreateService(), adminId, certId, manifest);

        Assert.Equal(ImportBatchStatusDto.Failed, result.Status);
        var errors = (await LogsAsync(result.BatchId)).Where(l => l.Level == "error").Select(l => l.Message).ToList();
        Assert.Contains(errors, m => m.Contains("R-ANYTHING") && m.Contains("R-DET") && m.Contains("R-GRAM"));
    }

    /// <summary>
    /// A level that is not on the authored ladder is refused at validation, and the message says why
    /// there is no mapping from the difficulty number: D1 is an absolute.
    /// </summary>
    [Fact]
    public async Task A_level_off_the_authored_ladder_is_refused_and_names_d1()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var manifest = JsonNode.Parse(TwoChildManifest(Unique("Bad level ")))!;
        manifest["sections"]![0]!["groups"]![0]!["questions"]![0]!["cefrLevel"] = "B3";

        var result = await ImportJsonAsync(CreateService(), adminId, certId, manifest);

        Assert.Equal(ImportBatchStatusDto.Failed, result.Status);
        var errors = (await LogsAsync(result.BatchId)).Where(l => l.Level == "error").Select(l => l.Message).ToList();
        Assert.Contains(errors, m => m.Contains("B3") && m.Contains("A1-C2") && m.Contains("difficulty-to-level"));
    }

    /// <summary>
    /// Import never mints production stock: an item lands demo and draft, so INV-6 keeps it out of a
    /// paid paper until a review gate promotes it. That is why INV-6 means something rather than being
    /// satisfied by a default nobody chose.
    /// </summary>
    [Fact]
    public async Task Imported_items_land_demo_and_draft_never_production()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var service = CreateService();

        var first = await service.ImportAsync(adminId, certId, "prod.json", Utf8(TwoChildManifest(Unique("Production paper "))));
        Assert.Equal(ImportBatchStatusDto.Done, first.Status);

        await using var db = Db();
        var items = await db.Items.AsNoTracking()
            .Where(i => db.ExamItems.Any(b => b.ExamId == first.ExamId && b.ItemId == i.ItemId))
            .ToListAsync();

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("demo", i.ContentTier));
        Assert.All(items, i => Assert.Equal("draft", i.LifecycleStatus));
    }

    /// <summary>
    /// The other half of INV-6: a production paper cannot hold the demo stock an import produces, so
    /// the commit writes the items to the pool and skips the bindings, saying so. Writing nothing
    /// would throw away work the pool can use; binding would be refused by the trigger and roll the
    /// whole batch back.
    /// </summary>
    [Fact]
    public async Task A_production_paper_gets_pool_stock_but_no_binding()
    {
        var (certId, _, readingSectionId, _, _) = await SeedAsync();
        await using var setup = Db();
        var exam = await new AdminExamService(setup, TimeProvider.System).CreateExamAsync(
            new CreateAdminExamRequest(certId, Unique("Prod quick "), 30, ExamPurposeDto.Mock));

        // The exam is already production before anything is pasted into it.
        await database.SqlAsync($"UPDATE Exams SET ContentTier = 'production' WHERE ExamId = {exam.ExamId};");

        int itemsBefore;
        await using (var before = Db())
        {
            itemsBefore = await before.Items.CountAsync();
        }

        var result = await CreateQuickIngest().CommitStagedAsync(
            exam.ExamId,
            new QuickIngestCommitRequest(readingSectionId, "append", [Question(1, null, "What time?", "B1", "R-DET")], CefrLevel: "B1"));

        Assert.True(result.Success);
        Assert.Equal(1, result.PoolItemsWritten);
        Assert.Equal(0, result.PoolItemsBound);
        Assert.Contains("INV-6", result.Message, StringComparison.Ordinal);

        await using var db = Db();
        Assert.Equal(0, await db.ExamItems.CountAsync(b => b.ExamId == exam.ExamId));
        Assert.Equal(itemsBefore + 1, await db.Items.CountAsync());
    }

    /// <summary>
    /// A question the pool cannot hold - here a multi-select, which has two keys where CNT-06 allows
    /// exactly one - is written in the legacy shape and named in the batch log. Refusing the whole
    /// file would mean a legacy manifest that imported yesterday stops importing today, which is the
    /// opposite of expand.
    /// </summary>
    [Fact]
    public async Task A_question_with_no_pool_form_still_imports_legacy_and_the_log_says_which()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var manifest = JsonNode.Parse(TwoChildManifest(Unique("Multi select ")))!;
        var first = manifest["sections"]![0]!["groups"]![0]!["questions"]![0]!;
        first["questionType"] = "multi_select";
        first["maxSelections"] = 2;
        first["options"]![1]!["isCorrect"] = true;
        first["cefrLevel"] = "B1";

        var result = await ImportJsonAsync(CreateService(), adminId, certId, manifest);

        Assert.Equal(ImportBatchStatusDto.Done, result.Status);
        Assert.Equal(2, result.QuestionCount);

        var logs = await LogsAsync(result.BatchId);
        Assert.Contains(logs, l => l.Level == "warn" && l.Message.Contains("exactly one key"));

        await using var db = Db();
        // Only the second question reached the pool; the multi-select did not.
        Assert.Equal(1, await db.ExamItems.CountAsync(b => b.ExamId == result.ExamId));
    }

    /// <summary>
    /// A productive task is the pool's other legal version shape: no options and no key. Importing
    /// one must produce that, not four empty options.
    /// </summary>
    [Fact]
    public async Task A_writing_task_imports_as_a_productive_version_with_no_options()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var manifest = $$"""
            {
              "version": "1.0",
              "exam": {
                "title": "{{Unique("Writing pool ")}}",
                "purpose": "mock",
                "durationMinutes": 30,
                "questionCount": 1
              },
              "sections": [
                {
                  "sectionCode": "W2",
                  "groups": [
                    {
                      "displayOrder": 1,
                      "cefrLevel": "B2",
                      "promptCopy": "Reply to the customer's complaint about a late delivery.",
                      "questions": [
                        {
                          "stem": "Write a reply to the customer.",
                          "difficultyLevel": 3,
                          "cefrLevel": "B2",
                          "topicCode": "W-CORR",
                          "itemType": "transactional_correspondence",
                          "questionType": "free_response",
                          "responseSeconds": 1200,
                          "minWords": 120,
                          "maxWords": 180
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """; 

        var result = await CreateService().ImportAsync(adminId, certId, "writing.json", Utf8(manifest));
        Assert.Equal(ImportBatchStatusDto.Done, result.Status);

        await using var db = Db();
        var binding = await db.ExamItems.AsNoTracking().SingleAsync(b => b.ExamId == result.ExamId);
        var version = await db.ItemVersions.AsNoTracking().SingleAsync(v => v.ItemVersionId == binding.ItemVersionId);
        Assert.Null(version.OptionA);
        Assert.Null(version.KeyLabel);

        var item = await db.Items.AsNoTracking().SingleAsync(i => i.ItemId == binding.ItemId);
        Assert.Equal("transactional_correspondence", item.ItemType);

        // The prompt is a stimulus, not a stem: it is the shared instruction the task answers.
        Assert.NotNull(item.StimulusId);
        var stimulus = await db.Stimuli.AsNoTracking().SingleAsync(s => s.StimulusId == item.StimulusId);
        Assert.Contains("late delivery", stimulus.PromptCopy!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A listening manifest must say which of the three listening types its audio is: the manifest
    /// carries no speaker or turn count, so the type is not derivable. Guessing one would put a
    /// twenty-minute talk under a name that promises a two-turn exchange.
    /// </summary>
    [Fact]
    public async Task A_listening_question_without_an_item_type_is_not_written_to_the_pool()
    {
        var (certId, adminId, _, listeningSectionId, _) = await SeedAsync();
        var manifest = JsonNode.Parse(TwoChildManifest(Unique("Listening no type ")))!;
        manifest["sections"]![0]!["sectionCode"] = "P1";
        manifest["sections"]![0]!["groups"]![0]!["questions"] = new JsonArray
        {
            new JsonObject
            {
                ["stem"] = "What are the speakers discussing?",
                ["difficultyLevel"] = 3,
                ["cefrLevel"] = "B1",
                ["topicCode"] = "L-GIST",
                ["questionType"] = "mcq",
                ["options"] = new JsonArray
                {
                    new JsonObject { ["label"] = "A", ["text"] = "A delivery.", ["isCorrect"] = true },
                    new JsonObject { ["label"] = "B", ["text"] = "A budget.", ["isCorrect"] = false },
                    new JsonObject { ["label"] = "C", ["text"] = "A hiring.", ["isCorrect"] = false },
                    new JsonObject { ["label"] = "D", ["text"] = "A holiday.", ["isCorrect"] = false },
                },
            },
        };

        var result = await ImportJsonAsync(CreateService(), adminId, certId, manifest);
        Assert.Equal(ImportBatchStatusDto.Done, result.Status);

        var logs = await LogsAsync(result.BatchId);
        Assert.Contains(logs, l => l.Level == "warn" && l.Message.Contains("itemType is required for a listening item"));

        await using var db = Db();
        Assert.Equal(0, await db.Items.CountAsync(i => db.ExamItems.Any(b => b.ExamId == result.ExamId && b.ItemId == i.ItemId)));
        Assert.Equal(1, await db.Questions.CountAsync(q => q.SectionId == listeningSectionId));
    }

    /// <summary>
    /// An item type belonging to another skill is refused by name rather than by
    /// <c>CK_Items_Modality_Type</c> rolling the whole batch back.
    /// </summary>
    [Fact]
    public async Task An_item_type_from_another_skill_is_not_written_to_the_pool()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var manifest = JsonNode.Parse(TwoChildManifest(Unique("Wrong type ")))!;
        manifest["sections"]![0]!["groups"]![0]!["questions"]![0]!["itemType"] = "persuasive_essay";

        var result = await ImportJsonAsync(CreateService(), adminId, certId, manifest);

        Assert.Equal(ImportBatchStatusDto.Done, result.Status);
        var logs = await LogsAsync(result.BatchId);
        Assert.Contains(logs, l => l.Level == "warn" && l.Message.Contains("not a reading item type"));
    }

    /// <summary>
    /// An evidence unit the schema does not know is refused with the two it does, so a typo is a
    /// warning rather than a constraint error that undoes the batch.
    /// </summary>
    [Fact]
    public async Task An_unknown_evidence_unit_is_not_written_to_the_pool()
    {
        var (certId, adminId, _, _, _) = await SeedAsync();
        var manifest = JsonNode.Parse(TwoChildManifest(Unique("Bad unit ")))!;
        var first = manifest["sections"]![0]!["groups"]![0]!["questions"]![0]!;
        first["evidenceUnit"] = "words";
        first["evidenceStart"] = 4;
        first["evidenceEnd"] = 21;

        var result = await ImportJsonAsync(CreateService(), adminId, certId, manifest);

        Assert.Equal(ImportBatchStatusDto.Done, result.Status);
        var logs = await LogsAsync(result.BatchId);
        Assert.Contains(logs, l => l.Level == "warn" && l.Message.Contains("'char' for text or 'sec' for audio"));
    }

    // ---- helpers ----

    private async Task<int> CountLegacyQuestionsAsync()
    {
        await using var db = Db();
        return await db.Questions.CountAsync();
    }

    private async Task<int> TopicIdAsync(string code)
    {
        await using var db = Db();
        return await db.Topics.Where(t => t.Code == code).Select(t => t.TopicId).SingleAsync();
    }

    private async Task<List<ImportLog>> LogsAsync(int batchId)
    {
        await using var db = Db();
        return await db.ImportLogs.Where(l => l.BatchId == batchId).OrderBy(l => l.LogId).ToListAsync();
    }

    private static Task<AdminImportResultDto> ImportJsonAsync(
        QuestionImportService service, int adminId, int certId, JsonNode manifest, string fileName = "manifest.json") =>
        service.ImportAsync(adminId, certId, fileName, Utf8(manifest.ToJsonString()));

    private static async Task<string> ReadJsonAsync((Stream Stream, string ContentType, string FileName) package)
    {
        using var reader = new StreamReader(package.Stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static QuickIngestParsedQuestionDto Question(
        int index, string? passage, string stem, string? level, string? topic) =>
        new(
            Index: index,
            StartLineNumber: index,
            EndLineNumber: index,
            Stem: stem,
            DifficultyLevel: null,
            Explanation: $"Explanation for {stem}",
            CategoryCode: null,
            Options:
            [
                new QuickIngestParsedOptionDto("A", $"Answer A to {stem}", true),
                new QuickIngestParsedOptionDto("B", $"Answer B to {stem}", false),
                new QuickIngestParsedOptionDto("C", $"Answer C to {stem}", false),
                new QuickIngestParsedOptionDto("D", $"Answer D to {stem}", false),
            ],
            Issues: [],
            IsValid: true,
            Passage: passage,
            GroupNumber: 1,
            CefrLevel: level,
            TopicCode: topic,
            // The commit takes the level from the request too, so a caller may state it once.
            ItemType: "passage_comprehension");

    /// <summary>
    /// The pool rows of one paper, read in a stable order so a field-by-field comparison is possible.
    /// A record rather than three loose collections, so an assertion cannot silently compare the
    /// wrong pair.
    /// </summary>
    private async Task<List<PoolSlice>> ReadPoolAsync(int examId)
    {
        await using var db = Db();
        var bindings = await db.ExamItems.AsNoTracking()
            .Where(b => b.ExamId == examId)
            .OrderBy(b => b.SectionId).ThenBy(b => b.DisplayOrder)
            .ToListAsync();

        var slices = new List<PoolSlice>();
        foreach (var binding in bindings)
        {
            var item = await db.Items.AsNoTracking().SingleAsync(i => i.ItemId == binding.ItemId);
            var version = await db.ItemVersions.AsNoTracking().SingleAsync(v => v.ItemVersionId == binding.ItemVersionId);
            var stimulus = item.StimulusId is int stimulusId
                ? await db.Stimuli.AsNoTracking().SingleAsync(s => s.StimulusId == stimulusId)
                : null;
            var topicCode = await db.Topics.AsNoTracking()
                .Where(t => t.TopicId == item.TopicId).Select(t => t.Code).SingleAsync();

            slices.Add(new PoolSlice(
                $"{item.Modality}|{topicCode}|{item.CefrLevel.Code}|{item.ItemType}|{item.ContentTier}|{item.LifecycleStatus}|{item.PlacementEligible}",
                $"{version.Stem}|{version.OptionA}|{version.OptionB}|{version.OptionC}|{version.OptionD}|{version.KeyLabel}|" +
                $"{version.Explanation}|{version.EvidenceStart}|{version.EvidenceEnd}|{version.EvidenceUnit}|{version.EvidenceQuote}|" +
                $"{version.ReferenceText}|{version.ReferenceKind}",
                stimulus is null
                    ? "(no stimulus)"
                    : $"{stimulus.CefrLevel.Code}|{stimulus.Passage}|{stimulus.AudioPath}|{stimulus.Transcript}|" +
                      $"{stimulus.ImagePath}|{stimulus.ImageAlt}|{stimulus.DomainContext}|{stimulus.PromptCopy}|" +
                      $"{stimulus.PrepSeconds}|{stimulus.ResponseSeconds}"));
        }

        return slices;
    }

    private sealed record PoolSlice(string Content, string Version, string Stimulus);
}
