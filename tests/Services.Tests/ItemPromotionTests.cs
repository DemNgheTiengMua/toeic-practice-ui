using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #255: an imported item can reach a learner.
/// <para>
/// <b>The defect this file exists for, measured.</b> Before this ticket nothing in <c>src/</c> wrote
/// <c>Items.LifecycleStatus = 'active'</c>. The importer wrote <c>draft</c>, every pool selector
/// filtered <c>== "active"</c>, and the only servable items were the two the demo seed wrote directly
/// as <c>active</c> - so an item that arrived through the import path was unreachable forever.
/// </para>
/// <para>
/// <b>Every test drives the whole chain</b> - import, promote, then <see cref="PracticeService"/>'s
/// own cell selector - rather than passing an item straight to the promotion. A test that seeded the
/// column itself, or that promoted an item it fabricated, would prove the writer works and say nothing
/// about whether import can reach a learner, which is the criterion.
/// </para>
/// <para>
/// <b>The two states are distinct.</b> <c>draft</c> is "not yet judged by the four gates";
/// <c>validated</c> is "gates passed, awaiting audit"; <c>active</c> is the one state selection reads.
/// The tests below fix that vocabulary: a draft is refused promotion (it was never judged), and a
/// validated item is invisible to selection until the promotion runs.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemPromotionTests(DatabaseFixture database)
{
    // ---- the file shape, reused from #198/#205 so a failure is about promotion ----------------

    private static string B1Passage => PoolGateTwoTests.B1Passage;

    private const string OptionsJson = """
        [
          { "key": "A", "text": "The night ledger.", "distractor_rationale": "Correct: the notice names the ledger." },
          { "key": "B", "text": "The depot shutter.", "distractor_rationale": "The passage never mentions a shutter." },
          { "key": "C", "text": "The depot supervisor.", "distractor_rationale": "No supervisor appears in the passage." },
          { "key": "D", "text": "The office doorway.", "distractor_rationale": "A door is not what the notice requires." }
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

    private static string Child(int index, int spanStart = 4, int spanEnd = 21) => $$"""
        {
          "item_id": "item_demo_promoted{{index:D8}}",
          "content_tier": "demo",
          "sequence_index": {{index + 1}},
          "question_type": "reading_passage_comprehension_mcq",
          "topic_id": "R-DET",
          "item_level": "B1",
          "placement_eligible": false,
          "time_limit_sec": 45,
          "stem": "What time does the depot open?",
          "options": {{OptionsJson}},
          "correct_option": "A",
          "explanation": "The first sentence states the opening time.",
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

    private static string Group(string stimulusId, params string[] children) => $$"""
        {
          "stimulus_id": "{{stimulusId}}",
          "content_tier": "demo",
          "version": 1,
          "lifecycle_status": "draft",
          "batch_tag": "promotion-255",
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

    // ---- the fixture database ----------------------------------------------------------------

    /// <summary>
    /// Runs <paramref name="body"/> against a disposable catalog and always drops it. Every fixture here
    /// plants a certificate, accounts, topics and an upload, and the pool's cells are shared by every
    /// fixture in the suite - so the isolation is not optional.
    /// </summary>
    private async Task InFreshDatabaseAsync(string suffix, Func<string, Task> body)
    {
        var dbName = $"EEPromo{suffix}_{Guid.NewGuid():N}"[..30];
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
    /// The accounts, the topic vocabulary and the certificate a drill needs.
    /// <para>
    /// <paramref name="triageOnly"/> decides whether the reviewer's role holds
    /// <c>pool.content.review</c> alone or <c>operations.support.triage</c> alone. The triage account
    /// must hold no authoring namespace, because <c>fn_IsTriageOnly</c> is "holds triage and holds
    /// neither <c>pool.content.author</c> nor <c>assessment.blueprint.design</c>": an account that
    /// also held author would be an author, and the trigger would correctly let it write.
    /// </para>
    /// </summary>
    private static async Task<(int Uploader, int Reviewer, int Triage, int TopicId, int CertId, int Learner)>
        SeedAsync(CompanionDbContext db, string certCode)
    {
        foreach (var code in Permissions.All)
        {
            if (!await db.Permissions.AnyAsync(p => p.Code == code))
            {
                db.Permissions.Add(new Permission { Code = code });
            }
        }

        await db.SaveChangesAsync();

        var authorRole = new Role { Code = $"{certCode}A", Name = "Author" };
        var reviewRole = new Role { Code = $"{certCode}R", Name = "Reviewer" };
        var triageRole = new Role { Code = $"{certCode}T", Name = "Triage" };
        var learnerRole = new Role { Code = $"learner", Name = "Learner" };

        db.Roles.AddRange(authorRole, reviewRole, triageRole, learnerRole);
        await db.SaveChangesAsync();

        db.RolePermissions.Add(new RolePermission { RoleId = authorRole.RoleId, PermissionCode = Permissions.ContentAuthor });
        db.RolePermissions.Add(new RolePermission { RoleId = reviewRole.RoleId, PermissionCode = Permissions.ContentReview });
        db.RolePermissions.Add(new RolePermission { RoleId = triageRole.RoleId, PermissionCode = Permissions.SupportTriage });
        await db.SaveChangesAsync();

        var cert = new Certificate { Code = certCode, Name = "Promotion cert", IsActive = true };
        db.Certificates.Add(cert);
        db.Topics.Add(new Topic { Modality = "reading", Code = "R-DET", Name = "Local factual retrieval and paraphrase matching" });
        await db.SaveChangesAsync();

        var uploader = NewUser(authorRole.RoleId, "uploader");
        var reviewer = NewUser(reviewRole.RoleId, "reviewer");
        var triage = NewUser(triageRole.RoleId, "triage");
        var learner = NewUser(learnerRole.RoleId, "learner");
        learner.ActiveCertId = cert.CertId;
        db.Users.AddRange(uploader, reviewer, triage, learner);
        await db.SaveChangesAsync();

        var topicId = await db.Topics
            .Where(t => t.Modality == "reading" && t.Code == "R-DET")
            .Select(t => t.TopicId)
            .SingleAsync();

        return (uploader.UserId, reviewer.UserId, triage.UserId, topicId, cert.CertId, learner.UserId);
    }

    private static User NewUser(int roleId, string tag) => new()
    {
        RoleId = roleId,
        Email = $"{tag}-{Guid.NewGuid():N}@test.local",
        PasswordHash = [1],
        FullName = tag,
        EmailVerifiedAt = DateTimeOffset.UtcNow,
    };

    private static IngestionWorkbenchService Bench(
        CompanionDbContext db, FakeVerifierModel one, FakeVerifierModel two, FakeLevelClassifier classifier)
    {
        var gates = new PoolIngestionGates(db, verifierOne: one, verifierTwo: two, ablationSolver: null, classifier: classifier);
        var ingestion = new PoolIngestionService(db, TimeProvider.System, gates);
        return new IngestionWorkbenchService(db, TimeProvider.System, ingestion);
    }

    /// <summary>
    /// The same workbench, over a context that carries the acting account - which is what the API's
    /// <c>CompanionDbContext</c> registration does in production. A fixture context without it writes
    /// an undeclared row, which a fixture catalog tolerates and a strict one refuses; using it here
    /// would make the promotion look like it works while the deployed path could not declare itself.
    /// </summary>
    private static IngestionWorkbenchService BenchAs(
        string connectionString,
        int actingUserId,
        FakeVerifierModel one,
        FakeVerifierModel two,
        FakeLevelClassifier classifier)
    {
        var db = CreateDbAs(connectionString, actingUserId);
        var gates = new PoolIngestionGates(db, verifierOne: one, verifierTwo: two, ablationSolver: null, classifier: classifier);
        var ingestion = new PoolIngestionService(db, TimeProvider.System, gates);
        return new IngestionWorkbenchService(db, TimeProvider.System, ingestion);
    }

    // ---- AC1: import -> promote -> serve -----------------------------------------------------

    /// <summary>
    /// AC1 and AC4. An item that arrived through the import path is promoted to <c>active</c> by an
    /// authorised account, and a pool drill then selects it.
    /// <para>
    /// <b>Why this drives the selector and not the column.</b> The defect was never "the column cannot
    /// hold active" - the demo seed writes it. It was "nothing in the import path ever sets it", so the
    /// only honest test imports, promotes, and asks the learner-facing query. Every step is the
    /// production one: <see cref="IngestionWorkbenchService.UploadAsync"/>,
    /// <see cref="IngestionWorkbenchService.ClearForServiceAsync"/>, and
    /// <see cref="PracticeService.StartSessionAsync"/> on the item's own cell.
    /// </para>
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a promotion that writes <c>active</c> but a
    /// fixture whose item is already <c>active</c>. The item is asserted <c>validated</c> before the
    /// promotion and the drill is asserted to find <em>nothing</em> before it, so the second half of the
    /// assertion is a real transition rather than a no-op.
    /// </para>
    /// </summary>
    [Fact]
    public Task An_imported_item_reaches_a_pool_drill_only_after_it_is_promoted() =>
        InFreshDatabaseAsync("Serve", async connStr =>
        {
            var certCode = $"P{Suffix()}";
            await using var db = DbContextFactory.Create(connStr);
            var seed = await SeedAsync(db, certCode);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var uploaded = await bench.UploadAsync(
                "reading",
                Utf8(File($"[{Group("stim_demo_promoted00001", Child(0))}]")),
                "promote.json",
                seed.Uploader);

            var itemId = uploaded.Accepted.Single().ItemId!.Value;

            await using (var staged = DbContextFactory.Create(connStr))
            {
                Assert.Equal("validated",
                    await staged.Items.AsNoTracking()
                        .Where(i => i.ItemId == itemId)
                        .Select(i => i.LifecycleStatus)
                        .SingleAsync());
            }

            // Before the promotion the drill draws nothing: this is the defect stated as a failure.
            await Assert.ThrowsAsync<PracticeException>(() =>
                Practice(connStr).StartSessionAsync(
                    seed.Learner,
                    new StartPracticeSessionInput(seed.TopicId, CefrLevel.B1) { ContentTier = ContentTiers.Demo }));

            await BenchAs(connStr, seed.Reviewer, one, two, classifier)
                .ClearForServiceAsync(uploaded.BatchId, itemId, seed.Reviewer);

            await using (var live = DbContextFactory.Create(connStr))
            {
                var item = await live.Items.AsNoTracking().SingleAsync(i => i.ItemId == itemId);
                Assert.Equal("active", item.LifecycleStatus);
                // The write declared who made it, which is what the strict-catalog trigger reads.
                Assert.Equal(seed.Reviewer, item.LastWrittenBy);
            }

            var session = await Practice(connStr).StartSessionAsync(
                seed.Learner,
                new StartPracticeSessionInput(seed.TopicId, CefrLevel.B1) { ContentTier = ContentTiers.Demo });
            var drawn = await Practice(connStr).GetSessionQuestionsAsync(seed.Learner, session.SessionId);

            await using var read = DbContextFactory.Create(connStr);
            var drawnItemIds = await read.ItemVersions
                .Where(v => drawn.Select(q => q.ItemVersionId).Contains(v.ItemVersionId))
                .Select(v => v.ItemId)
                .ToListAsync();

            Assert.Equal([itemId], drawnItemIds);
        });

    // ---- AC2: a triage-only account is refused by the trigger --------------------------------

    /// <summary>
    /// AC2. A triage-only account is refused by the table, not by a service rule, and the refusal is
    /// the trigger's own error number.
    /// <para>
    /// <b>Why the error number and not the message.</b> A message assertion passes any failure whose
    /// text happens to contain the word - including a foreign-key error on a row this test never
    /// planted. 50081 is <c>trg_Items_TriageCannotWrite</c>'s own THROW, and nothing else in this write
    /// raises it.
    /// </para>
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a direct <c>UPDATE Items</c> with
    /// <c>LastWrittenBy</c> set to the triage account, with no service involved, would prove the
    /// trigger and say nothing about the promotion path. So the assertion is made twice: once through
    /// the gridded process (the account's real name is stamped by the interceptor) and once as a raw
    /// write, because a service that pre-empted the trigger would make the second the only visible
    /// refusal.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_triage_only_account_is_refused_by_the_item_trigger_with_its_own_error_number() =>
        InFreshDatabaseAsync("Triage", async connStr =>
        {
            var certCode = $"P{Suffix()}";
            await using var db = DbContextFactory.Create(connStr);
            var seed = await SeedAsync(db, certCode);

            var itemId = await SeedValidatedItemAsync(db, seed.TopicId);

            // Stamp the triage account the way production does: through the real interceptor, so the
            // column the trigger reads names the account that is actually writing.
            await using var triageDb = CreateDbAs(connStr, seed.Triage);
            var item = await triageDb.Items.SingleAsync(i => i.ItemId == itemId);
            item.LifecycleStatus = "active";

            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => triageDb.SaveChangesAsync());
            var sql = Assert.IsType<SqlException>(refused.InnerException);
            Assert.Equal(50081, sql.Number);

            await using (var read = DbContextFactory.Create(connStr))
            {
                Assert.Equal("validated",
                    await read.Items.AsNoTracking()
                        .Where(i => i.ItemId == itemId)
                        .Select(i => i.LifecycleStatus)
                        .SingleAsync());
            }

            // The control: the identical write by the reviewer is accepted. Without it, a trigger that
            // refused every lifecycle write - or a fixture catalog that refused the declaration - would
            // satisfy the assertion above and prove nothing about triage.
            await using (var reviewerDb = CreateDbAs(connStr, seed.Reviewer))
            {
                var same = await reviewerDb.Items.SingleAsync(i => i.ItemId == itemId);
                same.LifecycleStatus = "active";
                await reviewerDb.SaveChangesAsync();
            }

            await using var after = DbContextFactory.Create(connStr);
            Assert.Equal("active", await StatusOfAsync(after, itemId));
            Assert.Equal(seed.Reviewer, await after.Items.AsNoTracking()
                .Where(i => i.ItemId == itemId)
                .Select(i => i.LastWrittenBy)
                .SingleAsync());
        });

    /// <summary>
    /// AC3. The intermediate state is <c>validated</c>, and promotion is unreachable from <c>draft</c>:
    /// a draft was never judged, so clearing it would publish stock the gates refused to look at.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a promotion that checks only "not already
    /// active". <c>quarantined</c> and <c>retired</c> are also not active, and promoting one would put
    /// a withdrawn item back into service without re-verification. The fixture therefore also holds a
    /// <c>quarantined</c> item of the same cell, and both are refused.
    /// </para>
    /// </summary>
    [Fact]
    public Task Only_a_validated_item_can_be_promoted_never_a_draft_or_a_quarantined_one() =>
        InFreshDatabaseAsync("Draft", async connStr =>
        {
            var certCode = $"P{Suffix()}";
            await using var db = DbContextFactory.Create(connStr);
            var seed = await SeedAsync(db, certCode);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var uploaded = await bench.UploadAsync(
                "reading",
                Utf8(File($"[{Group("stim_demo_promoted00002", Child(10))}]")),
                "draft.json",
                seed.Uploader);

            var acceptedItemId = uploaded.Accepted.Single().ItemId!.Value;

            // A hand-authored draft in the same batch, so the only difference is the state.
            var draftId = await SeedItemAsync(db, seed.TopicId, "draft");
            var quarantinedId = await SeedItemAsync(db, seed.TopicId, "quarantined");

            foreach (var refusedId in new[] { draftId, quarantinedId })
            {
                var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
                    bench.ClearForServiceAsync(uploaded.BatchId, refusedId, seed.Reviewer));
                Assert.Contains("validated", refused.Message, StringComparison.OrdinalIgnoreCase);
            }

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal("draft", await StatusOfAsync(read, draftId));
            Assert.Equal("quarantined", await StatusOfAsync(read, quarantinedId));

            // The control: the validated item from the same batch does clear, so the refusals above
            // were about the state and not about the batch or the reviewer.
            await BenchAs(connStr, seed.Reviewer, one, two, classifier)
                .ClearForServiceAsync(uploaded.BatchId, acceptedItemId, seed.Reviewer);
            await using var after = DbContextFactory.Create(connStr);
            Assert.Equal("active", await StatusOfAsync(after, acceptedItemId));
        });

    /// <summary>
    /// The batch an item is cleared under must be the batch it arrived in. <c>trg_ReviewSampleDecisions_
    /// NoSelfApproval</c> reads the batch's uploader, so a decision filed under some other batch would
    /// name a different account and launder the self-approval the trigger exists to refuse.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a clearance that trusted the two ids and
    /// never checked they were related. With two batches and two uploaders, promoting item B under
    /// batch A's id would pass a check that only compared states, and the decision row would then name
    /// the wrong batch for the rest of its life.
    /// </para>
    /// </summary>
    [Fact]
    public Task An_item_cannot_be_cleared_under_a_batch_it_did_not_arrive_in() =>
        InFreshDatabaseAsync("WrongBatch", async connStr =>
        {
            var certCode = $"P{Suffix()}";
            await using var db = DbContextFactory.Create(connStr);
            var seed = await SeedAsync(db, certCode);
            var (one, two, classifier) = AgreeingGates();
            var bench = Bench(db, one, two, classifier);

            var first = await bench.UploadAsync(
                "reading",
                Utf8(File($"[{Group("stim_demo_promoted00003", Child(20))}]")),
                "first.json",
                seed.Uploader);
            var second = await bench.UploadAsync(
                "reading",
                Utf8(File($"[{Group("stim_demo_promoted00004", Child(30, spanStart: 4, spanEnd: 21))}]")),
                "second.json",
                seed.Uploader);

            var itemOfSecond = second.Accepted.Single().ItemId!.Value;

            var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
                bench.ClearForServiceAsync(first.BatchId, itemOfSecond, seed.Reviewer));
            Assert.Contains("did not arrive", refused.Message, StringComparison.OrdinalIgnoreCase);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal("validated", await StatusOfAsync(read, itemOfSecond));
            Assert.Empty(await read.ReviewSampleDecisions.AsNoTracking()
                .Where(d => d.BatchId == first.BatchId)
                .ToListAsync());
        });

    // ---- helpers -----------------------------------------------------------------------------

    private static string Suffix() => Guid.NewGuid().ToString("N")[..10];

    private static CompanionDbContext CreateDbAs(string connectionString, int userId)
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new StaffAccountStampInterceptor(new Acting(userId)))
            .Options;
        return new CompanionDbContext(options);
    }

    private sealed class Acting(int userId) : IActingStaffAccount
    {
        public int? UserId { get; } = userId;
    }

    private static PracticeService Practice(string connectionString) =>
        new(DbContextFactory.Create(connectionString), new LearnerService(DbContextFactory.Create(connectionString)));

    private static Task<string> StatusOfAsync(CompanionDbContext db, int itemId) =>
        db.Items.AsNoTracking().Where(i => i.ItemId == itemId).Select(i => i.LifecycleStatus).SingleAsync();

    /// <summary>
    /// A pool item the gates judged and the audit has not cleared - the state the ingestion seam
    /// writes. A version is required because a cell read joins on the newest one.
    /// </summary>
    private static async Task<int> SeedValidatedItemAsync(CompanionDbContext db, int topicId) =>
        await SeedItemAsync(db, topicId, "validated");

    private static async Task<int> SeedItemAsync(CompanionDbContext db, int topicId, string lifecycle)
    {
        var stem = $"A {lifecycle} item at {Guid.NewGuid():N}.";
        var item = new Item
        {
            Modality = "reading",
            TopicId = topicId,
            CefrLevel = CefrLevel.B1,
            ItemType = "sentence_cloze",
            ContentTier = "demo",
            LifecycleStatus = lifecycle,
        };
        db.Items.Add(item);
        db.ItemVersions.Add(new ItemVersion
        {
            Item = item,
            VersionNumber = 1,
            Stem = stem,
            OptionA = $"{stem}-a",
            OptionB = $"{stem}-b",
            OptionC = $"{stem}-c",
            OptionD = $"{stem}-d",
            KeyLabel = "A",
            Explanation = "Because the passage says so.",
            RationaleA = "Correct.",
            RationaleB = "Not this.",
            RationaleC = "Not this.",
            RationaleD = "Not this.",
        });
        await db.SaveChangesAsync();
        return item.ItemId;
    }
}
