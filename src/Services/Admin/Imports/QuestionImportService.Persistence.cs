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
                exam = await WriteExamAsync(batch.CertId, plan, stored, cancellationToken);
                db.ImportLogs.Add(NewLog(batch.BatchId, null, ImportLogLevel.Info,
                    $"Created draft exam '{exam.Name}' with {plan.QuestionCount} questions in {plan.Groups.Count} groups."));
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

    /// <summary>Saves in stages (exam, groups, questions, options) with explicit keys: the composite
    /// foreign keys that pin a question to its group's exam and section need the generated ids.</summary>
    private async Task<Exam> WriteExamAsync(int certId, ImportPlan plan, IReadOnlyDictionary<string, string> stored, CancellationToken cancellationToken)
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

        var questions = groups.SelectMany(g => g.planned.Questions.Select(question => (question, row: new Question
        {
            GroupId = g.row.GroupId,
            ExamId = exam.ExamId,
            SectionId = g.planned.Section.SectionId,
            SkillId = g.planned.Section.SkillId,
            Stem = question.Source.Stem!.Trim(),
            DifficultyLevel = question.Source.DifficultyLevel!.Value,
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

        return exam;
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
