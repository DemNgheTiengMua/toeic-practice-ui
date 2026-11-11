using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class QuestionImportService
{

    // ---- Writing the exam ----

    /// <summary>Stores the media, then writes the exam and all its rows in one transaction. If either
    /// step fails, the rows roll back and the files already written are deleted, so a failed batch
    /// leaves only its ImportBatches row and log.</summary>
    private async Task<AdminImportResultDto> CommitAsync(ImportBatch batch, ImportPlan plan, ImportPackage package, ImportReport report, CancellationToken cancellationToken)
    {
        var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in plan.MediaPaths)
            {
                using var stream = new MemoryStream(package.Media![path]);
                stored[path] = await fileStore.SaveImportMediaAsync(batch.BatchId, stream, Path.GetExtension(path), cancellationToken);
            }
        }
        catch (IOException ex)
        {
            log?.LogError(ex, "Import batch {BatchId}: storing media failed", batch.BatchId);
            await DeleteStoredAsync(stored.Values);
            report.Error(null, "The media files could not be stored. Nothing was imported; try the upload again.");
            return await FailAsync(batch, report, cancellationToken);
        }

        Exam exam;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                // #189: the legacy shape first. Its rows are what a paper renders, and the
                // pool write below needs their ids to bind the pool's versions back onto the paper.
                (exam, var writtenQuestions) = await WriteExamAsync(batch.CertId!.Value, plan, stored, cancellationToken);
                var pool = await WritePoolAsync(batch, writtenQuestions, exam, stored, cancellationToken);

                db.ImportLogs.Add(NewLog(batch.BatchId, null, ImportLogLevel.Info,
                    $"Created draft exam '{exam.Name}' with {plan.QuestionCount} questions in {plan.Groups.Count} groups."));

                if (pool is not null)
                {
                    db.ImportLogs.Add(NewLog(batch.BatchId, null, ImportLogLevel.Info,
                        $"Wrote {pool.ItemsWritten} pool items and {pool.StimuliWritten} shared stimuli; {pool.Bound} bound to the new paper."));
                    foreach (var note in pool.Notes)
                    {
                        db.ImportLogs.Add(NewLog(batch.BatchId, null, ImportLogLevel.Warn, note));
                    }
                }

                batch.Status = ImportBatchStatus.Done.ToCode();
                batch.CompletedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Validation mirrors every constraint, so reaching here is a bug (ADR-0001): log it,
                // undo everything, and tell the admin plainly.
                log?.LogError(ex, "Import batch {BatchId}: database rejected a validated plan", batch.BatchId);
                await transaction.RollbackAsync(cancellationToken);
                await DeleteStoredAsync(stored.Values);
                report.Error(null, "The database refused the import. Nothing was imported; the error has been logged.");
                return await FailAsync(batch, report, cancellationToken, resetTracker: true);
            }
            catch (PoolImport.RefusedException ex)
            {
                // #266: a question the pool cannot express. Nothing is half-written: the transaction is
                // rolled back, the stored media is removed, and the author reads the sentence naming the
                // question and the reason.
                await transaction.RollbackAsync(cancellationToken);
                await DeleteStoredAsync(stored.Values);
                report.Error(null, ex.Message);
                return await FailAsync(batch, report, cancellationToken, resetTracker: true);
            }
        }

        return new AdminImportResultDto(batch.BatchId, ImportBatchStatusDto.Done, exam.ExamId, exam.Name, plan.QuestionCount, 0, report.WarningCount);
    }

    /// <summary>
    /// #189: the pool half of one group's children. The group is the unit that shares a stimulus, so
    /// one <c>Stimuli</c> row serves every child in it; the children are then bound to the paper at
    /// the positions the legacy rows already hold.
    /// <para>
    /// A question the pool cannot hold - a three-option item, a multi-select, a typed answer - is
    /// written in the legacy shape only and reported once. This is the expand half of the rebuild: a
    /// file that predates the pool still imports exactly as it did, and what cannot become pool stock
    /// is named rather than silently dropped.
    /// </para>
    /// </summary>
    private async Task<PoolImportWriter.Outcome> WritePoolAsync(
        ImportBatch batch,
        IReadOnlyList<WrittenQuestion> questions,
        Exam exam,
        IReadOnlyDictionary<string, string> stored,
        CancellationToken cancellationToken)
    {
        // A stored file resolves to the URL the site serves it at; the store keeps it under imports/.
        // Without this the stimulus named the raw entry path and the file existed but was unreachable.
        string? Url(string? file) => file is null
            ? null
            : MediaUrlPrefix + stored.GetValueOrDefault(NormaliseEntryPath(file) ?? file);
        var topics = await TopicCatalog.LoadAsync(db, cancellationToken);
        if (topics.Canonical("reading").Length == 0)
        {
            // A database with no topic vocabulary cannot hold pool stock. That is a deployment
            // mistake, not a bad file, and the legacy import still stands.
            return new PoolImportWriter.Outcome(0, 0, 0,
                ["The topic vocabulary is empty, so nothing was written to the pool. Load docs/seed-demo.sql."]);
        }

        var written = 0;
        var stimuli = 0;
        var bound = 0;
        var notes = new List<string>();

        foreach (var group in questions.GroupBy(q => q.StimulusKey))
        {
            // One stimulus per group, so its inputs come from the group's own fields. The group key is
            // (section, display order), which is what makes two children of one group share a row.
            var first = group.First();
            var stimulusInput = new PoolImport.StimulusInput(
                PoolImport.Normalise(first.GroupSource.CefrLevel),
                Passage: first.GroupSource.Passage,
                AudioPath: Url(first.GroupSource.AudioFile),
                Transcript: first.GroupSource.Transcript,
                ImagePath: Url(first.GroupSource.ImageFile),
                ImageAlt: first.GroupSource.ImageAlt,
                DomainContext: first.GroupSource.DomainContext,
                PromptCopy: first.GroupSource.PromptCopy,
                PrepSeconds: first.GroupSource.PrepSeconds,
                ResponseSeconds: first.GroupSource.ResponseSeconds);

            var requests = new List<PoolImportWriter.Request>();
            foreach (var question in group)
            {
                var source = question.Source;
                var options = (source.Options ?? [])
                    .Select(o => (o.Label, o.Text, o.IsCorrect, o.Rationale))
                    .ToList();

                if (!PoolImport.TryBuildContent(
                        topics,
                        question.Section.Modality,
                        source.ItemType,
                        hasPassage: !string.IsNullOrWhiteSpace(question.GroupSource.Passage),
                        source.TopicCode,
                        source.CategoryCode,
                        // A child may state its own level (D0.3 allows L_s ± 1); the group's is L_s.
                        source.CefrLevel ?? question.GroupSource.CefrLevel,
                        source.PlacementEligible ?? false,
                        options,
                        source.EvidenceStart,
                        source.EvidenceEnd,
                        source.EvidenceUnit,
                        source.EvidenceQuote,
                        out var content,
                        out var reason,
                        // #250: the legacy QuestionType IS the render format. Passing it keeps a
                        // multi_select a multi_select; without it the item falls back to the modality's
                        // default and every round-trip silently turns checkboxes into radios.
                        renderFormat: source.QuestionType,
                        hint: source.Hint,
                        acceptedAnswersJson: source.AcceptedAnswersJson,
                        requiredKeywords: source.RequiredKeywords is { Count: > 0 } kw
                            ? string.Join(", ", kw)
                            : null,
                        maxSelections: source.MaxSelections,
                        allowNumber: source.AllowNumber ?? true,
                        // #266: a productive task's timers and word window were only ever written to the
                        // legacy row. They travel through ProductiveOutput, which the importer must
                        // supply or a writing task arrives with no floor and no clock.
                        subTask: new PoolImport.ProductiveOutput(
                            source.MinWords,
                            source.MaxWords,
                            source.PrepSeconds,
                            source.ResponseSeconds)))
                {
                    // #266: a question the pool cannot express is a REFUSAL the author must read, never a
                    // row in a second model. The whole batch fails rather than importing "most of" its
                    // questions, because a pack that looks successful and arrives short by an amount only
                    // a log knows is the failure mode this rule exists to prevent.
                    throw new PoolImport.RefusedException(
                        $"Section {question.Section.Code}, question \"{Truncate(source.Stem)}\" cannot be written "
                        + $"to the pool - {reason}. Nothing was imported; fix the pack and re-run it.");
                }

                requests.Add(new PoolImportWriter.Request(
                    QuestionId: requests.Count,
                    question.StimulusKey,
                    content!,
                    source.Stem!,
                    source.Explanation,
                    source.SampleAnswerOrImageDesc,
                    source.ReferenceKind,
                    stimulusInput));
            }

            var outcome = await PoolImportWriter.WriteAsync(
                db, exam, first.Section.SectionId, first.Section.Modality, requests,
                batch.SourceName, clock.GetUtcNow(), cancellationToken);
            written += outcome.ItemsWritten;
            stimuli += outcome.StimuliWritten;
            bound += outcome.Bound;
            notes.AddRange(outcome.Notes);
        }

        return new PoolImportWriter.Outcome(written, stimuli, bound, notes);
    }

    /// <summary>#189 / #266: one written question, tied back to the section and planned question it came
    /// from. A plan is immutable and carries no ids; this is what the pool write needs to bind.</summary>
    private sealed record WrittenQuestion(
        string StimulusKey,
        Section Section,
        ImportManifestGroup GroupSource,
        ImportManifestQuestion Source,
        int GroupKey);

    private static string Truncate(string? stem) =>
        stem is null ? "(no stem)" : stem.Length > 60 ? stem[..57] + "..." : stem;

    /// <summary>
    /// #266: writes the exam and one entry per question, and NOTHING else.
    /// <para>
    /// The previously staged writes - a <c>QuestionGroup</c>, then a <c>Question</c> per item, then its
    /// <c>QuestionOption</c> rows - are gone. The pool write that used to run afterwards is now the only
    /// write, which is why this method returns a plan rather than rows: the pool writer creates the
    /// stimuli, items and versions, and binds them.
    /// </para>
    /// <para>
    /// <b>What still has to be staged.</b> The exam row itself, because the bindings name it and the
    /// batch's result reports its id. Everything else is derived from the plan.
    /// </para>
    /// </summary>
    private async Task<(Exam Exam, IReadOnlyList<WrittenQuestion> Questions)> WriteExamAsync(
        int certId, ImportPlan plan, IReadOnlyDictionary<string, string> stored, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var exam = new Exam
        {
            CertId = certId,
            Name = plan.Exam.Name,
            Status = ExamStatus.Draft.ToCode(),
            Purpose = plan.Exam.Purpose.ToCode(),
            DeliveryMode = plan.Exam.DeliveryMode,
            // #266: an imported paper draws the tier the importer writes. Without this the paper
            // defaulted to production while every imported item is demo, so INV-6 correctly refused
            // every binding and the pack arrived as items on no paper at all.
            ContentTier = ContentTiers.Demo,
            DurationMinutes = plan.Exam.DurationMinutes,
            SourceName = plan.Exam.SourceName,
            Description = plan.Exam.Description,
            QuestionCount = plan.Exam.QuestionCount,
            CreatedAt = now,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync(cancellationToken);

        // A group is the unit that shares a stimulus: children of one group share a key, and two groups
        // in one section are two passages even when their text is identical. The key is the group's
        // ordinal in the plan, which is stable and unique within the paper.
        var written = new List<WrittenQuestion>();
        for (var g = 0; g < plan.Groups.Count; g++)
        {
            var group = plan.Groups[g];
            foreach (var question in group.Questions)
            {
                written.Add(new WrittenQuestion(
                    StimulusKey: $"{group.Section.SectionId}:{g}",
                    group.Section,
                    group.Source,
                    question.Source,
                    GroupKey: g));
            }
        }

        return (exam, written);
    }

    private async Task<AdminImportResultDto> FailAsync(ImportBatch batch, ImportReport report, CancellationToken cancellationToken, bool resetTracker = false)
    {
        if (resetTracker)
        {
            // The rolled-back rows are still tracked as Added; drop them and re-attach the batch.
            db.ChangeTracker.Clear();
            db.ImportBatches.Attach(batch);
        }

        db.ImportLogs.AddRange(report.Entries.Select(e => NewLog(batch.BatchId, e.RowNo, e.Level, e.Message)));
        batch.Status = ImportBatchStatus.Failed.ToCode();
        batch.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new AdminImportResultDto(batch.BatchId, ImportBatchStatusDto.Failed, null, null, 0, report.ErrorCount, report.WarningCount);
    }

    private async Task DeleteStoredAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                await fileStore.DeleteMediaAsync(path);
            }
            catch (IOException ex)
            {
                log?.LogWarning(ex, "Could not remove orphaned import media {Path}", path);
            }
        }
    }

    private static ImportLog NewLog(int batchId, int? rowNo, ImportLogLevel level, string message) => new()
    {
        BatchId = batchId,
        RowNo = rowNo,
        Level = level.ToCode(),
        Message = message.Length > 1000 ? message[..997] + "..." : message,
    };
}
