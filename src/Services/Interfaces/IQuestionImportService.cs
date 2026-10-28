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

public interface IQuestionImportService
{
    /// <summary>Validates one upload (.json manifest or .zip package, ADR-0007) as a whole and, only if
    /// nothing is wrong, creates one draft exam from it. Every call that names a real certificate
    /// leaves an ImportBatches row and its ImportLog lines, whatever the outcome.</summary>
    Task<AdminImportResultDto> ImportAsync(int adminId, int certId, string sourceName, Stream content, CancellationToken cancellationToken = default);

    Task<AdminImportResultDto> ImportManifestAsync(int adminId, int certId, string manifestJson, CancellationToken cancellationToken = default);

    Task<AdminImportResultDto> ImportPackageZipAsync(int adminId, int certId, Stream zipStream, string fileName = "package.zip", CancellationToken cancellationToken = default);

    Task<AdminImportBatchesDto> GetBatchesAsync(int? certId, CancellationToken cancellationToken = default);

    Task<AdminImportBatchDetailDto> GetBatchAsync(int batchId, CancellationToken cancellationToken = default);

    Task<(Stream Stream, string ContentType, string FileName)> GenerateManifestTemplateAsync(int certId, CancellationToken cancellationToken = default);

    Task<(Stream Stream, string ContentType, string FileName)> ExportExamPackageAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default);
}
