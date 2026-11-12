using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #152 finding 5: a spoken task's reference (<c>ReferenceText</c> + <c>ReferenceKind</c>) must be
/// authorable by the product, not only by SQL. A column nothing can fill is dead weight, and every
/// Read Aloud task stayed unscripted as a result.
/// <para>
/// <b>#269: the store moved, the rule did not.</b> This used to drive
/// <c>AdminExamService.CreateQuestionAsync</c>/<c>UpdateQuestionAsync</c> and read
/// <c>Questions.SampleAnswerOrImageDesc</c>. The legacy question is gone; the pair now lives on
/// <see cref="ItemVersion.ReferenceText"/>/<see cref="ItemVersion.ReferenceKind"/>, and
/// <c>IAdminItemService.CreateAsync</c>/<c>UpdateAsync</c> is its authoring door. It is still the path
/// the studio, the API and the import manifest take, so "the admin can fill it" is still asserted
/// against the code an admin actually runs.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SpeakingReferenceAuthoringTests(DatabaseFixture database)
{
    private static CompanionDbContext Db(string connectionString) => DbContextFactory.Create(connectionString);

    private AdminItemService CreateService() =>
        new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    /// <summary>A speaking topic, provisioned because the vocabulary is a closed set of twenty.</summary>
    private static async Task<int> SpeakingTopicIdAsync(string connectionString)
    {
        await using var db = Db(connectionString);
        var existing = await db.Topics.Where(t => t.Modality == "speaking" && t.Code == "S-DESC")
            .Select(t => (int?)t.TopicId).FirstOrDefaultAsync();
        if (existing is not null)
        {
            return existing.Value;
        }

        var topic = new Topic { Modality = "speaking", Code = "S-DESC", Name = "Describing" };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic.TopicId;
    }

    /// <summary>A Read Aloud task with the reference pair, on the pool.</summary>
    private static CreatePoolItemRequest ReadAloudItem(
        int topicId, string stem, string? referenceText, ReferenceKindDto? referenceKind) =>
        new(
            ModalityDto.Speaking,
            topicId,
            "B1",
            "descriptive_monologue",
            stem,
            // Gate one (CNT-35) refuses an item with no explanation, whatever its reference is.
            Explanation: "A passing answer reads the announcement clearly and completely.",
            ReferenceText: referenceText,
            ReferenceKind: referenceKind);

    [Fact]
    public async Task An_admin_can_author_a_read_aloud_script_and_read_it_back()
    {
        var topicId = await SpeakingTopicIdAsync(database.ConnectionString);

        var created = await CreateService().CreateAsync(ReadAloudItem(
            topicId,
            "Read the announcement aloud as clearly as you can.",
            "Attention all shoppers. The sale ends today at 9:00 PM.",
            ReferenceKindDto.Script));

        Assert.Equal("Attention all shoppers. The sale ends today at 9:00 PM.", created.ReferenceText);
        Assert.Equal(ReferenceKindDto.Script, created.ReferenceKind);

        // And it is really in the column, not only on the DTO: the version is what a sitting renders,
        // and a reference a sitting cannot read is the defect this ticket is about.
        await using var db = Db(database.ConnectionString);
        var stored = await db.ItemVersions.AsNoTracking().SingleAsync(v => v.ItemVersionId == created.ItemVersionId);
        Assert.Equal("Attention all shoppers. The sale ends today at 9:00 PM.", stored.ReferenceText);
        Assert.Equal("script", stored.ReferenceKind);

        // An edit can replace it, and clear it. Both are scored fields, so each forks a version
        // (CNT-43) rather than rewriting the one a past attempt pinned.
        var updated = await CreateService().UpdateAsync(created.ItemId, new UpdatePoolItemRequest(
            Stem: created.Stem,
            Explanation: created.Explanation,
            ReferenceText: "Updated announcement text.",
            ReferenceKind: ReferenceKindDto.ImageDesc));
        Assert.Equal("Updated announcement text.", updated.ReferenceText);
        Assert.Equal(ReferenceKindDto.ImageDesc, updated.ReferenceKind);
        Assert.Equal(2, updated.VersionNumber);

        var cleared = await CreateService().UpdateAsync(created.ItemId, new UpdatePoolItemRequest(
            Stem: created.Stem,
            Explanation: created.Explanation));
        Assert.Null(cleared.ReferenceText);
        Assert.Null(cleared.ReferenceKind);
        Assert.Equal(3, cleared.VersionNumber);
    }

    /// <summary>
    /// The other half of finding 4's fix: a reference is only usable when the author also says what it
    /// is. Text with no kind cannot be classified, and guessing is what produced the original bug. The
    /// pool states the same rule in <c>AdminItemService.ValidateVersionShape</c> and the schema states it
    /// again in <c>CK_ItemVersions_ReferencePair</c>, so this is two guards rather than one.
    /// </summary>
    [Fact]
    public async Task A_reference_needs_both_the_text_and_what_it_is()
    {
        var topicId = await SpeakingTopicIdAsync(database.ConnectionString);

        var textOnly = await Assert.ThrowsAsync<AdminItemException>(() => CreateService().CreateAsync(
            ReadAloudItem(topicId, "Read aloud.", "Some words.", referenceKind: null)));
        Assert.Equal(ApiError.Codes.Validation, textOnly.Code);
        Assert.Contains("both", textOnly.Message, StringComparison.OrdinalIgnoreCase);

        var kindOnly = await Assert.ThrowsAsync<AdminItemException>(() => CreateService().CreateAsync(
            ReadAloudItem(topicId, "Read aloud.", referenceText: null, referenceKind: ReferenceKindDto.Script)));
        Assert.Equal(ApiError.Codes.Validation, kindOnly.Code);
        Assert.Contains("both", kindOnly.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The import manifest can carry the pair, which is how an existing exam gets its Read Aloud scripts
    /// without anyone opening the studio. A manifest that could not express the columns would leave every
    /// imported Read Aloud task unscripted, exactly as they were before this fix.
    /// <para>
    /// #269: the writer list changed with the store. <c>AdminExamService.Questions.cs</c> no longer
    /// writes a version, and the two importers plus <c>AdminItemService</c> do - which is the set below.
    /// </para>
    /// </summary>
    [Fact]
    public void The_import_manifest_can_express_a_script()
    {
        var root = DatabaseFixture.RepoRootPath;

        var manifest = File.ReadAllText(Path.Combine(root, "src", "Services", "Admin", "Imports", "Manifest", "ImportManifestQuestion.cs"));
        Assert.Contains("SampleAnswerOrImageDesc", manifest);
        Assert.Contains("ReferenceKind", manifest);

        // Every writer of a scored version carries the pair, so an import cannot silently drop it. The
        // two importers read a manifest's SampleAnswerOrImageDesc and write the version's ReferenceText;
        // AdminItemService is the authoring door that writes the same two columns.
        var importers = new[]
        {
            Path.Combine("src", "Services", "Admin", "Imports", "QuestionImportService.Persistence.cs"),
            Path.Combine("src", "Services", "Admin", "Imports", "ExamQuickIngestService.Commit.cs"),
        };

        foreach (var relative in importers)
        {
            var source = File.ReadAllText(Path.Combine(root, relative));
            Assert.Contains("SampleAnswerOrImageDesc", source);
            Assert.Contains("ReferenceKind", source);
        }

        var poolAuthor = File.ReadAllText(
            Path.Combine(root, "src", "Services", "Admin", "Items", "AdminItemService.cs"));
        Assert.Contains("ReferenceText", poolAuthor);
        Assert.Contains("ReferenceKind", poolAuthor);

        // The plain-text importer understands a SCRIPT: line, which is how a bulk author writes one.
        var parser = File.ReadAllText(Path.Combine(root, "src", "Services", "ExamQuickIngestService.cs"));
        Assert.Contains("ScriptRegex", parser);
        Assert.Contains("IMAGE_DESC", parser);
    }
}
