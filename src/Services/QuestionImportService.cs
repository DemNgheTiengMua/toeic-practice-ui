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

public sealed partial class QuestionImportService(
    CompanionDbContext db,
    IFileStore fileStore,
    TimeProvider clock,
    ILogger<QuestionImportService>? log = null) : IQuestionImportService
{
    public const long MaxUploadBytes = 100L * 1024 * 1024;
    private const long MaxManifestBytes = 5L * 1024 * 1024;
    private const long MaxMediaFileBytes = 60L * 1024 * 1024;
    private const long MaxUnpackedBytes = 200L * 1024 * 1024;
    private const int MaxPackageEntries = 500;

    /// <summary>Imported media is stored under imports/ and referenced from the question rows by the
    /// URL the MVC site serves it at (it forwards /media/imports/... to the API).</summary>
    public const string MediaUrlPrefix = "/media/";

    private static readonly string[] AudioExtensions = [".mp3", ".wav"];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png"];

    public async Task<AdminImportResultDto> ImportAsync(int adminId, int certId, string sourceName, Stream content, CancellationToken cancellationToken = default)
    {
        if (!await db.Certificates.AnyAsync(c => c.CertId == certId, cancellationToken))
        {
            throw new AdminExamException(ApiError.Codes.CertificateUnavailable, $"Certificate with id {certId} not found.", 404);
        }

        var fileName = Path.GetFileName(sourceName?.Trim() ?? string.Empty);
        if (fileName.Length == 0)
        {
            fileName = "upload";
        }

        var batch = new ImportBatch
        {
            CertId = certId,
            SourceName = fileName.Length > 260 ? fileName[..260] : fileName,
            Status = ImportBatchStatus.Pending.ToCode(),
            UploadedBy = adminId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        var report = new ImportReport();
        var package = await ReadPackageAsync(fileName, content, report, cancellationToken);
        if (package is null)
        {
            return await FailAsync(batch, report, cancellationToken);
        }

        var plan = await ValidateAsync(certId, package, report, cancellationToken);
        if (plan is null || report.ErrorCount > 0)
        {
            return await FailAsync(batch, report, cancellationToken);
        }

        return await CommitAsync(batch, plan, package, report, cancellationToken);
    }

    public Task<AdminImportResultDto> ImportManifestAsync(int adminId, int certId, string manifestJson, CancellationToken cancellationToken = default) =>
        ImportAsync(adminId, certId, "manifest.json", new MemoryStream(Encoding.UTF8.GetBytes(manifestJson)), cancellationToken);

    public Task<AdminImportResultDto> ImportPackageZipAsync(int adminId, int certId, Stream zipStream, string fileName = "package.zip", CancellationToken cancellationToken = default) =>
        ImportAsync(adminId, certId, fileName, zipStream, cancellationToken);

    // ---- Reading the upload ----

    /// <summary>The manifest plus, for a .zip, its media entries keyed by their normalised path.</summary>
    private sealed record ImportPackage(ImportManifest Manifest, IReadOnlyDictionary<string, byte[]>? Media);

    // ---- Validating the manifest ----

    private sealed record PlannedExam(
        string Name,
        ExamPurpose Purpose,
        string DeliveryMode,
        int DurationMinutes,
        string? SourceName,
        string? Description,
        int? QuestionCount);

    private sealed record PlannedGroup(
        Section Section,
        ImportManifestGroup Source,
        int DisplayOrder,
        IReadOnlyList<PlannedQuestion> Questions);

    private sealed record PlannedQuestion(
        ImportManifestQuestion Source,
        QuestionType Type,
        int? CategoryId,
        int DisplayOrder,
        string? AcceptedAnswersJson,
        string? RequiredKeywords);

    private sealed record ImportPlan(PlannedExam Exam, IReadOnlyList<PlannedGroup> Groups, IReadOnlyList<string> MediaPaths)
    {
        public int QuestionCount => Groups.Sum(g => g.Questions.Count);
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record BatchRow(ImportBatch Batch, string CertCode, string Email, int Errors, int Warnings);

    /// <summary>Collects every problem found in one upload; written to ImportLog when the batch ends.</summary>
    private sealed class ImportReport
    {
        public List<(int? RowNo, ImportLogLevel Level, string Message)> Entries { get; } = [];

        public int ErrorCount => Entries.Count(e => e.Level == ImportLogLevel.Error);

        public int WarningCount => Entries.Count(e => e.Level == ImportLogLevel.Warn);

        public void Error(int? rowNo, string message) => Entries.Add((rowNo, ImportLogLevel.Error, message));
    }
}
