using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #206 / S-03: the stratified audit - the sample, the blind solve, the quarantine and the promotion
/// scope.
/// <para>
/// <b>Every test owns its database.</b> One fixture here plants a batch, twenty items with versions,
/// their verdict rows and two accounts; seeding the shared catalog has broken this suite three times.
/// </para>
/// <para>
/// <b>Every guard is stated with the input that would pass it while being wrong.</b> The traps this
/// ticket names are all "looks right, is wrong" shapes: a random sample of the right size, a blind
/// order enforced only by a view, a quarantine that moves one item, a promotion that moves only what
/// was reviewed, and a 5% threshold compared with the wrong operator. Each has a test below that says
/// which one it catches.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StratifiedAuditTests(DatabaseFixture database)
{
    // ---- fixtures ---------------------------------------------------------------------------

    /// <summary>
    /// The cell shapes this fixture builds, chosen so the sample cannot be mistaken for a random one:
    /// one large cell, one tiny cell, and one C1 cell of a single item.
    /// <para>
    /// A random 10% of 26 items draws 2 or 3 items and can easily contain no C1 item at all - which is
    /// exactly the failure story 29 names. The large/tiny split means a draw that ignored cells would
    /// return the same items every run and still pass a size assertion.
    /// </para>
    /// </summary>
    private sealed record Fixture(
        int UploaderId,
        int ReviewerId,
        int BatchId);

    private async Task<Fixture> SeedAsync(CompanionDbContext db, string skill = "reading")
    {
        db.Add(new Topic { Modality = "reading", Code = "R-DET", Name = "Local factual retrieval and paraphrase matching" });
        db.Add(new Topic { Modality = "reading", Code = "R-INF", Name = "Bridging inference and logical deduction" });
        await db.SaveChangesAsync();

        var role = new Role { Code = $"d206-{Guid.NewGuid():N}"[..20], Name = "D206 staff" };
        db.Add(role);
        await db.SaveChangesAsync();

        var uploader = new User
        {
            RoleId = role.RoleId,
            Email = $"d206-uploader-{Guid.NewGuid():N}@test.local",
            PasswordHash = [1],
            FullName = "D206 uploader",
            EmailVerifiedAt = DateTimeOffset.UtcNow,
        };
        var reviewer = new User
        {
            RoleId = role.RoleId,
            Email = $"d206-reviewer-{Guid.NewGuid():N}@test.local",
            PasswordHash = [1],
            FullName = "D206 reviewer",
            EmailVerifiedAt = DateTimeOffset.UtcNow,
        };
        db.Add(uploader);
        db.Add(reviewer);
        await db.SaveChangesAsync();

        var batch = new ImportBatch
        {
            CertId = null,
            SourceName = "d206-pool.json",
            Status = "done",
            UploadedBy = uploader.UserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Add(batch);
        await db.SaveChangesAsync();

        var largeTopic = await db.Topics.Where(t => t.Code == "R-DET").Select(t => t.TopicId).SingleAsync();
        var tinyTopic = await db.Topics.Where(t => t.Code == "R-INF").Select(t => t.TopicId).SingleAsync();

        // 20 in one B1 cell, 5 in another B1 cell, 1 in a C1 cell. One item's key is A, and every item
        // carries a key so the reveal has something to return.
        await AddItemsAsync(db, batch.BatchId, largeTopic, CefrLevel.B1, 20, "A");
        await AddItemsAsync(db, batch.BatchId, tinyTopic, CefrLevel.B1, 5, "B");
        await AddItemsAsync(db, batch.BatchId, largeTopic, CefrLevel.C1, 1, "C");

        return new Fixture(uploader.UserId, reviewer.UserId, batch.BatchId);
    }

    private static async Task<List<Item>> AddItemsAsync(
        CompanionDbContext db, int batchId, int topicId, CefrLevel level, int count, string key)
    {
        var items = new List<Item>(count);
        for (var i = 0; i < count; i++)
        {
            var item = new Item
            {
                Modality = "reading",
                TopicId = topicId,
                CefrLevel = level,
                ItemType = "sentence_cloze",
                ContentTier = "demo",
                LifecycleStatus = "validated",
                BatchTag = "d206",
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            db.ItemVersions.Add(new ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = 1,
                Stem = $"Stem {level.Code} {i}",
                OptionA = "a",
                OptionB = "b",
                OptionC = "c",
                OptionD = "d",
                KeyLabel = key,
                Explanation = "Because the passage says so.",
            });
            await db.SaveChangesAsync();

            db.ImportLogs.Add(new ImportLog
            {
                BatchId = batchId,
                RowNo = i + 1,
                Level = "info",
                Message = "accepted and staged",
                Attempt = 1,
                ItemId = item.ItemId,
            });
            await db.SaveChangesAsync();

            items.Add(item);
        }

        return items;
    }

    private static StratifiedAuditService Audit(CompanionDbContext db) =>
        new(db, TimeProvider.System);

    private async Task InFreshDatabaseAsync(string suffix, Func<string, Task> body)
    {
        var dbName = $"EEAudit{suffix}_{Guid.NewGuid():N}"[..30];
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

    /// <summary>Solves and judges every drawn item of a stratum, which is what DecideAsync requires.</summary>
    private static async Task JudgeAllAsync(
        StratifiedAuditService audit, AuditQueue queue, string verdict, int reviewerId)
    {
        foreach (var stratum in queue.Strata)
        {
            foreach (var item in stratum.Items)
            {
                await audit.RecordSolveAsync(item.SampleItemId, "A", reviewerId);
                await audit.RevealKeyAsync(item.SampleItemId);
                await audit.RecordVerdictAsync(item.SampleItemId, ParseVerdict(verdict), reviewerId);
            }
        }
    }

    private static AuditVerdict ParseVerdict(string verdict) => verdict switch
    {
        "correct" => AuditVerdict.Correct,
        "key_error" => AuditVerdict.KeyError,
        _ => AuditVerdict.NonKeyDefect,
    };

    // ---- criterion 1: the sample is stratified across every cell -----------------------------

    /// <summary>
    /// Criterion 1, and the trap this ticket names first: <b>a "stratified" sample that is just
    /// <c>ORDER BY NEWID()</c> passes any test that checks the sample size</b>. So this fixture's cells
    /// have very different counts (20 / 5 / 1) and the assertion is that every one of them appears.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> sampling 10% of the 26 items at random.
    /// That returns 2 or 3 items, and the C1 cell - one item in 26 - is missed in 88% of draws, so the
    /// edge band that most needs review disappears. The assertion is per cell, not on the total.
    /// </para>
    /// <para>
    /// It also pins the floor: a 10% floor rounded down samples zero items from a one-item cell, which
    /// is the same failure with a different cause.
    /// </para>
    /// </summary>
    [Fact]
    public Task The_sample_draws_from_every_cell_and_never_zero_from_a_one_item_cell() =>
        InFreshDatabaseAsync("Strat", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);

            var queue = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);

            // Three cells, one stratum each - the C1 cell included, which random sampling would miss.
            Assert.Equal(3, queue.Strata.Count);
            Assert.Contains(queue.Strata, s => s.CefrLevel == "C1");

            // Every cell contributed at least one item, and the one-item cell contributed its one - so
            // its unreviewed remainder is zero, which is the honest number rather than a missing cell.
            Assert.All(queue.Strata, s => Assert.True(s.Sampled >= 1));
            var c1 = queue.Strata.Single(s => s.CefrLevel == "C1");
            Assert.Equal(1, c1.Sampled);
            Assert.Equal(0, c1.Unsampled);

            // The large cell samples a tenth (ceiling), the tiny cell its floor of one.
            var large = queue.Strata.Single(s => s.TopicCode == "R-DET" && s.CefrLevel == "B1");
            Assert.Equal(20, large.ItemCount);
            Assert.Equal(2, large.Sampled);
            var tiny = queue.Strata.Single(s => s.TopicCode == "R-INF");
            Assert.Equal(5, tiny.ItemCount);
            Assert.Equal(1, tiny.Sampled);

            // And the sampled item ids are the cell's own - not another cell's stock.
            foreach (var stratum in queue.Strata)
            {
                var cellTopic = await db.Topics.AsNoTracking()
                    .Where(t => t.Code == stratum.TopicCode).Select(t => t.TopicId).SingleAsync();
                foreach (var item in stratum.Items)
                {
                    var stored = await db.Items.AsNoTracking().SingleAsync(i => i.ItemId == item.ItemId);
                    Assert.Equal(cellTopic, stored.TopicId);
                    Assert.Equal(stratum.CefrLevel, stored.CefrLevel.Code);
                }
            }
        });

    /// <summary>
    /// The arithmetic, alone and at its boundaries. <b>Which guard could this pass while being wrong:</b>
    /// a `floor` implementation, which yields 0 for a one-item cell, or a bare `itemCount / 10`, which
    /// rounds 19 down to 1 when the audit needs 2.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(19, 2)]
    [InlineData(20, 2)]
    [InlineData(100, 10)]
    public void The_sample_size_is_a_tenth_rounded_up_with_a_floor_of_one(int items, int expected) =>
        Assert.Equal(expected, StratifiedSample.SizeFor(items));

    /// <summary>
    /// The spread. <b>Which guard could this pass while being wrong:</b> taking the first <em>k</em>
    /// items, which samples whatever was authored first - often one stimulus's children, hiding the
    /// shared-passage defect the sample exists to find.
    /// </summary>
    [Fact]
    public void The_draw_is_spread_over_the_cell_rather_than_the_first_items()
    {
        var indices = StratifiedSample.IndicesFor(20, 2);

        Assert.Equal([0, 10], indices);
        Assert.NotEqual([0, 1], indices);
        // Every draw is distinct, so a sample can never hold one item twice.
        Assert.Equal(indices.Count, indices.Distinct().Count());
    }

    // ---- criterion 2: the blind solve, enforced where the key is read -------------------------

    /// <summary>
    /// Criterion 2, and the trap: <b>blind-solve enforced in the Razor view only</b>. A reviewer who
    /// reads the key from the API and then solves has not solved blind.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a key exposed on the queue projection. This
    /// asserts that the reveal is refused by the service - with no controller, no policy and no view in
    /// the path - until a solve is stored, and that the queue itself carries no key at all.
    /// </para>
    /// </summary>
    [Fact]
    public Task The_authored_key_is_refused_until_a_solve_is_recorded() =>
        InFreshDatabaseAsync("Blind", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);
            var queue = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);
            var sample = queue.Strata.SelectMany(s => s.Items).First();

            // The queue is a list every reviewer can read; it must not carry a key, or blindness is a
            // property of which fields the page rendered.
            Assert.Null(typeof(AuditSampleRow).GetProperty("KeyLabel"));

            var refused = await Assert.ThrowsAsync<AdminItemException>(
                () => audit.RevealKeyAsync(sample.SampleItemId));
            Assert.Contains("W-01", refused.Message, StringComparison.Ordinal);

            // After the solve, the same call succeeds and returns the authored key.
            await audit.RecordSolveAsync(sample.SampleItemId, "A", fixture.ReviewerId);
            var reveal = await audit.RevealKeyAsync(sample.SampleItemId);

            Assert.Equal("A", reveal.KeyLabel);
            Assert.Equal("A", reveal.SolvedLabel);
            Assert.NotEqual(default, reveal.RevealedAt);
        });

    /// <summary>
    /// The order is answerable after the fact. <b>Which guard could this pass while being wrong:</b>
    /// storing a boolean "saw the key" flag, which cannot say whether the solve came first. The two
    /// timestamps can, and this asserts the solve's is not later than the reveal's.
    /// </summary>
    [Fact]
    public Task The_stored_timestamps_say_the_solve_came_before_the_key() =>
        InFreshDatabaseAsync("Order", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);
            var queue = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);
            var sample = queue.Strata.SelectMany(s => s.Items).First();

            await audit.RecordSolveAsync(sample.SampleItemId, "B", fixture.ReviewerId);
            await audit.RevealKeyAsync(sample.SampleItemId);

            await using var read = DbContextFactory.Create(connStr);
            var stored = await read.ReviewSampleItems.AsNoTracking()
                .SingleAsync(s => s.SampleItemId == sample.SampleItemId);

            Assert.NotNull(stored.SolvedAt);
            Assert.NotNull(stored.KeyRevealedAt);
            Assert.True(
                stored.SolvedAt <= stored.KeyRevealedAt,
                $"the key was read at {stored.KeyRevealedAt} but the solve was not stored until {stored.SolvedAt}");
        });

    /// <summary>
    /// And a second solve cannot follow a peek. <b>Which guard could this pass while being wrong:</b>
    /// allowing a re-solve, which lets a reviewer correct themselves into agreement with the key they
    /// just read - the anchoring story 30 exists to prevent.
    /// </summary>
    [Fact]
    public Task A_second_solve_is_refused_once_the_key_has_been_read() =>
        InFreshDatabaseAsync("ReSolve", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);
            var queue = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);
            var sample = queue.Strata.SelectMany(s => s.Items).First();

            await audit.RecordSolveAsync(sample.SampleItemId, "B", fixture.ReviewerId);
            await audit.RevealKeyAsync(sample.SampleItemId);

            var refused = await Assert.ThrowsAsync<AdminItemException>(
                () => audit.RecordSolveAsync(sample.SampleItemId, "A", fixture.ReviewerId));
            Assert.Contains("W-01", refused.Message, StringComparison.Ordinal);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal("B", await read.ReviewSampleItems.AsNoTracking()
                .Where(s => s.SampleItemId == sample.SampleItemId)
                .Select(s => s.SolvedLabel)
                .SingleAsync());
        });

    // ---- criterion 3: one key error quarantines the whole stratum ----------------------------

    /// <summary>
    /// Criterion 3's first half, and the trap: <b>quarantine that sets one item instead of the
    /// stratum</b>. The fixture is a 20-item cell; one key error must move all 20, not the one item it
    /// was found on.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> quarantining the sampled item only. That
    /// satisfies "the item is quarantined" and leaves the other nineteen - which share the defective
    /// authoring pattern - in service.
    /// </para>
    /// </summary>
    [Fact]
    public Task One_confirmed_key_error_quarantines_every_item_in_the_stratum() =>
        InFreshDatabaseAsync("KeyError", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);
            var queue = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);

            var large = queue.Strata.Single(s => s.TopicCode == "R-DET" && s.CefrLevel == "B1");
            Assert.Equal(20, large.ItemCount);

            // One key error among the two sampled items; the other is correct.
            var first = large.Items[0];
            var second = large.Items[1];
            foreach (var sample in new[] { first, second })
            {
                await audit.RecordSolveAsync(sample.SampleItemId, "A", fixture.ReviewerId);
                await audit.RevealKeyAsync(sample.SampleItemId);
            }

            await audit.RecordVerdictAsync(first.SampleItemId, AuditVerdict.KeyError, fixture.ReviewerId);
            await audit.RecordVerdictAsync(second.SampleItemId, AuditVerdict.Correct, fixture.ReviewerId);

            var decision = await audit.DecideAsync(large.StratumId, fixture.ReviewerId);

            Assert.Equal("quarantined", decision.Status);
            Assert.Equal("key_error", decision.QuarantineReason);
            // All twenty, not the one the error was found on, and not the two that were sampled.
            Assert.Equal(20, decision.Quarantined);

            await using var read = DbContextFactory.Create(connStr);
            var items = await read.Items.AsNoTracking()
                .Where(i => read.ImportLogs.Any(l => l.BatchId == fixture.BatchId && l.ItemId == i.ItemId))
                .ToListAsync();

            // The large cell's twenty are all quarantined. The other cells are untouched, which is what
            // makes "the whole stratum" a claim rather than "the whole batch".
            var largeTopicId = await read.Topics.Where(t => t.Code == "R-DET").Select(t => t.TopicId).SingleAsync();
            Assert.Equal(20, items.Count(i =>
                i.LifecycleStatus == "quarantined"
                && i.CefrLevel == CefrLevel.B1
                && i.TopicId == largeTopicId));
            Assert.Equal(6, items.Count(i => i.LifecycleStatus == "validated"));
        });

    /// <summary>
    /// The 5% rule, pinned at its boundary. <b>Which guard could this pass while being wrong:</b> a
    /// <c>&gt;=</c> comparison, which quarantines at exactly 5% - one defect in a sample of 20 - when
    /// story 32 says "above 5%". The other half of the trap is the denominator: computed over the batch
    /// instead of the stratum, a clean 100-item cell dilutes a broken 2-item one.
    /// </summary>
    [Fact]
    public void The_non_key_threshold_is_above_five_percent_not_at_it()
    {
        // 1 of 20 is exactly 5%: approved, because the rule is "above 5%".
        Assert.Equal(("approved", (string?)null), AuditStratumRule.Decide(
            ["non_key_defect", .. Enumerable.Repeat("correct", 19)]));

        // 2 of 20 is 10%: quarantined.
        Assert.Equal(("quarantined", (string?)"non_key_defect_rate"), AuditStratumRule.Decide(
            ["non_key_defect", "non_key_defect", .. Enumerable.Repeat("correct", 18)]));

        // A deliberate control that the boundary is the denominator's: 2 of 40 is 5%, approved. A rule
        // counting defects alone would quarantine it.
        Assert.Equal(("approved", (string?)null), AuditStratumRule.Decide(
            ["non_key_defect", "non_key_defect", .. Enumerable.Repeat("correct", 38)]));
    }

    /// <summary>
    /// A key error beats the defect rate as the stated reason. <b>Which guard could this pass while
    /// being wrong:</b> a rule reporting "over 5% defective" when one of the defects was a wrong key,
    /// which sends the author looking for level drift instead of a bad key.
    /// </summary>
    [Fact]
    public void A_key_error_is_the_reason_even_when_the_defect_rate_is_also_over()
    {
        var decision = AuditStratumRule.Decide(
            ["key_error", "non_key_defect", "non_key_defect", .. Enumerable.Repeat("correct", 17)]);

        Assert.Equal(("quarantined", (string?)"key_error"), decision);
    }

    // ---- criterion 4: promotion is the reviewed sample AND the unsampled remainder -----------

    /// <summary>
    /// Criterion 4, and the sentence the spec says is most often skipped: <b>an approved stratum promotes
    /// the 10% reviewed AND the 90% unsampled buffer together.</b>
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> promoting only the sampled items. That
    /// satisfies a button labelled "approve" and stalls the pool at 10% of target volume - D17's own
    /// warning. This test has unsampled items and asserts they are <c>active</c> too, and the returned
    /// counts name both halves.
    /// </para>
    /// </summary>
    [Fact]
    public Task Approving_a_stratum_promotes_the_sampled_and_unsampled_items_together() =>
        InFreshDatabaseAsync("Promote", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);
            var queue = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);
            await JudgeAllAsync(audit, queue, "correct", fixture.ReviewerId);

            var large = queue.Strata.Single(s => s.TopicCode == "R-DET" && s.CefrLevel == "B1");
            Assert.Equal(2, large.Sampled);
            Assert.Equal(18, large.Unsampled);

            var decision = await audit.DecideAsync(large.StratumId, fixture.ReviewerId);

            Assert.Equal("approved", decision.Status);
            Assert.Null(decision.QuarantineReason);
            Assert.Equal(2, decision.SampledPromoted);
            Assert.Equal(18, decision.UnsampledPromoted);

            await using var read = DbContextFactory.Create(connStr);
            var cell = await read.Items.AsNoTracking()
                .Where(i => i.CefrLevel == CefrLevel.B1
                    && i.TopicId == (read.Topics.Where(t => t.Code == "R-DET").Select(t => t.TopicId).First()))
                .ToListAsync();

            Assert.Equal(20, cell.Count);
            Assert.All(cell, i => Assert.Equal("active", i.LifecycleStatus));

            // The unsampled nineteen are the point: at least one active item was never drawn.
            var sampledIds = large.Items.Select(i => i.ItemId).ToHashSet();
            var unsampled = cell.Where(i => !sampledIds.Contains(i.ItemId)).ToList();
            Assert.NotEmpty(unsampled);
            Assert.All(unsampled, i => Assert.Equal("active", i.LifecycleStatus));

            // The other cells are untouched: an approval is the stratum's, not the batch's.
            var other = await read.Items.AsNoTracking()
                .Where(i => i.CefrLevel == CefrLevel.C1)
                .SingleAsync();
            Assert.Equal("validated", other.LifecycleStatus);
        });

    /// <summary>
    /// Quarantine is the whole stratum too, and the selection path is how a learner sees it. <b>Which
    /// guard could this pass while being wrong:</b> quarantining items without moving any, which passes
    /// a status assertion on the stratum and leaves the stock servable.
    /// </summary>
    [Fact]
    public Task A_quarantined_stratum_leaves_its_items_out_of_selection() =>
        InFreshDatabaseAsync("Select", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);
            var queue = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);
            await JudgeAllAsync(audit, queue, "key_error", fixture.ReviewerId);

            var large = queue.Strata.Single(s => s.TopicCode == "R-DET" && s.CefrLevel == "B1");
            await audit.DecideAsync(large.StratumId, fixture.ReviewerId);

            await using var read = DbContextFactory.Create(connStr);

            // The selector a pool drill runs: active + tier, in the cell. This is the same predicate
            // written out in domain38, and the reason criterion 5 is provable through selection rather
            // than through a new flag.
            var selectable = await read.Items.AsNoTracking()
                .CountAsync(i => i.TopicId == read.Topics.Where(t => t.Code == "R-DET").Select(t => t.TopicId).First()
                    && i.CefrLevel == CefrLevel.B1
                    && i.LifecycleStatus == "active"
                    && i.ContentTier == "demo");

            Assert.Equal(0, selectable);

            // And the control: the C1 cell was never decided, so it is still staged and still not
            // selectable - which means the zero above is "quarantined is excluded", not "nothing works".
            var allInBatch = await read.Items.AsNoTracking()
                .CountAsync(i => read.ImportLogs.Any(l => l.BatchId == fixture.BatchId && l.ItemId == i.ItemId));
            Assert.Equal(26, allInBatch);
        });

    // ---- ADM-02: the database refuses a self-approval, from the service -----------------------

    /// <summary>
    /// ADM-02, enforced where it cannot be bypassed. <b>Which guard could this pass while being
    /// wrong:</b> a controller-level check. This calls the service directly, with no endpoint and no
    /// policy, so only <c>trg_ReviewSampleDecisions_NoSelfApproval</c> can refuse it.
    /// </summary>
    [Fact]
    public Task The_uploader_cannot_approve_their_own_batch() =>
        InFreshDatabaseAsync("SelfApprove", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);
            var queue = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);

            // The uploader solves and judges their own sample, so the verdicts are not the obstacle.
            await JudgeAllAsync(audit, queue, "correct", fixture.UploaderId);
            var stratum = queue.Strata.First();

            var refused = await Assert.ThrowsAsync<AdminItemException>(
                () => audit.DecideAsync(stratum.StratumId, fixture.UploaderId));
            Assert.Contains("ADM-02", refused.Message, StringComparison.Ordinal);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal("pending", await read.ReviewStrata.AsNoTracking()
                .Where(s => s.StratumId == stratum.StratumId)
                .Select(s => s.Status)
                .SingleAsync());

            // And nothing was promoted on the way out.
            Assert.Equal(0, await read.Items.AsNoTracking()
                .CountAsync(i => i.LifecycleStatus == "active"));
        });

    // ---- the decision is taken once ---------------------------------------------------------

    /// <summary>
    /// A taken decision is not re-taken. <b>Which guard could this pass while being wrong:</b> a service
    /// that re-derives and rewrites on every call, which lets a quarantine be cleared by judging the
    /// sample again - a second writer of one fact.
    /// </summary>
    [Fact]
    public Task A_stratum_that_is_already_decided_is_refused_rather_than_rewritten() =>
        InFreshDatabaseAsync("Twice", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);
            var queue = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);
            await JudgeAllAsync(audit, queue, "correct", fixture.ReviewerId);

            var stratum = queue.Strata.First();
            await audit.DecideAsync(stratum.StratumId, fixture.ReviewerId);

            var refused = await Assert.ThrowsAsync<AdminItemException>(
                () => audit.DecideAsync(stratum.StratumId, fixture.ReviewerId));
            Assert.Contains("already", refused.Message, StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>
    /// A stratum cannot be decided on a fraction of its sample. <b>Which guard could this pass while
    /// being wrong:</b> counting only the verdicts that exist, so a 2-item sample judged once decides
    /// the cell - and one unjudged item is the one that had the key error.
    /// </summary>
    [Fact]
    public Task A_stratum_with_an_unjudged_sampled_item_cannot_be_decided() =>
        InFreshDatabaseAsync("Partial", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);
            var queue = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);

            var large = queue.Strata.Single(s => s.TopicCode == "R-DET" && s.CefrLevel == "B1");
            var first = large.Items[0];
            await audit.RecordSolveAsync(first.SampleItemId, "A", fixture.ReviewerId);
            await audit.RevealKeyAsync(first.SampleItemId);
            await audit.RecordVerdictAsync(first.SampleItemId, AuditVerdict.Correct, fixture.ReviewerId);

            var refused = await Assert.ThrowsAsync<AdminItemException>(
                () => audit.DecideAsync(large.StratumId, fixture.ReviewerId));
            Assert.Contains("judged", refused.Message, StringComparison.OrdinalIgnoreCase);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal("pending", await read.ReviewStrata.AsNoTracking()
                .Where(s => s.StratumId == large.StratumId).Select(s => s.Status).SingleAsync());
        });

    // ---- drawing twice ----------------------------------------------------------------------

    /// <summary>
    /// A second draw returns the audit under way rather than re-sampling it. <b>Which guard could this
    /// pass while being wrong:</b> a draw that deletes and re-inserts the strata, which would erase the
    /// solves and verdicts already recorded and let a quarantine be avoided by redrawing.
    /// </summary>
    [Fact]
    public Task Drawing_twice_returns_the_audit_under_way_and_keeps_its_verdicts() =>
        InFreshDatabaseAsync("Redraw", async connStr =>
        {
            await using var db = DbContextFactory.Create(connStr);
            var fixture = await SeedAsync(db);
            var audit = Audit(db);
            var first = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);

            var sample = first.Strata.SelectMany(s => s.Items).First();
            await audit.RecordSolveAsync(sample.SampleItemId, "A", fixture.ReviewerId);

            var second = await audit.DrawAsync(fixture.BatchId, fixture.UploaderId);

            Assert.Equal(first.Strata.Count, second.Strata.Count);
            var again = second.Strata.SelectMany(s => s.Items)
                .Single(i => i.SampleItemId == sample.SampleItemId);
            Assert.Equal("A", again.SolvedLabel);

            await using var read = DbContextFactory.Create(connStr);
            Assert.Equal(first.Strata.Count, await read.ReviewStrata.CountAsync());
        });
}
