using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;

namespace Services;

/// <summary>
/// #198/#203: "these judged children become pool stock".
/// <para>
/// Extracted from <see cref="PoolIngestionService"/> the moment a second caller needed it: the studio
/// (#203) writes a bundle from the same verdict the upload seam produces, and a second copy of this
/// loop would be a second answer to "what does an accepted bundle write". Nothing here decides
/// anything - the gates already did - so both doors stay one writer.
/// </para>
/// <para>
/// The stimulus is written once per group and shared by its children, because that is what a passage
/// is (CNT-12). Both rows of every item go into the same change tracker, so the caller's single save
/// either carries all of them or none.
/// </para>
/// </summary>
internal static class PoolIngestionWriter
{
    /// <summary>The rows one accepted bundle wrote, so a caller can answer with their ids.</summary>
    internal sealed record Written(
        IReadOnlyList<Item> Items,
        IReadOnlyList<ItemVersion> Versions);

    internal static Written WriteBundle(
        CompanionDbContext db,
        BundleVerdict verdict,
        DateTimeOffset now,
        string lifecycleStatus = PoolImport.LifecycleStatus)
    {
        var stimulus = verdict.Stimulus!.CarriesContent
            ? PoolImport.BuildStimulus(verdict.Stimulus, verdict.StimulusLevel!.Value, displayOrder: 0)
            : null;

        var items = new List<Item>(verdict.Children.Count);
        var versions = new List<ItemVersion>(verdict.Children.Count);
        foreach (var child in verdict.Children)
        {
            var (item, version) = PoolImport.BuildItem(
                child.TopicId,
                child.Content,
                child.Stem,
                child.Explanation,
                referenceText: null,
                referenceKind: null,
                stimulus,
                verdict.Bundle.Group.BatchTag,
                now,
                lifecycleStatus);

            db.Items.Add(item);
            db.ItemVersions.Add(version);
            items.Add(item);
            versions.Add(version);
        }

        return new Written(items, versions);
    }
}
