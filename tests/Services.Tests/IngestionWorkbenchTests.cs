using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #205 / S-02: the staging workbench - a batch, its per-record verdicts, and one corrected record
/// re-judged without the rest of the file.
/// <para>
/// These read the pool and batch tables afterwards, not only the returned value. "The batch records
/// who submitted what" is a statement about <c>ImportBatches</c>, and "an accepted record is staged"
/// is a statement about <c>Items.LifecycleStatus</c>. An assertion on the result object would pass a
/// build that reported the right thing and wrote the wrong one.
/// </para>
/// <para>
/// Every test owns its database. Seeding the shared catalog has broken the full suite three times in
/// this rebuild, and one fixture here plants a certificate, two accounts, two topics and a batch.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class IngestionWorkbenchTests(DatabaseFixture database)
{
    // ---- the file shape, reused from #198 so a failure here is about the workbench -------------

    private static string B1Passage => PoolGateTwoTests.B1Passage;

    private const string OptionsJson = """
        [
          { "key": "A", "text": "The night ledger.", "distractor_rationale": "Correct: the notice names the ledger." },
          { "key": "B", "text": "The depot shutter.", "distractor_rationale": "The passage never mentions a shutter." },
          { "key": "C", "text": "The depot supervisor.", "distractor_rationale": "No supervisor appears in the passage." },
          { "key": "D", "text": "The office doorway.", "distractor_rationale": "A door is not what the notice requires." }
        ]
        """;

    private const string OptionsWithoutRationaleJson = """
        [
          { "key": "A", "text": "The night ledger." },
          { "key": "B", "text": "The depot shutter." },
          { "key": "C", "text": "The depot supervisor." },
          { "key": "D", "text": "The office doorway." }
        ]
        """;

    /// <summary>
    /// The four options with one option's rationale removed. Two records using this helper with
    /// different letters break <c>gate1.feedback.rationale</c> with <b>different sentences</b>, which
    /// is the input that tells a grouping on the rule code from a grouping on the prose.
    /// </summary>
    private static string OptionsMissing(string label) => $$"""
        [
          { "key": "A", "text": "The night ledger.", "distractor_rationale": {{(label == "A" ? "null" : "\"Correct: the notice names the ledger.\"")}} },
          { "key": "B", "text": "The depot shutter.", "distractor_rationale": {{(label == "B" ? "null" : "\"The passage never mentions a shutter.\"")}} },
          { "key": "C", "text": "The depot supervisor.", "distractor_rationale": {{(label == "C" ? "null" : "\"No supervisor appears in the passage.\"")}} },
          { "key": "D", "text": "The office doorway.", "distractor_rationale": {{(label == "D" ? "null" : "\"A door is not what the notice requires.\"")}} }
        ]
        """;

    private static string File(string groups) => $$"""
        {
          "schema_version": "2.0.0",
          "skill": "reading",
          "generated_at": "2026-11-06T00:00:00Z",
          "stimulus_groups": {{groups}},
          "productive_tasks": [],
          "lessons": []
        }
        """;

    private static string Child(
        int index,
        bool withExplanation = true,
        bool withRationales = true,
        int spanStart = 4,
        int spanEnd = 21,
        string level = "B1",
        string? optionsJson = null)
    {
        var stem = index == 0 ? "What time does the depot open?" : $"Which notice is posted at gate {index}?";
        var options = optionsJson ?? (withRationales ? OptionsJson : OptionsWithoutRationaleJson);
        var explanation = withExplanation
            ? "\"The first sentence states the opening time.\""
            : "null";

        return $$"""
            {
              "item_id": "item_demo_depotitem{{index:D8}}",
              "content_tier": "demo",
              "sequence_index": {{index + 1}},
              "question_type": "reading_passage_comprehension_mcq",
              "topic_id": "R-DET",
              "item_level": "{{level}}",
              "placement_eligible": false,
              "time_limit_sec": 45,
              "stem": "{{stem}}",
              "options": {{options}},
              "correct_option": "A",
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

    private static string Group(string stimulusId, int firstChild, params string[] children) => $$"""
        {
          "stimulus_id": "{{stimulusId}}",
          "content_tier": "demo",
          "version": 1,
          "lifecycle_status": "draft",
          "batch_tag": "bench-205",
          "skill": "reading",
          "stimulus_level": "B1",
          "domain_context": "workplace",
          "stimulus_format": "single_passage",
          "content": {
            "text_or_transcript": {{System.Text.Json.JsonSerializer.Serialize(B1Passage)}},
            "word_count": 299
          },
          "metrics": {
            "flesch_kincaid_grade": 6.97,
            "mltu_words": 13.0,
            "clauses_per_t_unit": 1.35,
            "target_band_lexical_coverage": 0.95
          },
          "items": [
        {{string.Join(",\n", children)}}
          ]
        }
        """;

    private static string TwoChildGroup(string stimulusId, int firstChild) =>
        Group(stimulusId, firstChild, Child(firstChild), Child(firstChild + 1, spanStart: 64, spanEnd: 81));

    private static (FakeVerifierModel One, FakeVerifierModel Two, FakeLevelClassifier Classifier) AgreeingGates()
    {
        static Dictionary<string, decimal> Confident() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 0.90m, ["B"] = 0.04m, ["C"] = 0.03m, ["D"] = 0.03m,
        };

        static Dictionary<string, decimal> Blind() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 0.28m, ["B"] = 0.24m, ["C"] = 0.24m, ["D"] = 0.24m,
        };

        return (
            new FakeVerifierModel("verifier-a", "v1") { Probabilities = Confident(), BlindProbabilities = Blind() },
            new FakeVerifierModel("verifier-b", "v2") { Probabilities = Confident(), BlindProbabilities = Blind() },
            new FakeLevelClassifier("classifier-family", "c1") { Predicted = CefrLevel.B1 });
    }

    private static Stream Utf8(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// A disposable catalog with the two accounts the two invariants read and the topics a reading
    /// record resolves against. The topics must be canonical (<c>trg_Topics_ClosedVocabulary</c>),
    /// and R-DET/R-INF are.
    /// </summary>
    private static async Task<(int Uploader, int Reviewer)> SeedAccountsAsync(CompanionDbContext db)
    {
        db.Add(new Topic { Modality = "reading", Code = "R-DET", Name = "Local factual retrieval and paraphrase matching" });
        db.Add(new Topic { Modality = "reading", Code = "R-INF", Name = "Bridging inference and logical deduction" });
        await db.SaveChangesAsync();

        var role = new Role { Code = "d37-staff", Name = "D37 staff" };
        db.Add(role);
        await db.SaveChangesAsync();

        var uploader = new User { RoleId = role.RoleId, Email = $"d37-uploader-{Guid.NewGuid():N}@test.local", PasswordHash = [1], FullName = "D37 uploader", EmailVerifiedAt = DateTimeOffset.UtcNow };
        var reviewer = new User { RoleId = role.RoleId, Email = $"d37-reviewer-{Guid.NewGuid():N}@test.local", PasswordHash = [1], FullName = "D37 reviewer", EmailVerifiedAt = DateTimeOffset.UtcNow };
        db.Add(uploader);
        db.Add(reviewer);
        await db.SaveChangesAsync();

        return (uploader.UserId, reviewer.UserId);
    }

    private static IngestionWorkbenchService Bench(
        CompanionDbContext db,
        FakeVerifierModel one,
        FakeVerifierModel two,
        FakeLevelClassifier classifier)
    {
        var gates = new PoolIngestionGates(db, verifierOne: one, verifierTwo: two, ablationSolver: null, classifier: classifier);
        var ingestion = new PoolIngestionService(db, TimeProvider.System, gates);
        return new IngestionWorkbenchService(db, TimeProvider.System, ingestion);
    }

    /// <summary>Runs <paramref name="body"/> against a fresh catalog and always drops it.</summary>
    private async Task InFreshDatabaseAsync(string suffix, Func<string, Task> body)
    {
        var dbName = $"EEBench{suffix}_{Guid.NewGuid():N}"[..30];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await body(connStr);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    // ---- AC4: the batch, its uploader and its timestamp --------------------------------------

    /// <summary>
    /// AC4. A pool upload leaves a batch that names who submitted it and when, and the batch is
    /// findable by its id afterwards. The uploader is the point: a row that records a file name and a
    /// time but not an account is a record nobody can review.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a service that created the batch row but
    /// let <c>UploadedBy</c> fall to the acting-account interceptor, which is absent in a fixture - the
    /// column would be NULL and this page would name nobody. The assertion is on the stored id.
    /// </para>
    /// </summary>
    [Fact]
    public Task An_upload_records_its_batch_its_uploader_and_its_timestamp() =>
        InFreshDatabaseAsync("Batch", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();

            var result = await Bench(db, one, two, classifier).UploadAsync(
                "reading", Utf8(File($"[{TwoChildGroup("stim_demo_benchbatch0001", 0)}]")),
                "batch.json", uploaderId);

            await using var read = DbContextFactory.Create(connStr);
            var batch = await read.ImportBatches.AsNoTracking().SingleAsync(b => b.BatchId == result.BatchId);
            Assert.Equal(uploaderId, batch.UploadedBy);
            Assert.Equal("batch.json", batch.SourceName);
            Assert.NotEqual(default, batch.CreatedAt);
            Assert.Equal("done", batch.Status);
            Assert.Null(batch.CertId);

            // And the read path finds it and names the account, which is what a later review reads.
            var listed = await Bench(read, one, two, classifier).GetBatchesAsync();
            var found = Assert.Single(listed.Batches, b => b.BatchId == result.BatchId);
            Assert.Equal(uploaderId, found.UploadedBy);
            Assert.NotEqual(default, found.CreatedAt);
        });

    // ---- AC1: a per-record verdict with the failing rule and the line ------------------------

    /// <summary>
    /// AC1. One verdict per record, carrying the file's own line and the rule that refused it. The
    /// accepted and the refused record are in the same file on purpose: a list built from refusals
    /// only would satisfy "the failing rule and the line" and omit every record that passed.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a page that renders one row per
    /// <em>refusal</em>. The record that broke the rule can carry more than one, so a per-refusal list
    /// is longer than the record count. This asserts the <em>record</em> count and that the accepted
    /// record is present with no rule.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_batch_upload_reports_one_verdict_per_record_with_its_line_and_rule() =>
        InFreshDatabaseAsync("Verdicts", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();

            var good = TwoChildGroup("stim_demo_benchgood00001", 0);
            var bad = Group(
                "stim_demo_benchbad000001", 10,
                Child(10, spanStart: 4, spanEnd: 21),
                Child(11, withExplanation: false, withRationales: false, spanStart: 64, spanEnd: 81));

            var uploaded = await Bench(db, one, two, classifier).UploadAsync(
                "reading", Utf8(File($"[{good},{bad}]")), "mixed.json", uploaderId);

            await using var read = DbContextFactory.Create(connStr);
            var detail = await Bench(read, one, two, classifier).GetBatchAsync(uploaded.BatchId);

            // Four records in the file, and four verdicts - not one per refusal.
            Assert.Equal(4, detail.Records.Count);
            Assert.Equal(4, detail.Records.Select(r => r.Line).Distinct().Count());
            var accepted = detail.Records.Where(r => r.Accepted).ToList();
            var refused = detail.Records.Where(r => !r.Accepted).ToList();
            Assert.Equal(2, accepted.Count);
            Assert.Equal(2, refused.Count);

            // The accepted record names no rule; the refused one names the gate's own code and line.
            Assert.All(accepted, r => Assert.Empty(r.Refusals));
            Assert.All(refused, r => Assert.NotEmpty(r.Refusals));
            Assert.Contains(refused.SelectMany(r => r.Refusals), r => r.Rule == GateOneRules.Explanation);

            // There ARE more refusals than refused records, so a per-refusal list would have failed
            // the count above. This states it rather than leaving it implied.
            Assert.True(
                refused.Sum(r => r.Refusals.Count) > refused.Count,
                "the fixture is meant to give a refused record more than one reason");

            Assert.All(detail.Records, r => Assert.InRange(r.Line, 1, 400));
            Assert.Equal(detail.Records.OrderBy(r => r.Line).Select(r => r.Line), detail.Records.Select(r => r.Line));
        });

    // ---- AC2: staged, not active ------------------------------------------------------------

    /// <summary>
    /// AC2. A record that passed the gates is <c>validated</c> - judged, awaiting audit - and nothing
    /// the ingestion seam writes is <c>active</c>. The second half is the one that matters: the pool's
    /// selection filters on <c>active</c>, so an import that wrote it would publish unaudited stock.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> asserting only "not active" would accept
    /// <c>draft</c>, which is the state of an item nobody judged. The assertion is the exact state, and
    /// the run proves a second item in the same cell can be <c>active</c>, so <c>validated</c> is a
    /// choice the writer makes rather than the only value the column takes.
    /// </para>
    /// </summary>
    [Fact]
    public Task An_accepted_record_is_staged_as_validated_and_never_active() =>
        InFreshDatabaseAsync("Staged", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();

            var uploaded = await Bench(db, one, two, classifier).UploadAsync(
                "reading", Utf8(File($"[{TwoChildGroup("stim_demo_benchstage00001", 0)}]")),
                "staged.json", uploaderId);

            await using var read = DbContextFactory.Create(connStr);
            var items = await read.Items.AsNoTracking()
                .Where(i => read.ImportLogs.Any(l => l.BatchId == uploaded.BatchId && l.ItemId == i.ItemId))
                .ToListAsync();

            Assert.Equal(2, items.Count);
            Assert.All(items, i => Assert.Equal("validated", i.LifecycleStatus));
            Assert.DoesNotContain(items, i => i.LifecycleStatus == "active");
        });

    // ---- AC3: one corrected record, re-judged, without re-uploading the file ----------------

    /// <summary>
    /// AC3, the criterion most likely to be quietly skipped. A refused record is corrected and
    /// re-submitted on its own; the corrected bundle is written and the rest of the file is neither
    /// re-read nor rewritten. The assertion is on both ends: the new item exists and is staged, and
    /// the accepted items from the first upload were not duplicated.
    /// </summary>
    [Fact]
    public Task A_rejected_record_can_be_corrected_and_re_judged_without_re_uploading_the_file() =>
        InFreshDatabaseAsync("Resubmit", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var good = TwoChildGroup("stim_demo_benchkeep000001", 0);
            var bad = Group(
                "stim_demo_benchfix0000001", 10,
                Child(10, spanStart: 4, spanEnd: 21),
                Child(11, withExplanation: false, spanStart: 64, spanEnd: 81));

            var uploaded = await bench.UploadAsync("reading", Utf8(File($"[{good},{bad}]")), "resubmit.json", uploaderId);
            Assert.Equal(2, uploaded.Accepted.Count);
            Assert.Equal(2, uploaded.Rejected.Count);

            await using var before = DbContextFactory.Create(connStr);
            var itemsBefore = await before.Items.CountAsync();

            // The corrected record: the only change is the missing explanation.
            var corrected = Child(11, withExplanation: true, spanStart: 64, spanEnd: 81);
            var line = uploaded.Rejected.Single(r => r.RecordId.EndsWith("00000011", StringComparison.Ordinal)).Line;

            var verdict = await bench.ResubmitAsync(uploaded.BatchId, line, corrected, uploaderId);
            Assert.True(verdict.Accepted, string.Join("; ", verdict.Refusals.Select(r => r.Message)));

            await using var read = DbContextFactory.Create(connStr);

            // Exactly the corrected bundle's two children were added - the good group was not rewritten.
            Assert.Equal(itemsBefore + 2, await read.Items.CountAsync());

            var staged = await read.Items.AsNoTracking()
                .Where(i => read.ImportLogs.Any(l => l.BatchId == uploaded.BatchId && l.ItemId == i.ItemId))
                .ToListAsync();

            // Three, not four: the bad group's other child is still refused, so only the corrected
            // record was staged on top of the good group's two.
            Assert.Equal(3, staged.Count);
            Assert.All(staged, i => Assert.Equal("validated", i.LifecycleStatus));

            // And the batch's verdict list now shows that line accepted, still one row per record.
            var detail = await bench.GetBatchAsync(uploaded.BatchId);
            Assert.Equal(4, detail.Records.Count);
            Assert.True(detail.Records.Single(r => r.Line == line).Accepted);
        });

    /// <summary>
    /// AC3's guard, and the reason re-submission exists at all. A route that clears the error flag
    /// without re-judging is the failure mode; the same uncorrected record must be refused again, and
    /// a <em>different</em> rule must be able to catch it.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a re-submit that re-runs gate one but not
    /// gate four. This submits a correction that fixes the explanation and breaks the evidence span,
    /// so only a full re-judge refuses it.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_re_submitted_record_that_still_breaks_a_rule_is_refused_and_writes_nothing() =>
        InFreshDatabaseAsync("ResubmitBad", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var bad = Group(
                "stim_demo_benchbad2_000001", 20,
                Child(20, spanStart: 4, spanEnd: 21),
                Child(21, withExplanation: false, spanStart: 64, spanEnd: 81));

            var uploaded = await bench.UploadAsync("reading", Utf8(File($"[{bad}]")), "stillbad.json", uploaderId);
            Assert.Equal(2, uploaded.Rejected.Count);

            await using var before = DbContextFactory.Create(connStr);
            var itemsBefore = await before.Items.CountAsync();

            // Fixes the explanation but moves the span onto its sibling's: gate four's overlap rule
            // refuses, which is what proves the whole gate set re-ran rather than only gate one.
            var line = uploaded.Rejected.Single(r => r.RecordId.EndsWith("00000021", StringComparison.Ordinal)).Line;
            var broken = Child(21, withExplanation: true, spanStart: 8, spanEnd: 22);

            var verdict = await bench.ResubmitAsync(uploaded.BatchId, line, broken, uploaderId);

            Assert.False(verdict.Accepted);
            Assert.NotEmpty(verdict.Refusals);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal(itemsBefore, await read.Items.CountAsync());
            Assert.Empty(await read.ImportLogs.AsNoTracking()
                .Where(l => l.BatchId == uploaded.BatchId && l.ItemId != null)
                .ToListAsync());
        });

    // ---- AC2's missing second half: the audit's clearance ----------------------------------

    /// <summary>
    /// AC2 says "until the audit clears them", and until this ticket nothing in <c>src/</c> moved an
    /// item out of <c>validated</c>. Showing a staged state with no way to promote it would be a page
    /// that parks content forever. Clearing writes <c>validated -> active</c> and records the account,
    /// so a later review can say who cleared what.
    /// </summary>
    [Fact]
    public Task Clearing_a_staged_item_promotes_it_to_active_and_records_who_did_it() =>
        InFreshDatabaseAsync("Clear", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, reviewerId) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{TwoChildGroup("stim_demo_benchclear00001", 0)}]")),
                "clear.json", uploaderId);

            var itemId = uploaded.Accepted[0].ItemId;
            Assert.NotNull(itemId);

            await bench.ClearForServiceAsync(uploaded.BatchId, itemId!.Value, reviewerId);

            await using var read = DbContextFactory.Create(connStr);
            var item = await read.Items.AsNoTracking().SingleAsync(i => i.ItemId == itemId.Value);
            Assert.Equal("active", item.LifecycleStatus);

            var decision = await read.ReviewSampleDecisions.AsNoTracking()
                .SingleAsync(d => d.BatchId == uploaded.BatchId && d.ItemId == itemId.Value);
            Assert.Equal("clear", decision.Decision);
            Assert.Equal(reviewerId, decision.ActorUserId);

            // The other child is untouched: a clearance is one item, not a batch.
            var other = await read.Items.AsNoTracking()
                .Where(i => read.ImportLogs.Any(l => l.BatchId == uploaded.BatchId && l.ItemId == i.ItemId))
                .Where(i => i.ItemId != itemId.Value)
                .SingleAsync();
            Assert.Equal("validated", other.LifecycleStatus);
        });

    /// <summary>
    /// ADM-02, enforced where it cannot be bypassed: the uploader may not clear their own batch. The
    /// refusal is the database's, from <c>trg_ReviewSampleDecisions_NoSelfApproval</c>, so an endpoint
    /// written later cannot forget it.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a permission check alone. This test calls
    /// the service directly, with no controller and no policy, so only the trigger can refuse it.
    /// </para>
    /// </summary>
    [Fact]
    public Task The_uploader_cannot_clear_an_item_in_their_own_batch() =>
        InFreshDatabaseAsync("SelfClear", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{TwoChildGroup("stim_demo_benchself000001", 0)}]")),
                "self.json", uploaderId);

            var itemId = uploaded.Accepted[0].ItemId!.Value;

            var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
                bench.ClearForServiceAsync(uploaded.BatchId, itemId, uploaderId));
            Assert.Contains("ADM-02", refused.Message, StringComparison.Ordinal);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal("validated",
                await read.Items.AsNoTracking().Where(i => i.ItemId == itemId).Select(i => i.LifecycleStatus).SingleAsync());
        });

    /// <summary>
    /// Only a staged item can be cleared. A draft was never judged by the gates, so promoting it to
    /// active would skip the judgement the whole seam exists for.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a clearance that checked only "not
    /// already active". An item in <c>quarantined</c> or <c>retired</c> is also not active, and
    /// clearing one would put a withdrawn item back into service without a re-verification.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_draft_item_cannot_be_cleared_to_active() =>
        InFreshDatabaseAsync("ClearDraft", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, reviewerId) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{TwoChildGroup("stim_demo_benchdraft00001", 0)}]")),
                "draft.json", uploaderId);

            // A pool item that no gate has judged: hand-authored stock is exactly this shape.
            var draft = new Item
            {
                Modality = "reading",
                TopicId = await db.Topics.Where(t => t.Code == "R-DET").Select(t => t.TopicId).SingleAsync(),
                CefrLevel = CefrLevel.B1,
                ItemType = "sentence_cloze",
                ContentTier = "demo",
                LifecycleStatus = "draft",
            };
            db.Items.Add(draft);
            await db.SaveChangesAsync();

            var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
                bench.ClearForServiceAsync(uploaded.BatchId, draft.ItemId, reviewerId));
            Assert.Contains("validated", refused.Message, StringComparison.OrdinalIgnoreCase);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal("draft",
                await read.Items.AsNoTracking()
                    .Where(i => i.ItemId == draft.ItemId)
                    .Select(i => i.LifecycleStatus)
                    .SingleAsync());
        });

    // ---- #207 AC1: refusals grouped by rule code ---------------------------------------------

    /// <summary>
    /// #207 AC1. Three refused records, two of them breaking the same rule, and the grouping says so:
    /// two records under one rule and one under another.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a grouping on <c>Message</c> rather than
    /// <c>RuleCode</c>. The message is prose with the option letters interpolated, so the fixture makes
    /// the two rationale-broken records break the rule for <em>different</em> options - one is missing
    /// A's rationale, one is missing C's - so their sentences differ while their rule code does not.
    /// A grouping on prose reports two groups of one here.
    /// </para>
    /// </summary>
    [Fact]
    public Task The_rejections_are_grouped_by_rule_code_and_two_records_with_one_rule_share_a_group() =>
        InFreshDatabaseAsync("RuleGroups", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            // Two different sentences, one rule code: one record is missing A's rationale, the other
            // is missing C's.
            var missingA = Group(
                "stim_demo_rulegroup00001", 0,
                Child(0, optionsJson: OptionsMissing("A")),
                Child(1, optionsJson: OptionsMissing("C"), spanStart: 64, spanEnd: 81));
            // And a third record breaking a different rule of the same gate.
            var noExplanation = Group(
                "stim_demo_rulegroup00002", 10,
                Child(10, withExplanation: false, spanStart: 4, spanEnd: 21));

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{missingA},{noExplanation}]")), "rules.json", uploaderId);

            var detail = await bench.GetBatchAsync(uploaded.BatchId);
            Assert.Equal(3, detail.Records.Count);

            var rationale = Assert.Single(detail.RuleGroups, g => g.Rule == GateOneRules.OptionRationale);
            var explanation = Assert.Single(detail.RuleGroups, g => g.Rule == GateOneRules.Explanation);

            // Two distinct records break the rationale rule; one breaks the explanation rule.
            Assert.Equal(2, rationale.Records);
            Assert.Equal(1, explanation.Records);

            // The prose really does differ inside the larger group - which is what makes this a
            // grouping on the code rather than on the sentence.
            var messages = detail.Records
                .Where(r => r.Refusals.Any(x => x.Rule == GateOneRules.OptionRationale))
                .Select(r => r.Refusals.First(x => x.Rule == GateOneRules.OptionRationale).Message)
                .Distinct()
                .Count();
            Assert.True(messages > 1, "the fixture is meant to give one rule different sentences");

            // And no group is keyed on an empty code.
            Assert.DoesNotContain(detail.RuleGroups, g => g.Rule == string.Empty);
        });

    /// <summary>
    /// #207 AC1's second half: the reviewer can narrow the list to one rule's records, and every row
    /// that comes back breaks that rule.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a filter that ignores its argument and
    /// returns the whole batch. The assertion is that the filtered list is strictly shorter than the
    /// unfiltered one, and that every returned row names the rule.
    /// </para>
    /// </summary>
    [Fact]
    public Task The_batch_can_be_read_for_one_rule_s_only() =>
        InFreshDatabaseAsync("RuleFilter", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var missingA = Group(
                "stim_demo_rulefilter0001", 0,
                Child(0, optionsJson: OptionsMissing("A")),
                Child(1, optionsJson: OptionsMissing("C"), spanStart: 64, spanEnd: 81));
            var noExplanation = Group(
                "stim_demo_rulefilter0002", 10,
                Child(10, withExplanation: false, spanStart: 4, spanEnd: 21));

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{missingA},{noExplanation}]")), "filter.json", uploaderId);

            var all = await bench.GetBatchAsync(uploaded.BatchId);
            var filtered = await bench.GetBatchAsync(uploaded.BatchId, GateOneRules.Explanation);

            Assert.True(filtered.Records.Count < all.Records.Count,
                "filtering to one rule returned every record, so the filter was ignored");
            Assert.NotEmpty(filtered.Records);
            Assert.All(filtered.Records, r =>
                Assert.Contains(r.Refusals, refusal => refusal.Rule == GateOneRules.Explanation));

            // The counts stay the batch's, not the filtered view's: the other group is still there.
            Assert.Equal(all.RuleGroups.Count, filtered.RuleGroups.Count);
        });

    // ---- #207 AC3: discard one record -------------------------------------------------------

    /// <summary>
    /// #207 AC3. One refused record is withdrawn; the records that were accepted, and the items they
    /// produced, are exactly as they were.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a discard implemented as
    /// <c>DELETE FROM ImportLog</c> for the line - the audit trail goes with it. This asserts that the
    /// refusal rows are still there <em>and</em> that a new row was appended, so a delete fails the
    /// second assertion while passing the first.
    /// </para>
    /// </summary>
    [Fact]
    public Task Discarding_one_record_keeps_its_refusal_history_and_leaves_the_accepted_records_alone() =>
        InFreshDatabaseAsync("Discard", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, reviewerId) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var good = TwoChildGroup("stim_demo_discardgood001", 0);
            var bad = Group(
                "stim_demo_discardbad0001", 10,
                Child(10, withExplanation: false, spanStart: 4, spanEnd: 21),
                Child(11, spanStart: 64, spanEnd: 81));

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{good},{bad}]")), "discard.json", uploaderId);
            Assert.Equal(2, uploaded.Accepted.Count);
            Assert.Equal(2, uploaded.Rejected.Count);

            await using var before = DbContextFactory.Create(connStr);
            var itemsBefore = await before.Items.Select(i => i.ItemId).OrderBy(id => id).ToListAsync();
            var logsBefore = await before.ImportLogs.AsNoTracking()
                .Where(l => l.BatchId == uploaded.BatchId)
                .Select(l => l.LogId)
                .ToListAsync();

            var line = uploaded.Rejected.Single(r => r.RecordId.EndsWith("00000010", StringComparison.Ordinal)).Line;

            await bench.DiscardAsync(uploaded.BatchId, line, "stock withdrawn by the author", reviewerId);

            await using var read = DbContextFactory.Create(connStr);

            // The items the batch produced are the same rows: a discard writes no item table.
            Assert.Equal(itemsBefore, await read.Items.Select(i => i.ItemId).OrderBy(id => id).ToListAsync());
            Assert.Empty(await read.ReviewSampleDecisions.AsNoTracking().ToListAsync());

            // The refusal history is intact - every row that was there is still there...
            var logsAfter = await read.ImportLogs.AsNoTracking()
                .Where(l => l.BatchId == uploaded.BatchId)
                .ToListAsync();
            Assert.All(logsBefore, id => Assert.Contains(logsAfter, l => l.LogId == id));

            // ...and a new verdict was appended for the line, at attempt 2.
            var discard = Assert.Single(logsAfter, l =>
                l.RowNo == line && l.RuleCode == IngestionWorkbenchService.DiscardRule);
            Assert.Equal(2, discard.Attempt);
            Assert.Equal("warn", discard.Level);
            Assert.Contains("stock withdrawn by the author", discard.Message, StringComparison.Ordinal);

            // The refusal rows at attempt 1 are still readable beside it.
            Assert.Contains(
                logsAfter, l => l.RowNo == line && l.Attempt == 1 && l.Level == "error");

            // And the read path shows the line as discarded rather than refused, still one row.
            var detail = await bench.GetBatchAsync(uploaded.BatchId);
            Assert.Equal(4, detail.Records.Count);
            var row = detail.Records.Single(r => r.Line == line);
            Assert.True(row.Discarded);
            Assert.False(row.Accepted);
            Assert.Empty(row.Refusals);
            Assert.Contains("stock withdrawn", row.Note!, StringComparison.Ordinal);

            // A discarded record is no longer one of the batch's outstanding refusals, so it drops out
            // of the rule groups it was counted in.
            Assert.DoesNotContain(detail.RuleGroups, g => g.Rule == GateOneRules.Explanation);
        });

    /// <summary>
    /// #207: an accepted record cannot be discarded. Its item is already staged and may already be
    /// referenced, so removing it is a lifecycle decision, not a remediation one.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a discard that only checked "the line
    /// exists". Appending a discard row over an accepted line would report the batch as having
    /// withdrawn a record whose item is still servable - the item and its verdict would then disagree
    /// about whether the record landed.
    /// </para>
    /// </summary>
    [Fact]
    public Task An_accepted_record_cannot_be_discarded() =>
        InFreshDatabaseAsync("DiscardAccepted", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{TwoChildGroup("stim_demo_discardacc0001", 0)}]")),
                "accepted.json", uploaderId);

            var accepted = uploaded.Accepted[0].Line;

            var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
                bench.DiscardAsync(uploaded.BatchId, accepted, null, uploaderId));
            Assert.Contains("accepted", refused.Message, StringComparison.OrdinalIgnoreCase);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Empty(await read.ImportLogs.AsNoTracking()
                .Where(l => l.BatchId == uploaded.BatchId && l.RuleCode == IngestionWorkbenchService.DiscardRule)
                .ToListAsync());
        });

    /// <summary>
    /// #207: one discard per record. A second discard would record a withdrawal nobody made and would
    /// count the record twice in the batch's discarded total.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a service that appends unconditionally.
    /// The second call would succeed; this asserts it is refused and that exactly one discard row
    /// exists.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_record_cannot_be_discarded_twice() =>
        InFreshDatabaseAsync("DiscardTwice", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var bad = Group(
                "stim_demo_discardtwice01", 10,
                Child(10, withExplanation: false, spanStart: 4, spanEnd: 21),
                Child(11, spanStart: 64, spanEnd: 81));

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{bad}]")), "twice.json", uploaderId);
            var line = uploaded.Rejected[0].Line;

            await bench.DiscardAsync(uploaded.BatchId, line, "gone", uploaderId);

            var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
                bench.DiscardAsync(uploaded.BatchId, line, "gone again", uploaderId));
            Assert.Contains("already discarded", refused.Message, StringComparison.OrdinalIgnoreCase);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Single(await read.ImportLogs.AsNoTracking()
                .Where(l => l.BatchId == uploaded.BatchId
                    && l.RowNo == line
                    && l.RuleCode == IngestionWorkbenchService.DiscardRule)
                .ToListAsync());
        });

    // ---- #207 AC4: the batch's status follows the remediation --------------------------------

    /// <summary>
    /// #207 AC4. A batch that ended <c>failed</c> because every record was refused becomes
    /// <c>done</c> once the last refusal is corrected - the defect this ticket exists for.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a re-submission that clears the line but
    /// leaves the status at its upload value. That is exactly the bug: <c>UploadAsync</c> set the
    /// status and <c>ResubmitAsync</c> never did, so a fully repaired batch still read "Nothing
    /// accepted". The assertion is on the stored <c>ImportBatches.Status</c>, not on the verdict.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_failed_batch_becomes_done_when_its_last_refusal_is_corrected() =>
        InFreshDatabaseAsync("StatusFixed", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            // Two independent bundles, each with one broken child. Two bundles rather than one with two
            // broken children on purpose: re-judging a group re-reads the group's STORED siblings, so a
            // bundle with two broken children can never be repaired one at a time - correcting the
            // second one finds the first one's original text again. That is #205's limitation and it is
            // reported, not worked around by rewriting resubmit here.
            var first = Group(
                "stim_demo_statusfixed0001", 0,
                Child(0, withExplanation: false, spanStart: 4, spanEnd: 21));
            var second = Group(
                "stim_demo_statusfixed0002", 10,
                Child(10, withExplanation: false, spanStart: 4, spanEnd: 21));

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{first},{second}]")), "statusfixed.json", uploaderId);
            Assert.Equal("failed", uploaded.Status);
            Assert.Equal(2, uploaded.Rejected.Count);

            // Correcting one record is not enough: the batch still has an outstanding refusal.
            var firstLine = uploaded.Rejected.Single(r => r.RecordId.EndsWith("00000000", StringComparison.Ordinal)).Line;
            var firstVerdict = await bench.ResubmitAsync(
                uploaded.BatchId, firstLine, Child(0, spanStart: 4, spanEnd: 21), uploaderId);
            Assert.True(firstVerdict.Accepted, string.Join("; ", firstVerdict.Refusals.Select(r => r.Message)));

            await using (var mid = DbContextFactory.Create(connStr))
            {
                Assert.Equal("failed",
                    await mid.ImportBatches.AsNoTracking()
                        .Where(b => b.BatchId == uploaded.BatchId)
                        .Select(b => b.Status)
                        .SingleAsync());
            }

            // Correcting the last refusal settles the batch.
            var secondLine = uploaded.Rejected.Single(r => r.RecordId.EndsWith("00000010", StringComparison.Ordinal)).Line;
            var verdict = await bench.ResubmitAsync(
                uploaded.BatchId, secondLine, Child(10, spanStart: 4, spanEnd: 21), uploaderId);
            Assert.True(verdict.Accepted, string.Join("; ", verdict.Refusals.Select(r => r.Message)));

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal("done",
                await read.ImportBatches.AsNoTracking()
                    .Where(b => b.BatchId == uploaded.BatchId)
                    .Select(b => b.Status)
                    .SingleAsync());
        });

    /// <summary>
    /// #207 AC4 at the other half: withdrawing the last outstanding refusal also settles the batch.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a status rule wired into the re-submission
    /// only. The discard path would then leave a settled batch reading "Nothing accepted" forever.
    /// This test calls only <see cref="IngestionWorkbenchService.DiscardAsync"/>.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_failed_batch_becomes_done_when_its_last_refusal_is_discarded() =>
        InFreshDatabaseAsync("StatusDiscard", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var bad = Group(
                "stim_demo_statusdisc00001", 10,
                Child(10, withExplanation: false, spanStart: 4, spanEnd: 21),
                Child(11, spanStart: 64, spanEnd: 81));

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{bad}]")), "statusdisc.json", uploaderId);
            Assert.Equal("failed", uploaded.Status);

            foreach (var record in uploaded.Rejected)
            {
                await bench.DiscardAsync(uploaded.BatchId, record.Line, "withdrawn", uploaderId);
            }

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal("done",
                await read.ImportBatches.AsNoTracking()
                    .Where(b => b.BatchId == uploaded.BatchId)
                    .Select(b => b.Status)
                    .SingleAsync());

            // And "done" does not pretend the batch produced anything: nothing was accepted.
            var detail = await bench.GetBatchAsync(uploaded.BatchId);
            Assert.Equal(2, detail.Batch.Discarded);
            Assert.Equal(0, detail.Batch.Accepted);
            Assert.Equal(0, detail.Batch.Rejected);
        });

    /// <summary>
    /// #207 AC4's trap, pinned: <c>done</c> means <b>no record is still refused</b>, so a batch that
    /// accepted some records and still holds a refusal is <b>not</b> <c>done</c>. The label on the
    /// screen says "Refusals remain", not "Nothing accepted", because the latter would be a lie about a
    /// file that mostly landed.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a rule that sets <c>done</c> as soon as
    /// <em>any</em> record settles - the shape #205's upload formula had ("failed only if every record
    /// was refused"). A file of 2 accepted and 2 refused records read "done" and hid the rejections.
    /// This fixture is that file, and it asserts the status is <c>failed</c> until the last refusal is
    /// settled.
    /// </para>
    /// <para>
    /// The three-value column (<c>pending</c>/<c>done</c>/<c>failed</c>) cannot say "partly landed",
    /// so <c>failed</c> here means "refusals remain" rather than "nothing landed". The accepted count
    /// is its own column on the list, so nothing is hidden by the word.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_batch_with_a_refusal_left_is_not_done_even_after_some_records_were_accepted() =>
        InFreshDatabaseAsync("StatusMixed", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var (uploaderId, _) = await SeedAccountsAsync(db);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var good = TwoChildGroup("stim_demo_statusmixed01", 0);
            var bad = Group(
                "stim_demo_statusmixed02", 10,
                Child(10, withExplanation: false, spanStart: 4, spanEnd: 21),
                Child(11, spanStart: 64, spanEnd: 81));

            var uploaded = await bench.UploadAsync(
                "reading", Utf8(File($"[{good},{bad}]")), "mixed.json", uploaderId);

            // Two landed, two refused - so refusals remain and the batch is not done.
            Assert.Equal(2, uploaded.Accepted.Count);
            Assert.Equal(2, uploaded.Rejected.Count);
            Assert.Equal("failed", uploaded.Status);

            // Re-submitting a still-broken correction changes nothing: no new rejection was added, and
            // a refusal was already there. The status must stay where it is rather than drift.
            var line = uploaded.Rejected.Single(r => r.RecordId.EndsWith("00000010", StringComparison.Ordinal)).Line;
            var verdict = await bench.ResubmitAsync(
                uploaded.BatchId, line, Child(10, withExplanation: false), uploaderId);
            Assert.False(verdict.Accepted);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal("failed",
                await read.ImportBatches.AsNoTracking()
                    .Where(b => b.BatchId == uploaded.BatchId)
                    .Select(b => b.Status)
                    .SingleAsync());

            // And once both refusals are withdrawn, nothing remains and the batch settles - while the
            // two accepted records and their items are still exactly where they were.
            foreach (var record in uploaded.Rejected)
            {
                await bench.DiscardAsync(uploaded.BatchId, record.Line, "withdrawn", uploaderId);
            }

            var detail = await bench.GetBatchAsync(uploaded.BatchId);
            Assert.Equal("done", detail.Batch.Status);
            Assert.Equal(2, detail.Batch.Accepted);
            Assert.Equal(2, detail.Batch.Discarded);
            Assert.Empty(detail.RuleGroups);
        });
}
