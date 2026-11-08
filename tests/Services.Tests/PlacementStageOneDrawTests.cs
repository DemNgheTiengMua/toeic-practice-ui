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
/// #214 / D7, PLC-22: the stage-one draw against the real schema and the real pool.
/// <para>
/// The blueprint's arithmetic is proved in <see cref="PlacementStageOneBlueprintTests"/>; what only the
/// database can prove is that the candidates handed to it are the ones a placement may actually use:
/// active, the paper's own tier, and placement-eligible. Each test below therefore seeds the trap
/// beside the control - a quarantined item and a practice-only item next to the one that counts.
/// </para>
/// <para>
/// Every test runs on its own database because each one asserts an exact per-band count, and the shared
/// fixture already holds 60 classes' content.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementStageOneDrawTests(DatabaseFixture database)
{
    private async Task<(string ConnectionString, string DatabaseName)> FreshAsync()
    {
        var name = $"EEStageOne_{Guid.NewGuid():N}"[..26];
        return (await database.CreateFreshDatabaseAsync(name), name);
    }

    /// <summary>
    /// One certificate with listening and reading, plus a writing skill that stage one must ignore.
    /// A certificate's skills are what "each objective skill" means, so the third one is the trap:
    /// a draw that iterated sections instead would pick it up.
    /// </summary>
    private static async Task<int> SeedCertificateAsync(CompanionDbContext db)
    {
        db.Certificates.Add(new Certificate { Code = "S1", Name = "Stage one", IsActive = true });
        await db.SaveChangesAsync();
        var certId = await db.Certificates.Where(c => c.Code == "S1").Select(c => c.CertId).SingleAsync();

        db.Skills.AddRange(
            new Skill { CertId = certId, Code = "LIST", Name = "Listening", Modality = "listening", DisplayOrder = 1 },
            new Skill { CertId = certId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 2 },
            new Skill { CertId = certId, Code = "WRIT", Name = "Writing", Modality = "writing", DisplayOrder = 3 });
        await db.SaveChangesAsync();

        db.Topics.AddRange(
            new Topic { Modality = "listening", Code = "L-GIST", Name = "Listening gist" },
            new Topic { Modality = "reading", Code = "R-GIST", Name = "Reading gist" },
            new Topic { Modality = "writing", Code = "W-ARG", Name = "Writing argument" });
        await db.SaveChangesAsync();

        return certId;
    }

    /// <summary>
    /// Adds one item with one version at a band. The version is not optional: the draw pins the newest
    /// version of every item it serves, so an item with no version is not something a sitting can show.
    /// </summary>
    private static async Task<int> SeedItemAsync(
        CompanionDbContext db,
        string modality,
        string topicCode,
        CefrLevel level,
        string lifecycle = "active",
        bool placementEligible = true,
        string contentTier = "demo",
        int? stimulusId = null)
    {
        var topicId = await db.Topics.Where(t => t.Code == topicCode).Select(t => t.TopicId).SingleAsync();
        var item = new Item
        {
            Modality = modality,
            TopicId = topicId,
            CefrLevel = level,
            ItemType = modality == "listening" ? "short_exchange" : "passage_comprehension",
            // #250: this fixture only authors objective items; a productive one would be a free
            // response and is never built here.
            RenderFormat = "mcq",
            PlacementEligible = placementEligible,
            ContentTier = contentTier,
            LifecycleStatus = lifecycle,
            StimulusId = stimulusId,
        };
        if (modality == "writing")
        {
            item.ItemType = "transactional_correspondence";
            // #250: the override above makes this a productive item, so the widget must follow. A
            // writing item drawn as 'mcq' is exactly what CK_Items_Modality_RenderFormat refuses.
            item.RenderFormat = "free_response";
        }

        db.Items.Add(item);
        await db.SaveChangesAsync();

        db.ItemVersions.Add(new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = $"Item {item.ItemId}",
            OptionA = "A",
            OptionB = "B",
            OptionC = "C",
            OptionD = "D",
            KeyLabel = "A",
        });
        await db.SaveChangesAsync();
        return item.ItemId;
    }

    /// <summary>Fills every band of one skill with the blueprint's own counts, at a distinct topic.</summary>
    private static async Task SeedFullCellAsync(CompanionDbContext db, string modality, string topicCode)
    {
        foreach (var (level, count) in PlacementStageOneBlueprint.Distribution)
        {
            for (var i = 0; i < count; i++)
            {
                await SeedItemAsync(db, modality, topicCode, level);
            }
        }
    }

    [Fact]
    public async Task Each_objective_skill_draws_the_eight_item_distribution_and_the_productive_skill_draws_nothing()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var certId = await SeedCertificateAsync(db);
            await SeedFullCellAsync(db, "listening", "L-GIST");
            await SeedFullCellAsync(db, "reading", "R-GIST");
            // Writing has stock, so "zero" can only come from the blueprint's own rule.
            await SeedItemAsync(db, "writing", "W-ARG", CefrLevel.B1);

            var draw = await new PlacementStageOneDrawService(db).DrawAsync(certId, "demo");

            Assert.Equal(2, draw.Skills.Count);
            Assert.DoesNotContain(draw.Skills, skill => skill.Modality == "writing");
            Assert.True(draw.IsComplete);
            Assert.Equal(16, draw.ItemCount);

            foreach (var skill in draw.Skills)
            {
                Assert.Equal(8, skill.Items.Count);
                // The distribution, band by band. A total of 8 alone would pass a paper that put
                // every item at B1, which is the failure this ticket exists to prevent.
                Assert.Equal(1, skill.Items.Count(item => item.Level == CefrLevel.A1));
                Assert.Equal(1, skill.Items.Count(item => item.Level == CefrLevel.A2));
                Assert.Equal(2, skill.Items.Count(item => item.Level == CefrLevel.B1));
                Assert.Equal(2, skill.Items.Count(item => item.Level == CefrLevel.B2));
                Assert.Equal(1, skill.Items.Count(item => item.Level == CefrLevel.C1));
                Assert.Equal(1, skill.Items.Count(item => item.Level == CefrLevel.C2));
                Assert.Empty(skill.Deficits);
                // Every served item names the version it will render: an item with no version is an
                // item the sitting cannot show.
                Assert.All(skill.Items, item => Assert.NotNull(item.ItemVersionId));
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Criterion 2, at the level the sitting sees it: a band that asks for one item takes one child of
    /// a three-child stimulus. The input that passes while being wrong is exactly this pool - a
    /// selector that treats a stimulus as its unit either returns nothing for A1 or serves all three.
    /// </summary>
    [Fact]
    public async Task A_one_item_band_is_served_from_one_child_of_a_three_child_bundle()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var certId = await SeedCertificateAsync(db);

            db.Stimuli.Add(new Stimulus
            {
                CefrLevel = CefrLevel.A1,
                Passage = "A shared passage.",
                DomainContext = "general",
            });
            await db.SaveChangesAsync();
            var stimulusId = await db.Stimuli.Select(s => s.StimulusId).SingleAsync();

            for (var child = 0; child < 3; child++)
            {
                await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A1, stimulusId: stimulusId);
            }

            var draw = await new PlacementStageOneDrawService(db).DrawAsync(certId, "demo");
            // Reading is objective too, so it has a plan of its own - with nothing to serve, because
            // the bundle above is listening stock.
            Assert.Equal(2, draw.Skills.Count);
            var listening = Assert.Single(draw.Skills, skill => skill.Modality == "listening");
            Assert.Empty(Assert.Single(draw.Skills, skill => skill.Modality == "reading").Items);

            var a1 = Assert.Single(listening.Items, item => item.Level == CefrLevel.A1);
            Assert.Equal(stimulusId, a1.StimulusId);
            // The other five bands are empty, and the deficit says so rather than inventing stock.
            Assert.Equal(5, listening.Deficits.Count);
            Assert.Single(listening.Items);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// PLC-26 twice over. The trap is an over-full neighbour: A2 holds ten items, so a selector that
    /// borrowed across bands would fill C2 and report a complete form. The deficit must name C2, and no
    /// A2 item may appear at a band it was not authored for.
    /// </summary>
    [Fact]
    public async Task A_band_the_pool_cannot_supply_is_named_and_never_filled_from_a_neighbour()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var certId = await SeedCertificateAsync(db);
            foreach (var (level, count) in PlacementStageOneBlueprint.Distribution.Where(band => band.Level != CefrLevel.C2))
            {
                for (var i = 0; i < count; i++)
                {
                    await SeedItemAsync(db, "listening", "L-GIST", level);
                }
            }

            for (var i = 0; i < 10; i++)
            {
                await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A2);
            }

            var draw = await new PlacementStageOneDrawService(db).DrawAsync(certId, "demo");
            var listening = Assert.Single(draw.Skills, skill => skill.Modality == "listening");

            var deficit = Assert.Single(listening.Deficits);
            Assert.Equal(CefrLevel.C2, deficit.Level);
            Assert.Equal(1, deficit.Missing);
            Assert.Equal(7, listening.Items.Count);
            Assert.False(draw.IsComplete);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// CNT-24 / INV-6 / CNT-42: only active, placement-eligible stock of the paper's own tier may be
    /// drawn. The trap is a full cell built from items that are each wrong in exactly one way - one
    /// quarantined, one practice-only, one the other tier - so a query that dropped any single filter
    /// would still report a complete draw.
    /// </summary>
    [Fact]
    public async Task Quarantined_practice_only_and_other_tier_stock_is_never_drawn()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var certId = await SeedCertificateAsync(db);

            await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A1, lifecycle: "quarantined");
            await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A1, placementEligible: false);
            await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A1, contentTier: "production");

            var draw = await new PlacementStageOneDrawService(db).DrawAsync(certId, "demo");
            var listening = Assert.Single(draw.Skills, skill => skill.Modality == "listening");

            Assert.Empty(listening.Items);
            Assert.Equal(6, listening.Deficits.Count);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// A paper draws its own tier and nothing else: the same cell, asked for twice at two tiers, must
    /// answer from the tier it was asked about. This is the half the test above cannot see, because
    /// it only ever asked for <c>demo</c>.
    /// </summary>
    [Fact]
    public async Task The_draw_follows_the_tier_the_paper_was_asked_about()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var certId = await SeedCertificateAsync(db);
            await SeedFullCellAsync(db, "listening", "L-GIST");
            await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A1, contentTier: "production");

            var production = await new PlacementStageOneDrawService(db).DrawAsync(certId, "production");
            var productionListening = Assert.Single(production.Skills, skill => skill.Modality == "listening");
            var only = Assert.Single(productionListening.Items);
            Assert.Equal(CefrLevel.A1, only.Level);
            Assert.Equal(5, productionListening.Deficits.Count);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }
}
