using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>
/// #198: one seam accepts a file for one skill and returns a result per record.
/// <para>
/// This is D17's missing call site. The four gates are merged and tested as pure functions, and until
/// this class existed no production path called any of them: an author could save an item with three
/// options, no explanation and no rationale and it persisted. Every record here is judged by all four
/// <em>before</em> a single row is added, and an accepted record writes both its item and its version.
/// </para>
/// <para>
/// <b>Judge everything, then write everything.</b> Gating and writing record by record would let a
/// failure at record 247 leave 246 rows and a batch whose status cannot describe it. Judging first is
/// what makes "a rejected record writes nothing" true by the order of operations rather than by a
/// rollback nobody tested.
/// </para>
/// <para>
/// <b>The verdict is per record; the write is per bundle.</b> Gate four's span rule and gate one's key
/// balance are statements about a set - the children of one passage, the keys already in a cell - so a
/// bundle is refused together and reports a line for every child in it. A file of several hundred
/// records therefore loses only the bundles that failed.
/// </para>
/// </summary>
public sealed class PoolIngestionService(
    CompanionDbContext db,
    TimeProvider clock,
    PoolIngestionGates gates,
    ILogger<PoolIngestionService>? log = null)
{
    /// <summary>
    /// The one upload format this seam reads. CNT-03 names <c>UnifiedSkillContentPool</c> 2.0.0, and a
    /// file in any other version is refused by name rather than half-applied.
    /// </summary>
    public const string SupportedSchemaVersion = "2.0.0";

    /// <summary>
    /// Accepts a file for one skill and returns one result per record. A record that is accepted writes
    /// its item and its version; a refused one writes neither. A refused <em>file</em> - a bad version,
    /// the wrong skill - returns one rejected result per record it could read and writes nothing.
    /// <para>
    /// The uploader is not recorded here. <c>ImportBatches.CertId</c> is a required foreign key and pool
    /// stock belongs to no certificate (CNT-01), so an uploader-and-timestamp row for a pool file needs a
    /// batch table of its own; that is #205's staging workbench, not this seam.
    /// </para>
    /// </summary>
    public async Task<SkillContentPoolIngestionResult> IngestAsync(
        string skill,
        Stream content,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(skill);

        var skillCode = skill.Trim().ToLowerInvariant();

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        // ---- the file gates, before any record is judged or written ----
        SkillContentPoolFile file;
        try
        {
            file = SkillContentPoolReader.Read(bytes);
        }
        catch (JsonException exception)
        {
            return RefuseFile(skillCode, null, [], IngestionRules.RecordShape,
                $"the upload is not the ingest format: {exception.Message}");
        }

        if (!string.Equals(file.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            return RefuseFile(
                skillCode,
                file.BatchTag,
                file.RecordLines,
                IngestionRules.SchemaVersion,
                $"schema_version is '{file.SchemaVersion ?? "(absent)"}'; this importer reads "
                + $"UnifiedSkillContentPool {SupportedSchemaVersion} only. Nothing was written: a file in an "
                + "unknown version cannot be read well enough to say which records would have landed.");
        }

        if (!string.Equals(file.Skill, skillCode, StringComparison.Ordinal))
        {
            return RefuseFile(
                skillCode,
                file.BatchTag,
                file.RecordLines,
                IngestionRules.Skill,
                $"the file declares skill '{file.Skill ?? "(absent)"}' but this upload names '{skillCode}' "
                + "(CNT-02: one file per skill). Nothing was written, because a mixed file has no single "
                + "topic vocabulary to be judged against.");
        }

        if (file.ProductiveTaskLines.Count > 0)
        {
            // Not a defect in the file: a productive task needs a locked rubric and gate four's
            // seven-response ladder, and 2.0.0's task object carries no rubric. Naming that is better
            // than writing a task whose level claim was never checked.
            var lines = new List<int>(file.RecordLines);
            lines.AddRange(file.ProductiveTaskLines);
            return RefuseFile(
                skillCode,
                file.BatchTag,
                lines,
                IngestionRules.UnsupportedRecord,
                $"this file carries {file.ProductiveTaskLines.Count} productive task(s). Speaking and writing "
                + "stock enters through the task studio, where gate four's seven-response ladder runs against "
                + "the task's rubric; a 2.0.0 task carries no rubric, so its level and separation cannot be "
                + "judged here. Nothing was written.");
        }

        if (file.Bundles.Count == 0)
        {
            return RefuseFile(
                skillCode,
                file.BatchTag,
                [],
                IngestionRules.RecordShape,
                "the file carries no stimulus_groups, so there is no record to judge.");
        }

        // ---- judge every record, writing nothing yet ----
        var topics = await TopicCatalog.LoadAsync(db, cancellationToken);
        var verdicts = new List<BundleVerdict>(file.Bundles.Count);
        foreach (var bundle in file.Bundles)
        {
            verdicts.Add(await gates.JudgeBundleAsync(skillCode, bundle, topics, cancellationToken));
        }

        // ---- write only the bundles that passed ----
        var items = 0;
        var versions = 0;
        foreach (var verdict in verdicts)
        {
            if (verdict.Accepted)
            {
                var (writtenItems, writtenVersions) = WriteBundle(verdict);
                items += writtenItems;
                versions += writtenVersions;
            }
        }

        // One save for the whole file: an item with no version is a row the pool cannot serve, so the
        // two are written together or not at all, and the items that were judged first are the only
        // ones this save can carry.
        await db.SaveChangesAsync(cancellationToken);

        var records = new List<IngestionRecordResult>();
        foreach (var verdict in verdicts)
        {
            records.AddRange(verdict.Records);
        }

        records.Sort((left, right) => left.Line.CompareTo(right.Line));

        if (log is not null && records.Any(r => !r.Accepted))
        {
            log.LogWarning(
                "Ingestion for {Skill} ({Source}): {Accepted} record(s) accepted, {Rejected} refused, {Items} item(s) written.",
                skillCode, sourceName, records.Count(r => r.Accepted), records.Count(r => !r.Accepted), items);
        }

        return new SkillContentPoolIngestionResult(skillCode, file.BatchTag, records, items, versions);
    }

    /// <summary>
    /// A file that was refused before any record could be judged. Every record the reader saw is
    /// reported rejected under the one rule that refused the file, and nothing is written: an empty
    /// result would read as "a file with no records", which is a different fact.
    /// </summary>
    private static SkillContentPoolIngestionResult RefuseFile(
        string skill,
        string? batchTag,
        IReadOnlyList<int> lines,
        string rule,
        string message)
    {
        var records = new List<IngestionRecordResult>(lines.Count);
        foreach (var line in lines)
        {
            records.Add(new IngestionRecordResult(
                line,
                string.Empty,
                false,
                [new IngestionRefusal(rule, message)]));
        }

        return new SkillContentPoolIngestionResult(skill, batchTag, records, 0, 0);
    }

    /// <summary>
    /// Adds one accepted bundle's rows. Nothing here decides anything: the gates already did. The
    /// stimulus is written once per group and shared by its children, because that is what a passage is
    /// (CNT-12), and both rows of every item go into the same change tracker so the single save either
    /// carries all of them or none.
    /// </summary>
    private (int Items, int Versions) WriteBundle(BundleVerdict verdict)
    {
        var stimulus = verdict.Stimulus!.CarriesContent
            ? PoolImport.BuildStimulus(verdict.Stimulus, verdict.StimulusLevel!.Value, displayOrder: 0)
            : null;

        var written = 0;
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
                clock.GetUtcNow());

            db.Items.Add(item);
            db.ItemVersions.Add(version);
            written++;
        }

        return (written, written);
    }
}
