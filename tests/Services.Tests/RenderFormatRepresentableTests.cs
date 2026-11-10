using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 criterion 3: <b>all seven render formats are representable and render identically.</b>
/// <para>
/// <b>Why the seven are the requirement.</b> The pool carries TWO axes and they are not
/// interchangeable: <c>ItemType</c> is the semantic type (fourteen of them, "what is asked") and
/// <c>RenderFormat</c> is the widget (seven, "how it is drawn"). A <c>passage_comprehension</c> can be a
/// radio group, a checkbox list, a select or a drag target, and the seed uses six of the seven. Collapsing
/// the two would make the paper unrepresentable, which is the defect #231 left behind.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> Asserting "the create call returned a DTO"
/// passes on a service that ignores <c>RenderFormat</c> entirely and always writes the modality default.
/// So each case reads the STORED <c>Items.RenderFormat</c> back, and the eight values are driven through
/// the real authoring path rather than inserted with EF - a fixture insert proves the column exists,
/// not that the product can fill it.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RenderFormatRepresentableTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>The seven the schema's CHECK admits. Written out here, independently of the type.</summary>
    public static TheoryData<string, string> Formats => new()
    {
        // render format, item type - reading carries the objective widgets, writing/speaking the free response
        { "mcq", "passage_comprehension" },
        { "mcq_3", "passage_comprehension" },
        { "multi_select", "passage_comprehension" },
        { "inline_completion", "sentence_cloze" },
        { "dropdown", "passage_comprehension" },
        { "drag_drop", "passage_comprehension" },
        { "free_response", "persuasive_essay" },
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task Every_render_format_is_stored_on_the_item(string renderFormat, string itemType)
    {
        var name = $"EE250r_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);

            var governed = await SeedAsync(connectionString, renderFormat, itemType);
            var service = new AdminItemService(db, TimeProvider.System);

            var created = await service.CreateAsync(governed.Request);

            await using var read = DbContextFactory.Create(connectionString);
            var stored = await read.Items.AsNoTracking().SingleAsync(i => i.ItemId == created.ItemId);

            // The two axes are separate facts, and both are read back: a service that wrote the
            // render format into ItemType (or the reverse) would satisfy neither assertion.
            Assert.Equal(renderFormat, stored.RenderFormat);
            Assert.Equal(itemType, stored.ItemType);
            Assert.Equal(governed.Modality, stored.Modality);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Omitting the format falls back to the modality, and the fallback is the only value a productive
    /// item can legally take: an author who names nothing for a writing task gets <c>free_response</c>,
    /// never a radio group. The control case for the theory above.
    /// </summary>
    [Fact]
    public async Task An_unnamed_format_falls_back_to_the_modality_default()
    {
        var name = $"EE250f_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var governed = await SeedAsync(connectionString, renderFormat: null, itemType: "persuasive_essay");

            var created = await new AdminItemService(db, TimeProvider.System).CreateAsync(governed.Request);

            await using var read = DbContextFactory.Create(connectionString);
            Assert.Equal("free_response",
                await read.Items.Where(i => i.ItemId == created.ItemId).Select(i => i.RenderFormat).SingleAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// A format outside the seven is refused by the database, not silently stored. The CHECK is the
    /// arbiter; this proves the authoring path cannot route around it.
    /// </summary>
    [Fact]
    public async Task A_format_outside_the_seven_is_refused()
    {
        var name = $"EE250x_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var governed = await SeedAsync(connectionString, renderFormat: "checkbox_grid", itemType: "passage_comprehension");

            await Assert.ThrowsAnyAsync<Exception>(() =>
                new AdminItemService(db, TimeProvider.System).CreateAsync(governed.Request));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ---------------------------------------------------------------------------

    private sealed record Governed(CreatePoolItemRequest Request, string Modality);

    /// <summary>
    /// A topic and a free-response stimulus where the format needs one, so the only variable across the
    /// eight cases is the render format itself.
    /// </summary>
    private static async Task<Governed> SeedAsync(string connectionString, string? renderFormat, string itemType)
    {
        var modality = itemType is "persuasive_essay" or "transactional_correspondence"
            or "analytical_evaluation_essay" or "visual_data_synthesis"
            ? "writing"
            : "reading";

        await using var db = DbContextFactory.Create(connectionString);

        // The canonical twenty are the closed vocabulary the trigger enforces; pick a real one per skill.
        var (topicCode, topicName) = modality == "writing"
            ? ("W-ARG", "Thesis-driven persuasive and opinion argumentation")
            : itemType == "sentence_cloze"
                ? ("R-GRAM", "Morphology, tense, agreement and the closed word class")
                : ("R-DET", "Local factual retrieval and paraphrase matching");

        var topic = new Topic { Modality = modality, Code = topicCode, Name = topicName };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        // Every item carries a non-empty explanation (gate one) and a rationale per option where it has
        // options (#194), or gate one refuses it and the format under test never reaches the row.
        var isProductive = modality == "writing";
        var request = new CreatePoolItemRequest(
            Modality: modality == "writing" ? ModalityDto.Writing : ModalityDto.Reading,
            TopicId: topic.TopicId,
            CefrLevelCode: "B1",
            ItemType: itemType,
            Stem: $"Stem for {renderFormat ?? "default"} [{Unique("k")}]",
            OptionA: isProductive ? null : "The night ledger.",
            OptionB: isProductive ? null : "The depot shutter.",
            OptionC: isProductive ? null : "The depot supervisor.",
            OptionD: isProductive ? null : "The office doorway.",
            KeyLabel: isProductive ? null : "A",
            Explanation: isProductive
                ? "The task asks for a reasoned position."
                : "The first sentence names the ledger.",
            PlacementEligible: false,
            RationaleA: isProductive ? null : "Correct: the notice names the ledger.",
            RationaleB: isProductive ? null : "The passage never mentions a shutter.",
            RationaleC: isProductive ? null : "No supervisor appears in the passage.",
            RationaleD: isProductive ? null : "A door is not what the notice requires.",
            RenderFormat: renderFormat);

        return new Governed(request, modality);
    }
}
