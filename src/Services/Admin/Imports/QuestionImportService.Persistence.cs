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
                // #189: the legacy shape first. Its rows are what a paper renders until #231, and the
                // pool write below needs their ids to bind the pool's versions back onto the paper.
                (exam, var writtenQuestions) = await WriteExamAsync(batch.CertId, plan, stored, cancellationToken);
                var pool = await WritePoolAsync(batch, writtenQuestions, exam, cancellationToken);

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
        CancellationToken cancellationToken)
    {
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
                Passage: first.GroupRow.Passage,
                AudioPath: first.GroupRow.AudioPath,
                Transcript: first.GroupRow.Transcript,
                ImagePath: first.GroupRow.ImagePath,
                ImageAlt: first.GroupRow.ImageAlt,
                DomainContext: first.GroupSource.DomainContext,
                PromptCopy: first.GroupSource.PromptCopy,
                PrepSeconds: first.GroupRow.PrepSeconds,
                ResponseSeconds: first.GroupRow.ResponseSeconds);

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
                        out var reason))
                {
                    notes.Add(
                        $"Section {question.Section.Code}, group {question.GroupSource.DisplayOrder}, question \"{Truncate(source.Stem)}\": " +
                        $"not written to the pool - {reason}");
                    continue;
                }

                requests.Add(new PoolImportWriter.Request(
                    question.QuestionId,
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

    /// <summary>#189: one written legacy question, tied back to the group and planned question it came
    /// from. A plan is immutable and carries no ids; this is what the pool write needs to bind.</summary>
    private sealed record WrittenQuestion(
        int QuestionId,
        string StimulusKey,
        Section Section,
        ImportManifestGroup GroupSource,
        QuestionGroup GroupRow,
        ImportManifestQuestion Source,
        Question Row);

    private static string Truncate(string? stem) =>
        stem is null ? "(no stem)" : stem.Length > 60 ? stem[..57] + "..." : stem;

    /// <summary>Saves in stages (exam, groups, questions, options) with explicit keys: the composite
    /// foreign keys that pin a question to its group's exam and section need the generated ids.
    /// Returns each question's row and the group it came from, because the pool write binds by
    /// position and a position is only meaningful against rows that now exist.</summary>
    private async Task<(Exam Exam, IReadOnlyList<WrittenQuestion> Questions)> WriteExamAsync(
        int certId, ImportPlan plan, IReadOnlyDictionary<string, string> stored, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        string? Url(string? file) => file is null ? null : MediaUrlPrefix + stored[NormaliseEntryPath(file)!];

        var exam = new Exam
        {
            CertId = certId,
            Name = plan.Exam.Name,
            Status = ExamStatus.Draft.ToCode(),
            Purpose = plan.Exam.Purpose.ToCode(),
            DeliveryMode = plan.Exam.DeliveryMode,
            DurationMinutes = plan.Exam.DurationMinutes,
            SourceName = plan.Exam.SourceName,
            Description = plan.Exam.Description,
            QuestionCount = plan.Exam.QuestionCount,
            CreatedAt = now,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync(cancellationToken);

        var groups = plan.Groups.Select(planned => (planned, row: new QuestionGroup
        {
            ExamId = exam.ExamId,
            CertId = certId,
            SectionId = planned.Section.SectionId,
            UnitNumber = planned.Source.UnitNumber ?? 1,
            Passage = Normalise(planned.Source.Passage),
            AudioPath = Url(planned.Source.AudioFile),
            AudioStartMs = planned.Source.AudioStartMs,
            AudioEndMs = planned.Source.AudioEndMs,
            ImagePath = Url(planned.Source.ImageFile),
            ImageAlt = planned.Source.ImageFile is null ? null : Normalise(planned.Source.ImageAlt),
            Transcript = Normalise(planned.Source.Transcript),
            PrepSeconds = planned.Source.PrepSeconds,
            ResponseSeconds = planned.Source.ResponseSeconds,
            DisplayOrder = planned.DisplayOrder,
        })).ToList();
        db.QuestionGroups.AddRange(groups.Select(g => g.row));
        await db.SaveChangesAsync(cancellationToken);

        var questions = groups.SelectMany(g => g.planned.Questions.Select(question => (question, planned: g.planned, row: new Question
        {
            GroupId = g.row.GroupId,
            ExamId = exam.ExamId,
            SectionId = g.planned.Section.SectionId,
            SkillId = g.planned.Section.SkillId,
            Stem = question.Source.Stem!.Trim(),
            // #229 / D1: the level the file was validated on, not a second parse of the same string.
            // The difficulty is left NULL: it is dormant until #231 deletes the column.
            CefrLevel = question.Level,
            QuestionType = question.Type.ToCode(),
            Explanation = Normalise(question.Source.Explanation),
            CategoryId = question.CategoryId,
            PrepSeconds = question.Source.PrepSeconds,
            ResponseSeconds = question.Source.ResponseSeconds,
            MinWords = question.Source.MinWords,
            MaxWords = question.Source.MaxWords,
            MaxSelections = question.Source.MaxSelections,
            AllowNumber = question.Source.AllowNumber ?? true,
            AcceptedAnswersJson = question.AcceptedAnswersJson,
            RequiredKeywords = question.RequiredKeywords,
            Hint = Normalise(question.Source.Hint),
            SampleAnswerOrImageDesc = Normalise(question.Source.SampleAnswerOrImageDesc),
            ReferenceKind = Normalise(question.Source.ReferenceKind),
            DisplayOrder = question.DisplayOrder,
            UpdatedAt = now,
        }))).ToList();
        db.Questions.AddRange(questions.Select(q => q.row));
        await db.SaveChangesAsync(cancellationToken);

        // Options for all non-free-response questions
        db.QuestionOptions.AddRange(questions
            .Where(q => q.question.Type != QuestionType.FreeResponse && q.question.Source.Options != null)
            .SelectMany(q => q.question.Source.Options!.Select(o => new QuestionOption
            {
                QuestionId = q.row.QuestionId,
                QuestionType = q.row.QuestionType,
                Label = o.Label!.Trim().ToUpperInvariant(),
                Text = o.Text!.Trim(),
                IsCorrect = o.IsCorrect,
                Rationale = Normalise(o.Rationale),
            })));
        await db.SaveChangesAsync(cancellationToken);

        var written = questions
            .Select(q => new WrittenQuestion(
                q.row.QuestionId,
                // A group is the unit that shares a stimulus: children of one group share a key, and
                // two groups in one section are two passages even when their text is identical.
                StimulusKey: $"{q.row.SectionId}:{q.row.GroupId}",
                q.planned.Section,
                q.planned.Source,
                groups.First(g => g.planned == q.planned).row,
                q.question.Source,
                q.row))
            .ToList();

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
