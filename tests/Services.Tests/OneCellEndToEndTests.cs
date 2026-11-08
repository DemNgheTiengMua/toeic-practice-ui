using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;
using Xunit.Abstractions;

namespace Services.Tests;

/// <summary>
/// #209: one cell populated end to end.
/// <para>
/// <b>This is the proof, not a rebuild.</b> Every test below drives the production seams in the order
/// the pipeline states them - upload, the four gates, <c>validated</c>, the stratified audit's decision,
/// <c>active</c> - and then asks a learner-facing query. Nothing here seeds <c>active</c>: the demo
/// seed's convenience is exactly what this ticket exists to prove is not the only way content becomes
/// servable.
/// </para>
/// <para>
/// <b>Why the cell is reading R-DET B1.</b> Its quota is 30 (D0.5's core-band figure, read from
/// <c>ContentQuotas</c> rather than hardcoded), so "the matrix reports the cell as satisfied" can be
/// asserted honestly: 30 active items against a target of 30 is a deficit of zero, whereas a cell left
/// at two items would make <c>Deficit == 0</c> a lie. Filling 30 through the real gates is affordable
/// and is what the criterion actually says.
/// </para>
/// <para>
/// <b>Why one child per stimulus group.</b> Gate four's span-independence rule (CNT-12) measures every
/// pair of siblings against each other, so thirty children sharing one passage would need thirty
/// disjoint spans. A group per child keeps the bundle honest (one passage, one item) and lets the span
/// rule judge what it is for: a bundle whose children cite overlapping evidence.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OneCellEndToEndTests(DatabaseFixture database, ITestOutputHelper output)
{
    /// <summary>D17's reading passage, shared with <c>PoolGateTwoTests</c> so a bound change fails there.</summary>
    private static string B1Passage => PoolGateTwoTests.B1Passage;

    /// <summary>The cell this ticket fills. Chosen because its target is 30 and every figure below is read from the row.</summary>
    private const string TopicCode = "R-DET";

    private static readonly CefrLevel Level = CefrLevel.B1;

    // ---- the file shape, reused from #198/#205 so a failure here is about the pipeline ----------

    /// <summary>
    /// The four option texts. The first is the one the stub verifier can identify, so whichever
    /// position it sits in is the item's key - which is how a balanced cell can still pass gate three.
    /// </summary>
    private static readonly (string Text, string Rationale)[] OptionTexts =
    [
        ("The night ledger.", "Correct: the notice names the ledger."),
        ("The depot shutter.", "The passage never mentions a shutter."),
        ("The depot supervisor.", "No supervisor appears here."),
        ("The office doorway.", "A door is not what the notice requires."),
    ];

    /// <summary>The word only the key option carries, so the stub verifier can recognise it.</summary>
    private const string KeyMarker = "ledger";

    /// <summary>
    /// Four options, rotated so <see cref="KeyMarker"/>'s text sits at <paramref name="keyIndex"/>.
    /// Gate one judges each item's four texts, and every option keeps the rationale it explains.
    /// </summary>
    private static string OptionsFor(int keyIndex)
    {
        var slots = new (string Text, string Rationale)?[4];
        slots[keyIndex] = OptionTexts[0];

        var rest = OptionTexts.Skip(1).ToList();
        var next = 0;
        for (var position = 0; position < 4; position++)
        {
            if (slots[position] is null)
            {
                slots[position] = rest[next++];
            }
        }

        var options = slots.Select((slot, position) =>
            $$"""{ "key": "{{("ABCD")[position]}}", "text": {{System.Text.Json.JsonSerializer.Serialize(slot!.Value.Text)}}, "distractor_rationale": {{System.Text.Json.JsonSerializer.Serialize(slot!.Value.Rationale)}} }""");

        return "[" + string.Join(",\n", options) + "]";
    }

    /// <summary>
    /// One record. The key is the position the marker text was rotated into, so the authored key and
    /// the option the stub verifier recognises are the same fact.
    /// <para>
    /// A plain A-B-C-D rotation is used here because this fixture's cell starts empty. Gate one judges
    /// each arrival against the accumulated distribution and merely <em>notices</em> a cell too small
    /// to be balanced, so four letters in order pass on an empty cell at every step.
    /// </para>
    /// </summary>
    private static string Child(int index)
    {
        var keyIndex = index % 4;
        var key = "ABCD"[keyIndex].ToString();
        return $$"""
            {
              "item_id": "item_demo_cell209{{index:D4}}",
              "content_tier": "demo",
              "sequence_index": {{index + 1}},
              "question_type": "reading_passage_comprehension_mcq",
              "topic_id": "{{TopicCode}}",
              "item_level": "{{Level.Code}}",
              "placement_eligible": false,
              "time_limit_sec": 45,
              "stem": "What time does the depot open?",
              "options": {{OptionsFor(keyIndex)}},
              "correct_option": "{{key}}",
              "explanation": "The first sentence states the opening time.",
              "evidence_span": {
                "char_start": 4,
                "char_end": 21,
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

    /// <summary>One group per child: gate four judges a bundle's siblings, and one child has no siblings.</summary>
    private static string Group(int index) => $$"""
        {
          "stimulus_id": "stim_demo_cell209{{index:D4}}",
          "content_tier": "demo",
          "version": 1,
          "lifecycle_status": "draft",
          "batch_tag": "cell-209",
          "skill": "reading",
          "stimulus_level": "{{Level.Code}}",
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
          "items": [ {{Child(index)}} ]
        }
        """;

    private static string File(IEnumerable<string> groups) => $$"""
        {
          "schema_version": "2.0.0",
          "skill": "reading",
          "generated_at": "2026-11-06T00:00:00Z",
          "stimulus_groups": [ {{string.Join(",\n", groups)}} ],
          "productive_tasks": [],
          "lessons": []
        }
        """;

    // ---- the four gates, scripted so they can pass --------------------------------------------

    /// <summary>
    /// Two heterogeneous verifiers and an independent classifier that agree with the item.
    /// <para>
    /// <b>Why a local stub rather than <see cref="FakeVerifierModel"/>.</b> That fake returns ONE fixed
    /// distribution, so it can only identify one option - which makes a key-balanced cell impossible:
    /// three of every four items would be refused for a key its script cannot see, and the cell could
    /// never reach its quota. This stub reads the options it is handed and puts its weight on the one
    /// carrying <see cref="KeyMarker"/>, so the gate is exercised on every letter.
    /// </para>
    /// <para>
    /// The blind solve returns an even split on purpose: 0.25 is below gate three's 0.50 ablation
    /// ceiling, which is the honest shape of "this item measures comprehension, not world knowledge".
    /// </para>
    /// </summary>
    private sealed class MarkerVerifier(string family, string name) : IVerifierModel
    {
        public Task<GateThreeSolve> SolveAsync(
            GateThreeSolveRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Stimulus is null)
            {
                return Task.FromResult(new GateThreeSolve(family, name, new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["A"] = 0.25m, ["B"] = 0.25m, ["C"] = 0.25m, ["D"] = 0.25m,
                }));
            }

            var keyed = request.Options.FirstOrDefault(o =>
                o.Text.Contains(KeyMarker, StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(keyed);

            var distribution = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var option in request.Options)
            {
                distribution[option.Label] = option.Label == keyed!.Label ? 0.90m : 0.03m;
            }

            return Task.FromResult(new GateThreeSolve(family, name, distribution));
        }
    }

    private static (IVerifierModel One, IVerifierModel Two, FakeLevelClassifier Classifier) AgreeingGates() =>
        (
            new MarkerVerifier("verifier-a", "v1"),
            new MarkerVerifier("verifier-b", "v2"),
            new FakeLevelClassifier("classifier-family", "c1") { Predicted = Level });

    private static IngestionWorkbenchService Bench(
        CompanionDbContext db, IVerifierModel one, IVerifierModel two, FakeLevelClassifier classifier)
    {
        var gates = new PoolIngestionGates(db, verifierOne: one, verifierTwo: two, ablationSolver: null, classifier: classifier);
        var ingestion = new PoolIngestionService(db, TimeProvider.System, gates);
        return new IngestionWorkbenchService(db, TimeProvider.System, ingestion);
    }

    private static Stream Utf8(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    // ---- the fixture --------------------------------------------------------------------------

    private sealed record Seed(int UploaderId, int ReviewerId, int LearnerId, int TopicId, int CertId, int Quota);

    /// <summary>
    /// The canonical twenty topics, one certificate with a reading skill, three accounts, and the cell's
    /// own quota read from <c>ContentQuotas</c>. The topics are a closed set (<c>trg_Topics_ClosedVocabulary</c>)
    /// and the coverage matrix is Topics x ContentQuotas, so the whole vocabulary is planted rather than
    /// one row - otherwise "every other cell is a deficit" would be asserted over a matrix that is not 120.
    /// </summary>
    private static async Task<Seed> SeedAsync(CompanionDbContext db)
    {
        var topics = new (string Modality, string Code, string Name)[]
        {
            ("listening", "L-GIST", "Global gist, purpose, macro-topic"),
            ("listening", "L-DET", "Explicit factual detail and extraction"),
            ("listening", "L-INF", "Pragmatic implication and logical inference"),
            ("listening", "L-ATT", "Speaker stance, attitude, prosodic emotion"),
            ("listening", "L-FUNC", "Communicative function and speech acts"),
            ("listening", "L-ORG", "Discourse structure, signposting, synthesis"),
            ("reading", "R-GIST", "Global thesis, purpose, paragraph main idea"),
            ("reading", "R-DET", "Local factual retrieval and paraphrase matching"),
            ("reading", "R-INF", "Bridging inference and logical deduction"),
            ("reading", "R-ATT", "Authorial stance, rhetorical purpose, tone"),
            ("reading", "R-COH", "Discourse cohesion, reference, text insertion"),
            ("reading", "R-GRAM", "Morphosyntactic structure and lexical precision"),
            ("speaking", "S-DESC", "Informational description and personal framing"),
            ("speaking", "S-INT", "Transactional interaction and option negotiation"),
            ("speaking", "S-ARG", "Sustained monologic argumentation and justification"),
            ("speaking", "S-SYN", "Integrated summary and visual/data interpretation"),
            ("writing", "W-CORR", "Transactional correspondence and register control"),
            ("writing", "W-DATA", "Visual data, process, and informational synthesis"),
            ("writing", "W-ARG", "Thesis-driven persuasive and opinion argumentation"),
            ("writing", "W-DISC", "Multi-perspective problem-solution and evaluation"),
        };

        foreach (var (modality, code, name) in topics)
        {
            if (!await db.Topics.AnyAsync(t => t.Modality == modality && t.Code == code))
            {
                db.Topics.Add(new Topic { Modality = modality, Code = code, Name = name });
            }
        }

        await db.SaveChangesAsync();

        var role = new Role { Code = $"cell209-{Guid.NewGuid():N}"[..20], Name = "Cell 209 staff" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var cert = new Certificate { Code = $"C{Guid.NewGuid():N}"[..10], Name = "Cell 209 cert", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "READ", Name = "Reading",
            Modality = Modality.Reading.ToCode(), DisplayOrder = 1, DurationMinutes = 30,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var uploader = NewUser(role.RoleId, "uploader");
        var reviewer = NewUser(role.RoleId, "reviewer");
        var learner = NewUser(role.RoleId, "learner");
        learner.ActiveCertId = cert.CertId;
        db.Users.AddRange(uploader, reviewer, learner);
        await db.SaveChangesAsync();

        var topicId = await db.Topics
            .Where(t => t.Modality == "reading" && t.Code == TopicCode)
            .Select(t => t.TopicId)
            .SingleAsync();

        // The target is read from the row, never restated here: a retuned quota must move the fixture too.
        var quota = await db.ContentQuotas
            .Where(q => q.Modality == "reading" && q.CefrLevel == Level.Code)
            .Select(q => q.TargetItemCount)
            .SingleAsync();

        return new Seed(uploader.UserId, reviewer.UserId, learner.UserId, topicId, cert.CertId, quota);
    }

    private static User NewUser(int roleId, string tag) => new()
    {
        RoleId = roleId,
        Email = $"{tag}-{Guid.NewGuid():N}@cell209.local",
        PasswordHash = [1],
        FullName = tag,
        EmailVerifiedAt = DateTimeOffset.UtcNow,
    };

    private static PracticeService Practice(string connectionString) =>
        new(DbContextFactory.Create(connectionString), new LearnerService(DbContextFactory.Create(connectionString)));

    /// <summary>Runs <paramref name="body"/> against a disposable catalog and always drops it.</summary>
    private async Task InFreshDatabaseAsync(string suffix, Func<string, Task> body)
    {
        var dbName = $"EECell209{suffix}_{Guid.NewGuid():N}"[..30];
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

    /// <summary>
    /// The whole pipeline for one cell, in the order the ticket states it, returning the ids a caller
    /// needs. <paramref name="quota"/> records arrive, one per group.
    /// </summary>
    private static async Task<(int BatchId, IReadOnlyList<int> ItemIds)> PopulateCellAsync(
        string connectionString, Seed seed, int quota)
    {
        var (one, two, classifier) = AgreeingGates();

        int batchId;
        await using (var db = DbContextFactory.Create(connectionString))
        {
            var uploaded = await Bench(db, one, two, classifier).UploadAsync(
                "reading",
                Utf8(File(Enumerable.Range(0, quota).Select(Group))),
                "cell-209.json",
                seed.UploaderId);

            Assert.Equal(quota, uploaded.Accepted.Count);
            Assert.Empty(uploaded.Rejected);
            batchId = uploaded.BatchId;
        }

        // The audit promotes the whole cell, so its decision is the only thing that makes the stock live.
        await using (var db = DbContextFactory.Create(connectionString))
        {
            var audit = new StratifiedAuditService(db, TimeProvider.System);
            var queue = await audit.DrawAsync(batchId, seed.ReviewerId);

            var stratum = Assert.Single(queue.Strata);
            Assert.Equal(TopicCode, stratum.TopicCode);
            Assert.Equal(Level.Code, stratum.CefrLevel);
            Assert.Equal(quota, stratum.ItemCount);

            // Every drawn item: solve blind, read the key, record the verdict. A missing verdict is what
            // DecideAsync refuses, so this is the step that makes the promotion possible at all.
            foreach (var sample in stratum.Items)
            {
                await audit.RecordSolveAsync(sample.SampleItemId, "A", seed.ReviewerId);
                await audit.RevealKeyAsync(sample.SampleItemId);
                await audit.RecordVerdictAsync(sample.SampleItemId, AuditVerdict.Correct, seed.ReviewerId);
            }

            var decision = await audit.DecideAsync(stratum.StratumId, seed.ReviewerId);
            Assert.Equal("approved", decision.Status);
            Assert.Equal(quota, decision.SampledPromoted + decision.UnsampledPromoted);
        }

        await using var read = DbContextFactory.Create(connectionString);
        var itemIds = await read.Items.AsNoTracking()
            .Where(i => i.TopicId == seed.TopicId && i.CefrLevel == Level)
            .OrderBy(i => i.ItemId)
            .Select(i => i.ItemId)
            .ToListAsync();

        return (batchId, itemIds);
    }

    // ---- criterion 1 (objective half) ---------------------------------------------------------

    /// <summary>
    /// Criterion 1's objective half, asserted as a state transition rather than as a column value: the
    /// items are <c>validated</c> after the upload - the gates judged them and no audit has cleared them
    /// - and <c>active</c> only after the stratum decision.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a fixture that inserted the rows
    /// <c>active</c>, or a promotion that ran before the audit. The first half asserts the exact
    /// intermediate state, so an importer that wrote <c>active</c> fails here; the second asserts the
    /// transition happened, so an audit that decided nothing fails there.
    /// </para>
    /// </summary>
    [Fact]
    public Task One_reading_cell_reaches_active_through_the_gates_and_a_stratified_decision() =>
        InFreshDatabaseAsync("Pipeline", async connStr =>
        {
            Seed seed;
            await using (var db = DbContextFactory.Create(connStr))
            {
                seed = await SeedAsync(db);
            }

            var (one, two, classifier) = AgreeingGates();

            var uploaded = await UploadOnlyAsync(connStr, seed, seed.Quota, one, two, classifier);

            await using (var staged = DbContextFactory.Create(connStr))
            {
                var states = await staged.Items.AsNoTracking()
                    .Where(i => i.TopicId == seed.TopicId && i.CefrLevel == Level)
                    .Select(i => i.LifecycleStatus)
                    .ToListAsync();

                Assert.Equal(seed.Quota, states.Count);
                Assert.All(states, s => Assert.Equal("validated", s));
            }

            await DecideCellAsync(connStr, seed, uploaded.BatchId);

            await using var live = DbContextFactory.Create(connStr);
            var active = await live.Items.AsNoTracking()
                .Where(i => i.TopicId == seed.TopicId && i.CefrLevel == Level)
                .ToListAsync();

            Assert.Equal(seed.Quota, active.Count);
            Assert.All(active, i => Assert.Equal("active", i.LifecycleStatus));
        });

    private static async Task<WorkbenchBatchResult> UploadOnlyAsync(
        string connStr, Seed seed, int count,
        IVerifierModel one, IVerifierModel two, FakeLevelClassifier classifier)
    {
        await using var db = DbContextFactory.Create(connStr);
        return await Bench(db, one, two, classifier).UploadAsync(
            "reading",
            Utf8(File(Enumerable.Range(0, count).Select(Group))),
            "cell-209.json",
            seed.UploaderId);
    }

    private static async Task DecideCellAsync(string connStr, Seed seed, int batchId)
    {
        await using var db = DbContextFactory.Create(connStr);
        var audit = new StratifiedAuditService(db, TimeProvider.System);
        var queue = await audit.DrawAsync(batchId, seed.ReviewerId);
        var stratum = Assert.Single(queue.Strata);

        foreach (var sample in stratum.Items)
        {
            await audit.RecordSolveAsync(sample.SampleItemId, "A", seed.ReviewerId);
            await audit.RevealKeyAsync(sample.SampleItemId);
            await audit.RecordVerdictAsync(sample.SampleItemId, AuditVerdict.Correct, seed.ReviewerId);
        }

        var decision = await audit.DecideAsync(stratum.StratumId, seed.ReviewerId);
        Assert.Equal("approved", decision.Status);
    }

    // ---- criterion 2 -------------------------------------------------------------------------

    /// <summary>
    /// Criterion 2: a learner runs a practice session against that cell and receives a graded result.
    /// <para>
    /// <b>Why the selector, not a session row.</b> The criterion is "a learner <em>can</em> run", so the
    /// session is started through <see cref="PracticeService.StartSessionAsync"/> on the cell's own
    /// coordinates, answered through <see cref="PracticeService.SubmitAnswerAsync"/>, and graded through
    /// <see cref="PracticeService.GetSummaryAsync"/>. A fixture that wrote a <c>PracticeAnswers</c> row
    /// itself would prove the insert and say nothing about whether an imported item can be served.
    /// </para>
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> running the drill against demo seed stock.
    /// The session's drawn item ids are asserted to be this fixture's own items, and the drill is shown
    /// to be empty <em>before</em> the promotion - so the transition, not the environment, is what made
    /// it servable.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_learner_drills_the_promoted_cell_and_receives_a_graded_result() =>
        InFreshDatabaseAsync("drill", async connStr =>
        {
            Seed seed;
            await using (var db = DbContextFactory.Create(connStr))
            {
                seed = await SeedAsync(db);
            }

            var (one, two, classifier) = AgreeingGates();
            var uploaded = await UploadOnlyAsync(connStr, seed, seed.Quota, one, two, classifier);

            // Before the audit, the cell holds stock the selector must not see.
            var beforePromotion = await Assert.ThrowsAsync<PracticeException>(() =>
                Practice(connStr).StartSessionAsync(
                    seed.LearnerId,
                    new StartPracticeSessionInput(seed.TopicId, Level) { ContentTier = ContentTiers.Demo }));

            Assert.Equal(PracticeException.NoQuestionsAvailable, beforePromotion.Code);

            await DecideCellAsync(connStr, seed, uploaded.BatchId);

            var practice = Practice(connStr);
            var session = await practice.StartSessionAsync(
                seed.LearnerId,
                new StartPracticeSessionInput(seed.TopicId, Level) { ContentTier = ContentTiers.Demo });

            Assert.Equal(seed.TopicId, session.TopicId);
            Assert.True(session.QuestionCount > 0);

            var questions = await practice.GetSessionQuestionsAsync(seed.LearnerId, session.SessionId);
            Assert.NotEmpty(questions);

            // The pinned version's key, so the drill can submit a right and a wrong answer on purpose.
            // Reading it here is not "testing the key": the criterion is that an imported item is
            // servable and graded, and grading is only demonstrated by one hit and one miss.
            var versionIds = questions.Select(q => q.ItemVersionId!.Value).ToList();
            var keys = await ReadKeysAsync(connStr, versionIds);

            for (var index = 0; index < questions.Count; index++)
            {
                var question = questions[index];
                var versionId = question.ItemVersionId!.Value;
                var key = keys[versionId];

                // The first answer is deliberately wrong, so "graded" is not "always true".
                var label = index == 0
                    ? question.Options.Select(o => o.Label).First(l => l != key)
                    : key;
                var chosen = question.Options.Single(o => o.Label == label);

                var graded = await practice.SubmitAnswerAsync(
                    seed.LearnerId, session.SessionId, versionId,
                    new SubmitPracticeAnswerInput(chosen.OptionId));

                Assert.Equal(index != 0, graded.IsCorrect);
            }

            var summary = await practice.GetSummaryAsync(seed.LearnerId, session.SessionId);
            Assert.Equal(questions.Count, summary.AnsweredCount);
            Assert.Equal(questions.Count - 1, summary.CorrectCount);
            Assert.Equal(1, summary.IncorrectCount);
            Assert.All(summary.Questions, q => Assert.NotNull(q.IsCorrect));
        });

    /// <summary>The authored key per pinned version, read once.</summary>
    private static async Task<Dictionary<int, string>> ReadKeysAsync(string connStr, IEnumerable<int> versionIds)
    {
        var ids = versionIds.ToList();
        await using var db = DbContextFactory.Create(connStr);
        var rows = await db.ItemVersions.AsNoTracking()
            .Where(v => ids.Contains(v.ItemVersionId))
            .Select(v => new { v.ItemVersionId, v.KeyLabel })
            .ToListAsync();

        return rows.ToDictionary(r => r.ItemVersionId, r => r.KeyLabel!);
    }

    // ---- criterion 3 -------------------------------------------------------------------------

    /// <summary>
    /// Criterion 3: the coverage matrix reports the filled cell as satisfied and every other cell as a
    /// deficit - the honest state - read over <b>all 120 rows</b>, not a filtered subset.
    /// <para>
    /// <b>Why exactly one and exactly 119.</b> "Satisfied" is quota-satisfaction, so the filled cell's
    /// target is read from <c>ContentQuotas</c> and its active count must equal it. A filtered
    /// <c>Deficit &gt; 0</c> over the non-empty cells would pass a matrix that silently dropped the
    /// other 118; the assertion here is the total row count and both buckets.
    /// </para>
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> asserting <c>Deficit == 0</c> on a cell
    /// holding one item against a target of 30. The fixture fills the cell to its quota and asserts the
    /// active count equals the target, so a coarser "has some stock" reading fails.
    /// </para>
    /// </summary>
    [Fact]
    public Task The_matrix_reports_exactly_one_satisfied_cell_and_119_deficits() =>
        InFreshDatabaseAsync("Matrix", async connStr =>
        {
            Seed seed;
            await using (var db = DbContextFactory.Create(connStr))
            {
                seed = await SeedAsync(db);
            }

            await PopulateCellAsync(connStr, seed, seed.Quota);

            await using var read = DbContextFactory.Create(connStr);
            var cells = await new AdminCoverageService(read).GetMatrixAsync();

            // The matrix is Topics x ContentQuotas, so the denominator is the grid and not this fixture.
            Assert.Equal(120, cells.Cells.Count);

            var satisfied = cells.Cells.Where(c => c.Deficit == 0).ToList();
            var deficient = cells.Cells.Where(c => c.Deficit > 0).ToList();

            Assert.Equal(1, satisfied.Count);
            Assert.Equal(119, deficient.Count);

            var filled = Assert.Single(satisfied);
            Assert.Equal(TopicCode, filled.TopicCode);
            Assert.Equal(Level.Code, filled.CefrLevel);
            Assert.Equal(seed.Quota, filled.TargetItemCount);
            Assert.Equal(seed.Quota, filled.ActiveItemCount);

            // No other cell holds stock at all, so every one of them is a deficit of its own target.
            Assert.All(deficient, c => Assert.Equal(0, c.ActiveItemCount));
            Assert.All(deficient, c => Assert.Equal(c.TargetItemCount, c.Deficit));
        });
}
