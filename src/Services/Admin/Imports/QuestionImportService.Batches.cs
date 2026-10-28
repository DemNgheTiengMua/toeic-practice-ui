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

    // ---- Reading batches back ----

    public async Task<AdminImportBatchesDto> GetBatchesAsync(int? certId, CancellationToken cancellationToken = default)
    {
        var query = db.ImportBatches.AsNoTracking();
        if (certId is { } id)
        {
            query = query.Where(b => b.CertId == id);
        }

        var batches = await ProjectBatches(query.OrderByDescending(b => b.BatchId).Take(50)).ToListAsync(cancellationToken);
        var certificates = await db.Certificates.AsNoTracking().OrderBy(c => c.CertId)
            .Select(c => new AdminCertificateOptionDto(c.CertId, c.Code, c.Name)).ToListAsync(cancellationToken);
        return new AdminImportBatchesDto(batches.Select(ToDto).ToList(), certificates);
    }

    public async Task<AdminImportBatchDetailDto> GetBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var batch = await ProjectBatches(db.ImportBatches.AsNoTracking().Where(b => b.BatchId == batchId)).SingleOrDefaultAsync(cancellationToken)
            ?? throw new AdminExamException(ApiError.Codes.ImportBatchNotFound, $"Import batch {batchId} not found.", 404);

        var logs = await db.ImportLogs.AsNoTracking().Where(l => l.BatchId == batchId).OrderBy(l => l.LogId).ToListAsync(cancellationToken);
        return new AdminImportBatchDetailDto(
            ToDto(batch),
            logs.Select(l => new AdminImportLogDto(l.LogId, l.RowNo, EnumCodes.Parse<ImportLogLevel>(l.Level).MapTo<ImportLogLevelDto>(), l.Message, l.LoggedAt)).ToList());
    }

    private static IQueryable<BatchRow> ProjectBatches(IQueryable<ImportBatch> query)
    {
        var error = ImportLogLevel.Error.ToCode();
        var warn = ImportLogLevel.Warn.ToCode();
        return query.Select(b => new BatchRow(
            b,
            b.Cert.Code,
            b.UploadedByNavigation.Email,
            b.ImportLogs.Count(l => l.Level == error),
            b.ImportLogs.Count(l => l.Level == warn)));
    }

    private static AdminImportBatchListItemDto ToDto(BatchRow row) => new(
        row.Batch.BatchId,
        row.Batch.CertId,
        row.CertCode,
        row.Batch.SourceName,
        EnumCodes.Parse<ImportBatchStatus>(row.Batch.Status).MapTo<ImportBatchStatusDto>(),
        row.Email,
        row.Batch.CreatedAt,
        row.Batch.CompletedAt,
        row.Errors,
        row.Warnings);
}
