using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #184: the pool's EF mapping, against the real schema (real triggers, real composite keys).
/// <para>
/// The schema test docs/schema-tests/domain22-pool.sql proves the constraints in T-SQL. These prove
/// the other half: that EF can actually write through them. A table with a trigger and no
/// <c>HasTrigger</c> declaration accepts every T-SQL insert and fails every EF save on the OUTPUT
/// clause - which is the defect this repo has already shipped once, so it is exercised rather than
/// asserted in prose.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolPersistenceTests(DatabaseFixture database)
{
    [Fact]
    public async Task Stimulus_item_and_version_save_with_no_exam_section_or_certificate()
    {
        // A database whose pool tables are empty and whose certificate catalogue is untouched by this
        // test. The point of #184 is that a pool row needs none of the exam-owned shape.
        //
        // The topic is a canonical one rather than a generated code. A synthetic topic accumulates a
        // row per run against the shared fixture database, and since the vocabulary is a closed set of
        // twenty, those rows eventually push the reloading seed over the ceiling and it throws. Using
        // a topic the vocabulary already owns keeps the fixture database at twenty however many times
        // this runs.
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var topic = await db.Topics.FirstOrDefaultAsync(t => t.Modality == "reading" && t.Code == "R-DET");
        if (topic is null)
        {
            // The fixture database starts empty: this test may be the first to want a topic.
            topic = new Topic { Modality = "reading", Code = "R-DET", Name = "Local factual retrieval" };
            db.Topics.Add(topic);
            await db.SaveChangesAsync();
        }

        var stim = new Stimulus
        {
            CefrLevel = CefrLevel.B1,
            Passage = "The depot opens at six.",
            Transcript = "Sign the night ledger.",
            DomainContext = "workplace",
            ImagePath = "/img/depot.jpg",
            ImageAlt = "A notice board beside the depot gate.",
        };
        db.Stimuli.Add(stim);
        await db.SaveChangesAsync();

        var item = new Item
        {
            StimulusId = stim.StimulusId,
            Modality = "reading",
            TopicId = topic.TopicId,
            CefrLevel = CefrLevel.B1,
            ItemType = "passage_comprehension",
            PlacementEligible = true,
            ContentTier = "demo",
            LifecycleStatus = "draft",
            BatchTag = "pool-persistence-a",
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var version = new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = "What must staff who arrive before six do?",
            OptionA = "Sign the night ledger.",
            OptionB = "Open the gate.",
            OptionC = "Call the supervisor.",
            OptionD = "Leave a note.",
            KeyLabel = "A",
            Explanation = "Stated directly.",
            EvidenceStart = 0,
            EvidenceEnd = 33,
            EvidenceUnit = "char",
            EvidenceQuote = "Staff who arrive before then must sign the night ledger.",
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync();

        // Saved through EF despite trg_ItemVersions_ScoredFieldsImmutable being on the table.
        Assert.True(version.ItemVersionId > 0, "the version row must have an identity after SaveChanges");

        var reloaded = await db.ItemVersions
            .AsNoTracking()
            .Include(v => v.Item)
            .SingleAsync(v => v.ItemVersionId == version.ItemVersionId);
        Assert.Equal("A", reloaded.KeyLabel);
        Assert.Equal("Sign the night ledger.", reloaded.OptionA);

        // The level round-trips through its code, not an integer rank.
        Assert.Equal("B1", reloaded.Item.CefrLevel.Code);
        Assert.True(reloaded.Item.CefrLevel == CefrLevel.B1);
    }

    [Fact]
    public async Task In_place_scored_edit_is_refused_by_the_database_not_by_ef()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = DbContextFactory.Create(database.ConnectionString);

        // A canonical topic, not a generated one: see the note in the first test.
        var topic = await db.Topics.FirstOrDefaultAsync(t => t.Modality == "reading" && t.Code == "R-DET");
        if (topic is null)
        {
            // The fixture database starts empty: this test may be the first to want a topic.
            topic = new Topic { Modality = "reading", Code = "R-DET", Name = "Local factual retrieval" };
            db.Topics.Add(topic);
            await db.SaveChangesAsync();
        }

        var item = new Item
        {
            Modality = "reading",
            TopicId = topic.TopicId,
            CefrLevel = CefrLevel.A2,
            ItemType = "sentence_cloze",
            BatchTag = suffix,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var version = new ItemVersion { ItemId = item.ItemId, VersionNumber = 1, Stem = "The original stem." };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync();

        // Change the stem on the tracked entity. EF will happily emit the UPDATE; the trigger refuses
        // it, and the exception must be the trigger's, not EF's optimistic-concurrency noise.
        version.Stem = "A rewritten stem.";
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("scored field is immutable", error.InnerException?.Message ?? error.Message);

        // The row is unchanged, and the approved path - a new version - still works.
        await db.Entry(version).ReloadAsync();
        Assert.Equal("The original stem.", version.Stem);

        db.ItemVersions.Add(new ItemVersion { ItemId = item.ItemId, VersionNumber = 2, Stem = "A rewritten stem." });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.ItemVersions.CountAsync(v => v.ItemId == item.ItemId));
    }
}
