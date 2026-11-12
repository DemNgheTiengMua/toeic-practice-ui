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
/// #194: gate one against the real pool.
/// <para>
/// The rules are a pure function and are tested exhaustively in <see cref="GateOneTests"/> without a
/// database. What only a database can show is the piece the ticket names as the thing that will bite:
/// <b>key balance is per cell</b>, so the gate needs the keys already stored at the item's
/// (topic, level). These tests prove that context is read from the pool rather than assumed.
/// </para>
/// <para>
/// Every test runs inside a transaction it rolls back, because the cell is (topic, level) and the pool
/// offers only six levels for each of twenty topics. Seeded rows would otherwise accumulate into one
/// shared cell and every later run would judge a distribution it did not author.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolGateOneTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EEGate1_{Guid.NewGuid():N}"[..24];
    private string _connectionString = null!;

    public PoolGateOneTests(DatabaseFixture database) => _database = database;

    /// <summary>
    /// <b>#272: a database this class owns.</b> A gate-one cell is (topic, level), the balance rule reads
    /// the WHOLE cell, and every reading item in the fixture database lands in one of twenty canonical
    /// topics — so a test that seeded ten keys shared its cell with every other class that seeded a
    /// reading item at that level. Measured in a full run: six tests here failed together, each reading a
    /// fixture it did not own, while all six passed in isolation. The rule was right; the fixture was
    /// shared.
    /// </para>
    /// <para>
    /// A private topic would be the smaller fix, and the schema refuses it: a topic must be one of the
    /// canonical twenty, so two tests at the same level cannot have different topics. A private database
    /// is therefore the smallest fix that makes the arithmetic true, and it weakens nothing — the same
    /// keys, the same expected percentage, now actually provable.
    /// </para>
    /// </summary>
    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    /// <summary>
    /// One reading item whose four options are distinct, equally long noun phrases with a rationale
    /// each, so only the rule under test can refuse it. The key is the caller's, because that is what
    /// the balance rule is about.
    /// </summary>
    private static Item SeededItem(int topicId, CefrLevel level, string key, string tag)
    {
        var item = new Item
        {
            Modality = "reading",
            TopicId = topicId,
            CefrLevel = level,
            ItemType = "passage_comprehension",
            ContentTier = "demo",
            LifecycleStatus = "draft",
            BatchTag = tag,
        };

        item.ItemVersions.Add(new ItemVersion
        {
            VersionNumber = 1,
            Stem = "What must every visitor do at the gate?",
            OptionA = "The night ledger.",
            OptionB = "The depot shutter.",
            OptionC = "The depot supervisor.",
            OptionD = "The office door.",
            KeyLabel = key,
            RationaleA = "Correct: the notice names the ledger.",
            RationaleB = "The passage never mentions a shutter.",
            RationaleC = "No supervisor appears in the passage.",
            RationaleD = "A door is not what the notice requires.",
            Explanation = "The notice says every visitor signs the night ledger.",
        });

        return item;
    }

    /// <summary>The canonical reading topic, created only if the fixture database has no topics yet.</summary>
    private static async Task<int> ReadingTopicAsync(CompanionDbContext db)
    {
        var topic = await db.Topics
            .FirstOrDefaultAsync(t => t.Modality == "reading" && t.Code == "R-DET");

        if (topic is not null)
        {
            return topic.TopicId;
        }

        topic = new Topic
        {
            Modality = "reading",
            Code = "R-DET",
            Name = "Local factual retrieval and paraphrase matching",
        };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic.TopicId;
    }

    /// <summary>
    /// The trap the ticket warns about: an item judged alone looks fine. Four A keys in a cell of
    /// eight is 50%, and only the stored cell shows it. A gate that skipped the cell would accept
    /// every item.
    /// </summary>
    [Fact]
    public async Task The_cell_balance_refusal_comes_from_the_keys_already_stored_in_the_cell()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topicId = await ReadingTopicAsync(db);

        // 3/2/2/2 - balanceable, with A one short of the 30% ceiling. Keying the tenth A makes
        // 4/2/2/2 = 40%, and the refusal must name both the letter and the percentage.
        foreach (var key in new[] { "A", "A", "A", "B", "B", "C", "C", "D", "D" })
        {
            db.Items.Add(SeededItem(topicId, CefrLevel.B2, key, "gate1-cell"));
        }

        var subject = SeededItem(topicId, CefrLevel.B2, "A", "gate1-subject");
        db.Items.Add(subject);
        await db.SaveChangesAsync();

        var verdict = await PoolGateOne.ReviewAsync(db, subject.ItemId);

        var refusal = Assert.Single(verdict.Refusals, r => r.Rule == GateOneRules.KeyCellBalance);
        Assert.Contains("A", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("40", refusal.Message, StringComparison.Ordinal);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// An item is judged against the cell <em>without</em> itself. Counting it would let every item
    /// vouch for its own balance, and nine keys at 3/2/2/2 would accept a tenth A on the strength of
    /// the A it is adding.
    /// </summary>
    [Fact]
    public async Task The_item_is_not_counted_in_its_own_cell()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topicId = await ReadingTopicAsync(db);

        var subject = SeededItem(topicId, CefrLevel.C1, "A", "gate1-subject");
        db.Items.Add(subject);
        await db.SaveChangesAsync();

        var verdict = await PoolGateOne.ReviewAsync(db, subject.ItemId);

        // The only key in the cell is the item's own, so the cell it joins is empty and no balance
        // rule can fire. A cell counted with the item in it would be 1/0/0/0 and reported as
        // unbalanceable - the notice a first-item-in-a-cell author must never see.
        Assert.DoesNotContain(verdict.Refusals, r => r.Rule == GateOneRules.KeyCellBalance);
        Assert.DoesNotContain(verdict.Notices, n => n.Rule == GateOneRules.KeyCellBalance);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// The cell is (topic, level), both halves. A neighbouring level's keys must not leak in: the pool
    /// is levelled, and a C2 cell's distribution says nothing about a C1 cell's.
    /// </summary>
    [Fact]
    public async Task A_neighbouring_level_does_not_contribute_to_the_cell()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topicId = await ReadingTopicAsync(db);

        // Eight A keys one level up, every one of which would refuse an A key if the level were ignored.
        for (var i = 0; i < 8; i++)
        {
            db.Items.Add(SeededItem(topicId, CefrLevel.C2, "A", "gate1-neighbour"));
        }

        // A balanceable cell at this level: 3/3/2/2 with one A still available.
        foreach (var key in new[] { "A", "A", "A", "B", "B", "B", "C", "C", "D", "D" })
        {
            db.Items.Add(SeededItem(topicId, CefrLevel.C1, key, "gate1-cell"));
        }

        var subject = SeededItem(topicId, CefrLevel.C1, "A", "gate1-subject");
        db.Items.Add(subject);
        await db.SaveChangesAsync();

        var verdict = await PoolGateOne.ReviewAsync(db, subject.ItemId);

        Assert.DoesNotContain(verdict.Refusals, r => r.Rule == GateOneRules.KeyCellBalance);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// A sibling's earlier version is not a second key: an item keyed A in version 1 and B in version
    /// 2 contributes one B. A loader that counted every version would double the cell and refuse a key
    /// the cell can hold.
    /// </summary>
    [Fact]
    public async Task A_siblings_earlier_version_is_not_a_second_key()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topicId = await ReadingTopicAsync(db);

        // 3/3/2/2 at A1, made by ten items of which one has been re-keyed twice.
        var rekeyed = SeededItem(topicId, CefrLevel.A1, "A", "gate1-rekeyed");
        db.Items.Add(rekeyed);
        await db.SaveChangesAsync();

        rekeyed.ItemVersions.Add(new ItemVersion
        {
            VersionNumber = 2,
            Stem = "What must every visitor do at the gate?",
            OptionA = "The night ledger.",
            OptionB = "The depot shutter.",
            OptionC = "The depot supervisor.",
            OptionD = "The office door.",
            KeyLabel = "C",
            RationaleA = "Correct: the notice names the ledger.",
            RationaleB = "The passage never mentions a shutter.",
            RationaleC = "No supervisor appears in the passage.",
            RationaleD = "A door is not what the notice requires.",
            Explanation = "The notice says every visitor signs the night ledger.",
        });
        await db.SaveChangesAsync();

        foreach (var key in new[] { "A", "A", "B", "B", "B", "C", "D", "D", "D" })
        {
            db.Items.Add(SeededItem(topicId, CefrLevel.A1, key, "gate1-cell"));
        }

        var subject = SeededItem(topicId, CefrLevel.A1, "A", "gate1-subject");
        db.Items.Add(subject);
        await db.SaveChangesAsync();

        var verdict = await PoolGateOne.ReviewAsync(db, subject.ItemId);

        // The cell is 3 + the re-keyed C + 2 = A3/B3/C2/D3 in nine... plus the subject's A = ten at
        // A3/B3/C2/D2, every letter inside [20%, 30%]. Counting the re-keyed item's old A would make
        // A 4 of 11 and refuse it.
        Assert.DoesNotContain(verdict.Refusals, r => r.Rule == GateOneRules.KeyCellBalance);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// The rationales gate one refuses an item for are the ones stored beside the options, so an item
    /// authored through the service is judged on what the database holds rather than on the request.
    /// </summary>
    [Fact]
    public async Task Rationales_written_through_the_service_are_what_the_gate_reads()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topicId = await ReadingTopicAsync(db);

        var created = await new AdminItemService(db, TimeProvider.System).CreateAsync(
            new Contracts.CreatePoolItemRequest(
                Contracts.ModalityDto.Reading, topicId, "B1", "sentence_cloze",
                "What must every visitor do at the gate?",
                OptionA: "The night ledger.",
                OptionB: "The depot shutter.",
                OptionC: "The depot supervisor.",
                OptionD: "The office door.",
                KeyLabel: "A",
                Explanation: "The notice says every visitor signs the night ledger.",
                RationaleA: "Correct: the notice names the ledger.",
                RationaleB: "The passage never mentions a shutter.",
                RationaleC: "No supervisor appears in the passage.",
                RationaleD: "A door is not what the notice requires."));

        Assert.Equal("Correct: the notice names the ledger.", created.RationaleA);
        Assert.Equal("A door is not what the notice requires.", created.RationaleD);

        var verdict = await PoolGateOne.ReviewAsync(db, created.ItemId);

        Assert.True(verdict.Accepted, string.Join(", ", verdict.Refusals.Select(r => r.Rule)));
        Assert.DoesNotContain(verdict.Refusals, r => r.Rule == GateOneRules.OptionRationale);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// The negative half: an item whose version carries no rationale is refused by the gate, and the
    /// message names the option missing one.
    /// <para>
    /// #198: the item is written through the schema rather than through <c>AdminItemService</c>, which
    /// now invokes gate one itself and refuses this record at save time. The gate's own verdict still
    /// has to be demonstrated for a row that reached the table another way - a migration, a direct
    /// import, a version written before the gate had a caller - because #194's contract is that the
    /// gate reads what is stored, not what a service intended.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_item_saved_without_a_rationale_is_refused_by_the_gate()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topicId = await ReadingTopicAsync(db);

        var stored = SeededItem(topicId, CefrLevel.B1, "A", "gate1-rationale");
        stored.ItemVersions.Single().RationaleC = null;
        db.Items.Add(stored);
        await db.SaveChangesAsync();

        var verdict = await PoolGateOne.ReviewAsync(db, stored.ItemId);

        var refusal = Assert.Single(verdict.Refusals, r => r.Rule == GateOneRules.OptionRationale);
        Assert.Contains("C", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("A,", refusal.Message, StringComparison.Ordinal);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// An item re-versioned with a corrected rationale reads the new one: the cell and the gate both
    /// look at the latest version, so a corrected rationale is what a new item is judged on.
    /// </summary>
    [Fact]
    public async Task A_rekeyed_item_is_judged_on_its_latest_version()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topicId = await ReadingTopicAsync(db);

        var created = await new AdminItemService(db, TimeProvider.System).CreateAsync(
            new Contracts.CreatePoolItemRequest(
                Contracts.ModalityDto.Reading, topicId, "B1", "sentence_cloze",
                "What must every visitor do at the gate?",
                OptionA: "The night ledger.",
                OptionB: "The depot shutter.",
                OptionC: "The depot supervisor.",
                OptionD: "The office door.",
                KeyLabel: "A",
                Explanation: "The notice says every visitor signs the night ledger.",
                RationaleA: "Correct: the notice names the ledger.",
                RationaleB: "The passage never mentions a shutter.",
                RationaleC: "No supervisor appears in the passage.",
                RationaleD: "A door is not what the notice requires."));

        var edited = await new AdminItemService(db, TimeProvider.System).UpdateAsync(
            created.ItemId,
            new Contracts.UpdatePoolItemRequest(
                Stem: created.Stem,
                OptionA: created.OptionA,
                OptionB: created.OptionB,
                OptionC: created.OptionC,
                OptionD: created.OptionD,
                KeyLabel: created.KeyLabel,
                Explanation: created.Explanation,
                RationaleA: created.RationaleA,
                RationaleB: created.RationaleB,
                RationaleC: created.RationaleC,
                RationaleD: "Corrected: a door is the entrance, not the requirement."));

        // A rationale is scored content, so correcting one forks a version rather than rewriting the
        // row a past attempt pinned.
        Assert.Equal(2, edited.VersionCount);
        Assert.Equal(2, edited.VersionNumber);

        var verdict = await PoolGateOne.ReviewAsync(db, created.ItemId);

        Assert.True(verdict.Accepted, string.Join(", ", verdict.Refusals.Select(r => r.Rule)));

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// A whole cell's imbalance is reported by the cell, naming the letter outside the band. This is
    /// the half of the rule no item can fix, so it has its own call.
    /// </summary>
    [Fact]
    public async Task An_imbalanced_cell_is_reported_by_the_cell()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topicId = await ReadingTopicAsync(db);

        // 3/3/3/1: D holds 10%, under the 20% floor.
        foreach (var key in new[] { "A", "A", "A", "B", "B", "B", "C", "C", "C", "D" })
        {
            db.Items.Add(SeededItem(topicId, CefrLevel.A2, key, "gate1-cell"));
        }

        await db.SaveChangesAsync();

        var notices = await PoolGateOne.ReviewCellAsync(db, topicId, CefrLevel.A2);

        var notice = Assert.Single(notices);
        Assert.Equal(GateOneRules.KeyCellBalance, notice.Rule);
        Assert.Contains("D", notice.Message, StringComparison.Ordinal);
        Assert.Contains("20", notice.Message, StringComparison.Ordinal);

        await transaction.RollbackAsync();
    }
}
