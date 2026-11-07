using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
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
/// #198: the ingestion seam reports per line, and the four gates are what refuse.
/// <para>
/// The tests that matter read the pool tables afterwards rather than the returned value. "A rejected
/// record writes nothing" is a statement about <c>Items</c> and <c>ItemVersions</c>; an assertion on
/// the result object would pass a build that reported a refusal and wrote the row anyway, which is
/// precisely the state #198 was filed against - four gates with no caller.
/// </para>
/// <para>
/// Gate three and gate four need models. The fakes are scripted per test so each proves a
/// <em>specific</em> refusal; a fake that always agreed would make every test pass for the wrong
/// reason, and that is the shape of eight review findings this month.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolIngestionTests(DatabaseFixture database)
{
    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    // ---- the fixture database ---------------------------------------------------------------

    private async Task SeedTopicsAsync()
    {
        await ClearIngestedAsync();
        await using var db = Db();
        await EnsureTopicAsync(db, "reading", "R-DET", "Local factual retrieval and paraphrase matching");
        await EnsureTopicAsync(db, "reading", "R-INF", "Bridging inference and logical deduction");
        await EnsureTopicAsync(db, "listening", "L-DET", "Explicit factual detail and extraction");
    }

    /// <summary>
    /// Every record these tests ingest lives in one cell - (R-DET, B1) - because gate one's key balance
    /// is a statement about that cell. Left behind, a few runs of a hundred records would make the cell
    /// imbalanced for every later test in the assembly, so each test removes what it wrote.
    /// </summary>
    private async Task ClearIngestedAsync()
    {
        // #205: an accepted record now lands in `validated`, and CNT-43 refuses the hard delete of
        // anything that has left draft - which is the invariant working, not a defect. A test catalog
        // is the one place it may be stepped around, and PackageServiceTests sets the precedent. The
        // rows must go rather than be retired, because gate one counts every item in a cell whatever
        // its lifecycle: a retired leftover would skew the balance the next test asserts.
        await database.SqlAsync("""
            DELETE FROM ExamItems
            WHERE ItemId IN (SELECT ItemId FROM Items WHERE BatchTag IN ('ingest-198', 'gate1-cell'));
            DELETE FROM ImportLog
            WHERE ItemId IN (SELECT ItemId FROM Items WHERE BatchTag IN ('ingest-198', 'gate1-cell'));
            DELETE FROM ItemVersions
            WHERE ItemId IN (SELECT ItemId FROM Items WHERE BatchTag IN ('ingest-198', 'gate1-cell'));
            ALTER TABLE Items DISABLE TRIGGER trg_Items_NoHardDelete;
            DELETE FROM Items WHERE BatchTag IN ('ingest-198', 'gate1-cell');
            ALTER TABLE Items ENABLE TRIGGER trg_Items_NoHardDelete;
            """);
    }

    private static async Task EnsureTopicAsync(CompanionDbContext db, string modality, string code, string name)
    {
        if (await db.Topics.AnyAsync(t => t.Modality == modality && t.Code == code))
        {
            return;
        }

        db.Topics.Add(new Topic { Modality = modality, Code = code, Name = name });
        await db.SaveChangesAsync();
    }

    private async Task<int> AdminIdAsync()
    {
        var email = $"ingest-{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin') INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{email}', 0x01, N'Ingest Admin', SYSDATETIMEOFFSET());
            """);
        await using var db = Db();
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    // ---- the file's shape -------------------------------------------------------------------

    /// <summary>
    /// The B1 reading passage <see cref="PoolGateTwoTests"/> proves against D0.1 and D0.2. Reusing it
    /// means a failure here is about the seam, not about a passage nobody calibrated.
    /// </summary>
    private static string B1Passage => PoolGateTwoTests.B1Passage;

    /// <summary>
    /// Four options that differ by more than two characters, open as noun phrases of equal length and
    /// carry a rationale each - so nothing but the rule under test can refuse the record.
    /// </summary>
    private const string OptionsJson = """
        [
          { "key": "A", "text": "The night ledger.", "distractor_rationale": "Correct: the notice names the ledger." },
          { "key": "B", "text": "The depot shutter.", "distractor_rationale": "The passage never mentions a shutter." },
          { "key": "C", "text": "The depot supervisor.", "distractor_rationale": "No supervisor appears in the passage." },
          { "key": "D", "text": "The office doorway.", "distractor_rationale": "A door is not what the notice requires." }
        ]
        """;

    /// <summary>The options with every rationale removed, which gate one must refuse (CNT-35).</summary>
    private const string OptionsWithoutRationaleJson = """
        [
          { "key": "A", "text": "The night ledger." },
          { "key": "B", "text": "The depot shutter." },
          { "key": "C", "text": "The depot supervisor." },
          { "key": "D", "text": "The office doorway." }
        ]
        """;

    private static string File(string skill, string groups, string version = "2.0.0") => $$"""
        {
          "schema_version": "{{version}}",
          "skill": "{{skill}}",
          "generated_at": "2026-11-06T00:00:00Z",
          "stimulus_groups": {{groups}},
          "productive_tasks": [],
          "lessons": []
        }
        """;

    /// <summary>
    /// One child of a reading group, every field the pool needs. A test edits one argument to make one
    /// rule fail, so the refused line is a known position rather than "somewhere".
    /// </summary>
    private static string Child(
        int index,
        string tier = "demo",
        string level = "B1",
        string key = "A",
        string? topic = null,
        string? itemType = "reading_passage_comprehension_mcq",
        bool withExplanation = true,
        bool withRationales = true,
        int spanStart = 4,
        int spanEnd = 21,
        string? itemId = null)
    {
        var stem = index == 0 ? "What time does the depot open?" : $"Which notice is posted at gate {index}?";
        var code = topic ?? (index % 2 == 0 ? "R-DET" : "R-INF");
        var options = withRationales ? OptionsJson : OptionsWithoutRationaleJson;
        var id = itemId ?? $"item_{(tier == "production" ? "prod" : "demo")}_depotitem{index:D8}";
        var explanation = withExplanation
            ? "\"The first sentence states the opening time.\""
            : "null";

        return $$"""
            {
              "item_id": "{{id}}",
              "content_tier": "{{tier}}",
              "sequence_index": {{index + 1}},
              "question_type": "{{itemType}}",
              "topic_id": "{{code}}",
              "item_level": "{{level}}",
              "placement_eligible": false,
              "time_limit_sec": 45,
              "stem": "{{stem}}",
              "options": {{options}},
              "correct_option": "{{key}}",
              "explanation": {{explanation}},
              "evidence_span": {
                "char_start": {{spanStart}},
                "char_end": {{spanEnd}},
                "verbatim_quote": "opens at six"
              },
              "verification_audit": {
                "author_model": "author-family",
                "verifier_a_model": "verifier-a",
                "verifier_b_model": "verifier-b",
                "verifier_agreement": true,
                "human_reviewed": false
              }
            }
            """;
    }

    /// <summary>
    /// One reading group: the shared passage plus its children. <paramref name="firstChild"/> keeps a
    /// group's stems and ids distinct from the next group's, because two groups in one file is the
    /// normal shape and two records titled "gate 1" would make a line assertion ambiguous.
    /// </summary>
    private static string ReadingGroup(
        int firstChild = 0,
        string stimulusId = "stim_demo_depotpassage01",
        string tier = "demo",
        string level = "B1",
        string? children = null,
        int childCount = 2,
        string batchTag = "ingest-198")
    {
        var items = children ?? string.Join(",", Enumerable.Range(0, childCount).Select(i => Child(
            firstChild + i,
            tier: tier,
            level: level,
            spanStart: 4 + (i * 60),
            spanEnd: 21 + (i * 60))));

        // Each child opens on its own line, as a pretty-printed 2.0.0 file does: a record's line is
        // where its object opens, so a fixture with two records on one line could not show that.
        items = items.Replace("},{", "},\n{", StringComparison.Ordinal);

        return $$"""
            {
              "stimulus_id": "{{stimulusId}}",
              "content_tier": "{{tier}}",
              "version": 1,
              "lifecycle_status": "draft",
              "batch_tag": "{{batchTag}}",
              "skill": "reading",
              "stimulus_level": "{{level}}",
              "domain_context": "workplace",
              "stimulus_format": "single_passage",
              "content": {
                "text_or_transcript": {{JsonSerializer.Serialize(B1Passage)}},
                "word_count": 299
              },
              "metrics": {
                "flesch_kincaid_grade": 6.97,
                "mltu_words": 13.0,
                "clauses_per_t_unit": 1.35,
                "target_band_lexical_coverage": 0.95
              },
              "items": [
            {{items}}
              ]
            }
            """;
    }

    // ---- the fakes --------------------------------------------------------------------------

    /// <summary>
    /// The models a passing record needs: two heterogeneous verifiers confident in the key and unsure
    /// without the passage, and a classifier that predicts the authored band. Everything a test uses
    /// this with is therefore asserting about the seam, not about a gate.
    /// </summary>
    private static (FakeVerifierModel One, FakeVerifierModel Two, FakeLevelClassifier Classifier) AgreeingGates()
    {
        // Keyed A with 0.9 confidence and every distractor under 0.15, which is what D17 requires.
        static Dictionary<string, decimal> Confident() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 0.90m, ["B"] = 0.04m, ["C"] = 0.03m, ["D"] = 0.03m,
        };

        // Without the passage the key must not be found at 0.50 or above.
        static Dictionary<string, decimal> Blind() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 0.28m, ["B"] = 0.24m, ["C"] = 0.24m, ["D"] = 0.24m,
        };

        return (
            new FakeVerifierModel("verifier-a", "v1") { Probabilities = Confident(), BlindProbabilities = Blind() },
            new FakeVerifierModel("verifier-b", "v2") { Probabilities = Confident(), BlindProbabilities = Blind() },
            new FakeLevelClassifier("classifier-family", "c1") { Predicted = CefrLevel.B1 });
    }

    private PoolIngestionService Service(
        FakeVerifierModel one,
        FakeVerifierModel two,
        FakeLevelClassifier classifier)
    {
        var db = Db();
        var gates = new PoolIngestionGates(
            db,
            verifierOne: one,
            verifierTwo: two,
            ablationSolver: null,
            classifier: classifier);
        return new PoolIngestionService(db, TimeProvider.System, gates);
    }

    private PoolIngestionService Service() =>
        Service(AgreeingGates().One, AgreeingGates().Two, AgreeingGates().Classifier);

    private static Stream Utf8(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private async Task<int> ItemsAsync()
    {
        await using var db = Db();
        return await db.Items.CountAsync();
    }

    private async Task<int> VersionsAsync()
    {
        await using var db = Db();
        return await db.ItemVersions.CountAsync();
    }

    private async Task<int> StimuliAsync()
    {
        await using var db = Db();
        return await db.Stimuli.CountAsync();
    }

    // ---- AC1: one seam, one result per record -----------------------------------------------

    /// <summary>
    /// AC1. A file for one skill returns a verdict per record, and the line numbers are the file's own:
    /// the assertion is on two distinct, increasing lines inside the document rather than on a count,
    /// because a seam that reported "record 1, record 2" would pass a count and tell the author nothing.
    /// </summary>
    [Fact]
    public async Task A_file_for_one_skill_returns_one_result_per_record_with_its_line()
    {
        await SeedTopicsAsync();
        var (one, two, classifier) = AgreeingGates();

        var groups = $"[{ReadingGroup(childCount: 2)}]";
        var result = await Service(one, two, classifier)
            .IngestAsync("reading", Utf8(File("reading", groups)), "reading_pool.json");

        Assert.Equal("reading", result.Skill);
        Assert.Equal(2, result.Records.Count);
        Assert.All(result.Records, r => Assert.True(r.Accepted, string.Join("; ", r.Refusals.Select(x => x.Message))));
        Assert.Equal(2, result.ItemsWritten);
        Assert.Equal(2, result.VersionsWritten);

        // The lines are the file's own: distinct, increasing, and inside the document.
        Assert.Equal(result.Records.OrderBy(r => r.Line).Select(r => r.Line), result.Records.Select(r => r.Line));
        Assert.All(result.Records, r => Assert.InRange(r.Line, 1, 200));
        Assert.NotEqual(result.Records[0].Line, result.Records[1].Line);

        // And they point at the record: the reported line is the line the record's object opens on, and
        // the item's own id is the first member inside it.
        var lines = File("reading", groups).Split('\n');
        Assert.Equal("{", lines[result.Records[0].Line - 1].Trim());
        Assert.Contains("\"item_id\"", lines[result.Records[0].Line], StringComparison.Ordinal);
        Assert.Contains(result.Records[0].RecordId, lines[result.Records[0].Line], StringComparison.Ordinal);
        Assert.Contains(result.Records[1].RecordId, lines[result.Records[1].Line], StringComparison.Ordinal);
    }

    // ---- AC2: a rejected record writes no item and no version --------------------------------

    /// <summary>
    /// AC2, the criterion that must be read off the database. Two groups: the first is valid, the
    /// second carries one record with no explanation. The assertion is not that the second group's
    /// result is rejected - it is that <c>Items</c> and <c>ItemVersions</c> gained exactly the first
    /// group's rows and not one more.
    /// </summary>
    [Fact]
    public async Task A_rejected_record_writes_no_item_and_no_version_and_an_accepted_one_writes_both()
    {
        await SeedTopicsAsync();

        var itemsBefore = await ItemsAsync();
        var versionsBefore = await VersionsAsync();

        var good = ReadingGroup(firstChild: 10, stimulusId: "stim_demo_goodgroup0001");
        var bad = ReadingGroup(
            firstChild: 20,
            stimulusId: "stim_demo_badgroup0001",
            children: string.Join(",", new[]
            {
                Child(20, withExplanation: true, spanStart: 4, spanEnd: 21),
                Child(21, withExplanation: false, spanStart: 60, spanEnd: 80),
            }));

        var result = await Service().IngestAsync("reading", Utf8(File("reading", $"[{good},{bad}]")), "mixed.json");

        // The good group wrote both rows; the bad group wrote neither.
        Assert.Equal(2, result.Accepted.Count);
        Assert.Equal(2, result.Rejected.Count);

        Assert.Equal(itemsBefore + 2, await ItemsAsync());
        Assert.Equal(versionsBefore + 2, await VersionsAsync());

        await using var db = Db();
        var stored = await db.ItemVersions.AsNoTracking()
            .Where(v => v.Stem == "Which notice is posted at gate 21?")
            .ToListAsync();
        Assert.Empty(stored);

        // The refusal names the rule gate one owns, not a generic "invalid".
        var refused = result.Rejected.Single(r => r.RecordId.EndsWith("00000021", StringComparison.Ordinal));
        Assert.Contains(refused.Refusals, r => r.Rule == GateOneRules.Explanation);
    }

    // ---- AC3: the version is checked and an unknown one changes nothing ----------------------

    /// <summary>
    /// AC3. A file declaring 3.0.0 is refused by name before any record is judged, and - the half that
    /// matters - no row is written. A "half-applied" unknown format is the failure mode: the author
    /// cannot tell which records landed, so the whole file is refused.
    /// </summary>
    [Fact]
    public async Task An_unrecognised_version_is_refused_with_a_clear_message_and_writes_nothing()
    {
        await SeedTopicsAsync();
        var itemsBefore = await ItemsAsync();
        var versionsBefore = await VersionsAsync();

        var result = await Service().IngestAsync("reading", Utf8(File("reading", $"[{ReadingGroup()}]", version: "3.0.0")), "future.json");

        Assert.Equal(2, result.Records.Count);
        Assert.All(result.Records, r => Assert.False(r.Accepted));
        Assert.All(result.Records, r => Assert.Contains(
            "2.0.0", r.Message, StringComparison.Ordinal));
        Assert.Contains("3.0.0", result.Records[0].Message, StringComparison.Ordinal);

        Assert.Equal(0, result.ItemsWritten);
        Assert.Equal(itemsBefore, await ItemsAsync());
        Assert.Equal(versionsBefore, await VersionsAsync());
    }

    /// <summary>
    /// A file for another skill is refused rather than read with the wrong profile: a listening item's
    /// transcript envelope is shorter than a reading passage's at the same band (D0.2), so a mixed file
    /// has no single set of bounds to be judged against.
    /// </summary>
    [Fact]
    public async Task A_file_declaring_another_skill_is_refused_and_writes_nothing()
    {
        await SeedTopicsAsync();
        var itemsBefore = await ItemsAsync();

        var result = await Service().IngestAsync("reading", Utf8(File("listening", $"[{ReadingGroup()}]")), "wrong-skill.json");

        Assert.All(result.Records, r => Assert.False(r.Accepted));
        Assert.Contains("listening", result.Records[0].Message, StringComparison.Ordinal);
        Assert.Equal(itemsBefore, await ItemsAsync());
    }

    // ---- AC4: the tier prefix is machine-checked --------------------------------------------

    /// <summary>
    /// AC4, and the guard whose passing input would be wrong: a record whose id is
    /// <c>item_demo_…</c> and whose <c>content_tier</c> is <c>production</c> reads as production to
    /// anything that trusts the column, and reaches a paid paper under a name that says demo. Both
    /// halves are read, so the disagreement is caught rather than resolved in the file's favour.
    /// </summary>
    [Fact]
    public async Task A_record_whose_id_prefix_disagrees_with_its_tier_is_refused_and_writes_nothing()
    {
        await SeedTopicsAsync();
        var itemsBefore = await ItemsAsync();

        // The group is demo, so its stimulus prefix is right; the child claims production while its id
        // still says item_demo_.
        var group = ReadingGroup(
            children: Child(0, tier: "production", itemId: "item_demo_depotitem00000000"));

        var result = await Service().IngestAsync("reading", Utf8(File("reading", $"[{group}]")), "prefix.json");

        var record = Assert.Single(result.Records);
        Assert.False(record.Accepted);
        Assert.Contains(record.Refusals, r => r.Rule == IngestionRules.TierPrefix);
        Assert.Contains("item_demo_", record.Message, StringComparison.Ordinal);

        Assert.Equal(itemsBefore, await ItemsAsync());
    }

    /// <summary>
    /// The other half: a production-prefixed id inside a demo group. The child agrees with itself and
    /// still disagrees with its stimulus, which is a tier the pool has no column to express.
    /// </summary>
    [Fact]
    public async Task A_child_of_another_tier_than_its_stimulus_is_refused()
    {
        await SeedTopicsAsync();
        var itemsBefore = await ItemsAsync();

        var group = ReadingGroup(
            children: Child(0, tier: "production", itemId: "item_prod_depotitem00000000"));

        var result = await Service().IngestAsync("reading", Utf8(File("reading", $"[{group}]")), "tier-mix.json");

        var record = Assert.Single(result.Records);
        Assert.False(record.Accepted);
        Assert.Contains(record.Refusals, r => r.Rule == IngestionRules.TierPrefix);
        Assert.Equal(itemsBefore, await ItemsAsync());
    }

    // ---- AC5: a partially valid file keeps the valid records --------------------------------

    /// <summary>
    /// AC5, at the scale the ticket names. Three hundred records across fifty groups; the twenty-fifth
    /// group carries one record gate one refuses. The assertion is on the database: two hundred and
    /// ninety-nine items, two hundred and ninety-nine versions, one refusal with its own line.
    /// </summary>
    [Fact]
    public async Task A_partially_valid_file_of_several_hundred_records_keeps_the_valid_ones()
    {
        await SeedTopicsAsync();

        var itemsBefore = await ItemsAsync();
        var versionsBefore = await VersionsAsync();

        const int groupCount = 50;
        const int childrenPerGroup = 6;

        var groups = new List<string>(groupCount);
        for (var g = 0; g < groupCount; g++)
        {
            if (g == 24)
            {
                // One child of this group carries no explanation, which gate one refuses.
                var children = Enumerable.Range(0, childrenPerGroup)
                    .Select(i => Child(g * 10 + i, withExplanation: i != 3, spanStart: 4 + (i * 60), spanEnd: 21 + (i * 60)));
                groups.Add(ReadingGroup(
                    firstChild: g * 10,
                    stimulusId: "stim_demo_bulkgroup0240",
                    children: string.Join(",", children)));
                continue;
            }

            groups.Add(ReadingGroup(
                firstChild: g * 10,
                stimulusId: $"stim_demo_bulkgroup{g:D4}",
                childCount: childrenPerGroup));
        }

        var result = await Service().IngestAsync("reading", Utf8(File("reading", $"[{string.Join(",", groups)}]")), "bulk.json");

        Assert.Equal(groupCount * childrenPerGroup, result.Records.Count);

        // The one bad group's six records are refused; the other 294 are accepted. The record that broke
        // the rule names it; its five siblings name the bundle, because a stimulus and its children
        // enter the pool together or not at all.
        Assert.Equal(294, result.Accepted.Count);
        Assert.Equal(6, result.Rejected.Count);
        Assert.Single(result.Rejected, r => r.Refusals.Any(x => x.Rule == GateOneRules.Explanation));
        Assert.Equal(5, result.Rejected.Count(r => r.Refusals.Any(x => x.Rule == IngestionRules.RecordShape)));

        // A rejected record writes no item and no version, and the rest do.
        Assert.Equal(itemsBefore + 294, await ItemsAsync());
        Assert.Equal(versionsBefore + 294, await VersionsAsync());
        Assert.Equal(294, result.ItemsWritten);
    }

    // ---- the gates actually run -------------------------------------------------------------

    /// <summary>
    /// The finding that opened the ticket: gate one is <em>called</em>. An item with four options, a key
    /// and <b>no explanation and no rationale</b> was persistable through the single-item path - it is
    /// the exact shape the review saved - and now it is refused and the count of pool rows does not
    /// move. The options are complete on purpose: <c>ValidateVersionShape</c> already refuses a
    /// half-filled set, so a three-option case would pass this test without gate one running at all.
    /// </summary>
    [Fact]
    public async Task The_single_item_create_path_invokes_gate_one_and_writes_nothing_when_it_refuses()
    {
        await SeedTopicsAsync();
        await using var db = Db();
        var topicId = await db.Topics.Where(t => t.Modality == "reading" && t.Code == "R-DET")
            .Select(t => t.TopicId).SingleAsync();

        var itemsBefore = await ItemsAsync();
        var versionsBefore = await VersionsAsync();

        var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
            new AdminItemService(db, TimeProvider.System).CreateAsync(
                new CreatePoolItemRequest(
                    ModalityDto.Reading, topicId, "B1", "sentence_cloze",
                    "What time does the depot open?",
                    OptionA: "Six in the morning.",
                    OptionB: "Six in the evening.",
                    OptionC: "Eight in the morning.",
                    OptionD: "Eight in the evening.",
                    KeyLabel: "A",
                    Explanation: null)));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);

        // Both rules fire, and both are gate one's own codes rather than a restatement of them.
        Assert.Contains(GateOneRules.Explanation, refused.Message, StringComparison.Ordinal);
        Assert.Contains(GateOneRules.OptionRationale, refused.Message, StringComparison.Ordinal);

        Assert.Equal(itemsBefore, await ItemsAsync());
        Assert.Equal(versionsBefore, await VersionsAsync());
    }

    /// <summary>
    /// The other half of the same finding: the edit path. A create that was gated but an update that was
    /// not is one door, and the review's item can be walked through it in one step - drop the
    /// rationales and save.
    /// </summary>
    [Fact]
    public async Task The_edit_path_invokes_gate_one_and_writes_no_new_version_when_it_refuses()
    {
        await SeedTopicsAsync();
        await using var db = Db();
        var topicId = await db.Topics.Where(t => t.Modality == "reading" && t.Code == "R-DET")
            .Select(t => t.TopicId).SingleAsync();

        var service = new AdminItemService(db, TimeProvider.System);
        var created = await service.CreateAsync(new CreatePoolItemRequest(
            ModalityDto.Reading, topicId, "B1", "sentence_cloze",
            "What time does the depot open?",
            OptionA: "Six in the morning.",
            OptionB: "Six in the evening.",
            OptionC: "Eight in the morning.",
            OptionD: "Eight in the evening.",
            KeyLabel: "A",
            Explanation: "The passage states the opening time.",
            RationaleA: "Correct: six is the opening time.",
            RationaleB: "Evening is the closing time.",
            RationaleC: "The passage gives one time.",
            RationaleD: "The passage gives one time."));

        var versionsBefore = await VersionsAsync();

        var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
            service.UpdateAsync(created.ItemId, new UpdatePoolItemRequest(
                Stem: created.Stem,
                OptionA: created.OptionA,
                OptionB: created.OptionB,
                OptionC: created.OptionC,
                OptionD: created.OptionD,
                KeyLabel: created.KeyLabel,
                Explanation: created.Explanation,
                RationaleA: null,
                RationaleB: null,
                RationaleC: null,
                RationaleD: null)));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains(GateOneRules.OptionRationale, refused.Message, StringComparison.Ordinal);
        Assert.Equal(versionsBefore, await VersionsAsync());
    }

    /// <summary>
    /// Gate two is called too, and it refuses the wrong <em>stimulus</em>: a 12-word passage cannot
    /// support a B1 item (D0.2). A seam that profiled the item's stem instead would pass this, because
    /// the stem is a sentence and its length is nobody's bound.
    /// </summary>
    [Fact]
    public async Task Gate_two_refuses_a_stimulus_outside_its_bands_envelope_and_writes_nothing()
    {
        await SeedTopicsAsync();
        var itemsBefore = await ItemsAsync();

        var group = ReadingGroup().Replace(
            JsonSerializer.Serialize(B1Passage),
            JsonSerializer.Serialize("The depot opens at six."),
            StringComparison.Ordinal);

        var result = await Service().IngestAsync("reading", Utf8(File("reading", $"[{group}]")), "short-passage.json");

        Assert.All(result.Records, r => Assert.False(r.Accepted));
        Assert.Contains(result.Records[0].Refusals, r => r.Rule == GateTwoRules.WordCount);
        Assert.Equal(itemsBefore, await ItemsAsync());
    }

    /// <summary>
    /// Gate three is called, and with the real <see cref="GateThreeVerifier"/>: two models that agree
    /// with the key pass, and the same item with a verifier that dissents is refused. Proving only the
    /// passing half would pass a seam that never ran the gate.
    /// </summary>
    [Fact]
    public async Task Gate_three_refuses_an_item_a_verifier_dissents_from_and_writes_nothing()
    {
        await SeedTopicsAsync();
        var itemsBefore = await ItemsAsync();

        var (_, agreeing, classifier) = AgreeingGates();
        var dissenting = new FakeVerifierModel("verifier-a", "v1")
        {
            Probabilities = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = 0.10m, ["B"] = 0.80m, ["C"] = 0.05m, ["D"] = 0.05m,
            },
            BlindProbabilities = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = 0.25m, ["B"] = 0.25m, ["C"] = 0.25m, ["D"] = 0.25m,
            },
        };

        var result = await Service(dissenting, agreeing, classifier).IngestAsync("reading", Utf8(File("reading", $"[{ReadingGroup()}]")), "dissent.json");

        Assert.All(result.Records, r => Assert.False(r.Accepted));
        Assert.Contains(result.Records[0].Refusals, r => r.Rule == GateThreeRules.Dissent);
        Assert.Equal(itemsBefore, await ItemsAsync());
    }

    /// <summary>
    /// Gate three also refuses when no verifier is wired at all. A seam whose default was "two models
    /// that agree" would certify every key in an environment that configured none, which is a check
    /// that did not run reported as a pass.
    /// </summary>
    [Fact]
    public async Task Gate_three_refuses_when_no_verifier_is_configured()
    {
        await SeedTopicsAsync();
        var itemsBefore = await ItemsAsync();

        await using var db = Db();
        var gates = new PoolIngestionGates(db);
        var result = await new PoolIngestionService(db, TimeProvider.System, gates)
            .IngestAsync("reading", Utf8(File("reading", $"[{ReadingGroup()}]")), "unwired.json");

        Assert.All(result.Records, r => Assert.False(r.Accepted));
        Assert.Contains(result.Records[0].Refusals, r => r.Rule == GateThreeRules.UnusableDistribution);
        Assert.Equal(itemsBefore, await ItemsAsync());
    }

    /// <summary>
    /// Gate four is called: an independent classifier that predicts a band other than the authored one
    /// refuses the record, and the authored band is the one it names.
    /// </summary>
    [Fact]
    public async Task Gate_four_refuses_a_level_an_independent_classifier_disagrees_with()
    {
        await SeedTopicsAsync();
        var itemsBefore = await ItemsAsync();

        var (one, two, _) = AgreeingGates();
        var wrong = new FakeLevelClassifier("classifier-family", "c1") { Predicted = CefrLevel.C1 };

        var result = await Service(one, two, wrong).IngestAsync("reading", Utf8(File("reading", $"[{ReadingGroup()}]")), "level.json");

        Assert.All(result.Records, r => Assert.False(r.Accepted));
        Assert.Contains(result.Records[0].Refusals, r => r.Rule == GateFourRules.LevelDisagreement);
        Assert.Equal(itemsBefore, await ItemsAsync());
    }

    /// <summary>
    /// Gate four's span rule is a statement about the bundle, so two siblings citing overlapping
    /// evidence refuse together even though each record alone is fine. The input that would pass a
    /// per-record guard is exactly this pair: two well-formed spans whose union is the same sentence.
    /// </summary>
    [Fact]
    public async Task Gate_four_refuses_siblings_whose_evidence_overlaps_and_writes_nothing()
    {
        await SeedTopicsAsync();
        var itemsBefore = await ItemsAsync();

        var group = ReadingGroup(
            children: string.Join(",", new[]
            {
                Child(0, spanStart: 100, spanEnd: 200, topic: "R-DET"),
                Child(1, spanStart: 120, spanEnd: 220, topic: "R-INF"),
            }));

        var result = await Service().IngestAsync("reading", Utf8(File("reading", $"[{group}]")), "overlap.json");

        Assert.All(result.Records, r => Assert.False(r.Accepted));
        Assert.Contains(result.Records[0].Refusals, r => r.Rule == GateFourRules.SpanOverlap);
        Assert.Equal(itemsBefore, await ItemsAsync());
    }

    /// <summary>
    /// Gate one's cell balance is answered from the pool <em>and</em> from the bundle: four A-keyed
    /// siblings in a cell that holds nine keys at 3/2/2/2 must not all pass on the strength of the
    /// stored distribution alone.
    /// </summary>
    [Fact]
    public async Task Gate_one_counts_the_bundle_s_own_keys_against_the_cell_balance()
    {
        await SeedTopicsAsync();

        await using var db = Db();
        var topicId = await db.Topics.Where(t => t.Modality == "reading" && t.Code == "R-DET")
            .Select(t => t.TopicId).SingleAsync();

        // Fifteen stored B1 items in the cell at 3/4/4/4. Keying one more A makes 4/4/4/4 of sixteen,
        // which is inside the band; the bundle's own siblings are what take it out.
        var keys = new[]
        {
            "A", "A", "A",
            "B", "B", "B", "B",
            "C", "C", "C", "C",
            "D", "D", "D", "D",
        };
        for (var i = 0; i < keys.Length; i++)
        {
            var item = new Item
            {
                Modality = "reading",
                TopicId = topicId,
                CefrLevel = CefrLevel.B1,
                ItemType = "sentence_cloze",
                ContentTier = "demo",
                LifecycleStatus = "draft",
                BatchTag = "gate1-cell",
            };
            item.ItemVersions.Add(new ItemVersion
            {
                VersionNumber = 1,
                Stem = "What must every visitor do at the gate?",
                OptionA = "The night ledger.",
                OptionB = "The depot shutter.",
                OptionC = "The depot supervisor.",
                OptionD = "The office doorway.",
                KeyLabel = keys[i],
                Explanation = "The notice says every visitor signs the night ledger.",
                RationaleA = "Correct: the notice names the ledger.",
                RationaleB = "The passage never mentions a shutter.",
                RationaleC = "No supervisor appears in the passage.",
                RationaleD = "A door is not what the notice requires.",
            });
            db.Items.Add(item);
        }

        await db.SaveChangesAsync();

        // Four children, all keyed A, in that one cell. The stored cell is 3/4/4/4, so the first child's
        // A is 4/16 - legal. Counted one at a time against the stored cell only, all four would pass;
        // counted as a bundle, the second A is 5/17 (29%, still legal) and the third is 6/18 (33%), which
        // is outside the band and repairable by keying it B - so gate one refuses it by name.
        var children = Enumerable.Range(0, 4).Select(i => Child(
            i, key: "A", topic: "R-DET", spanStart: 4 + (i * 60), spanEnd: 21 + (i * 60)));
        var group = ReadingGroup(children: string.Join(",", children));

        var result = await Service().IngestAsync("reading", Utf8(File("reading", $"[{group}]")), "balance.json");

        Assert.Contains(result.Records, r => r.Refusals.Any(x => x.Rule == GateOneRules.KeyCellBalance));
    }

    // ---- the #189 gap: an imported rationale survives to the pool ---------------------------

    /// <summary>
    /// The known gap: <c>ImportManifestOption.Rationale</c> was written to the legacy
    /// <c>QuestionOptions</c> row and dropped on the way to the pool, so an imported item failed gate
    /// one for a rationale the file had supplied. The assertion is on the stored version, not on the
    /// file, because "the file carried it" is what was true before the fix.
    /// </summary>
    [Fact]
    public async Task An_imported_rationale_reaches_the_pool_version()
    {
        await SeedTopicsAsync();

        var result = await Service().IngestAsync("reading", Utf8(File("reading", $"[{ReadingGroup()}]")), "rationale.json");

        Assert.Equal(2, result.Accepted.Count);

        await using var db = Db();
        var versions = await db.ItemVersions.AsNoTracking()
            .Where(v => v.Stem == "What time does the depot open?")
            .ToListAsync();

        var version = Assert.Single(versions);
        Assert.Equal("Correct: the notice names the ledger.", version.RationaleA);
        Assert.Equal("The passage never mentions a shutter.", version.RationaleB);
        Assert.Equal("No supervisor appears in the passage.", version.RationaleC);
        Assert.Equal("A door is not what the notice requires.", version.RationaleD);
        Assert.Equal("The first sentence states the opening time.", version.Explanation);
    }
}
