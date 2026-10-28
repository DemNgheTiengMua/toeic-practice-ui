using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapExams(RouteGroupBuilder admin)
    {

        // ---- Admin Exam & Question Editor endpoints ----
        admin.MapGet("/exams", async (
            string? q,
            string? status,
            int? certId,
            string? purpose,
            int? page,
            int? pageSize,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            var summary = await exams.GetExamsAsync(q, status, certId, purpose, page ?? 1, pageSize ?? 20, cancellationToken);
            return Results.Ok(summary);
        });

        admin.MapPost("/exams", async (
            CreateAdminExamRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await exams.CreateExamAsync(request, cancellationToken);
                return Results.Created($"/api/admin/exams/{created.ExamId}", created);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/generate", async (
            ExamBlueprintDto request,
            HttpContext http,
            IExamGeneratorService generator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await generator.GenerateAndIngestExamAsync(request, CurrentUserId(http), cancellationToken);
                return Results.Ok(result);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapGet("/exams/{examId:int}", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var exam = await exams.GetExamByIdAsync(examId, cancellationToken);
                return Results.Ok(exam);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPut("/exams/{examId:int}", async (
            int examId,
            UpdateAdminExamRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await exams.UpdateExamAsync(examId, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/publish", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await exams.PublishExamAsync(examId, cancellationToken);
                return Results.Ok(result);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapDelete("/exams/{examId:int}", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await exams.DeleteExamAsync(examId, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/audio", async (
            int examId,
            SaveExamSectionAudioRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var audio = await exams.SaveSectionAudioAsync(examId, request, cancellationToken);
                return Results.Ok(audio);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/sections/{sectionId:int}/ai-generate", async (
            int examId,
            int sectionId,
            SectionGenerateRequest? request,
            IExamGeneratorService generator,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var adminUserId = CurrentUserId(http);
                var result = await generator.GenerateSectionQuestionsAsync(examId, sectionId, adminUserId, request?.QuestionCount, cancellationToken);
                return Results.Ok(result);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/media", async Task<IResult> (
            int examId,
            HttpRequest request,
            IFileStore fileStore,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Problem(StatusCodes.Status400BadRequest, "Expected multipart/form-data content.", ApiError.Codes.Validation);
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Problem(StatusCodes.Status400BadRequest, "Choose a file to upload.", ApiError.Codes.Validation);
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".svg", ".webp", ".mp3", ".wav", ".ogg", ".m4a" };
            if (!allowed.Contains(ext))
            {
                return Problem(StatusCodes.Status400BadRequest, $"File format '{ext}' is not supported.", ApiError.Codes.Validation);
            }

            await using var stream = file.OpenReadStream();
            var relativePath = await fileStore.SaveExamMediaAsync(examId, stream, ext, cancellationToken);
            var fileName = Path.GetFileName(relativePath);
            var result = new AdminExamMediaUploadResultDto(
                Url: $"/media/exams/{examId}/{fileName}",
                Path: relativePath,
                FileName: fileName);

            return Results.Ok(result);
        }).DisableAntiforgery();

        admin.MapGet("/exams/{examId:int}/export", async (
            int examId,
            string? format,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var (stream, contentType, fileName) = await exams.ExportExamPackageAsync(examId, format, cancellationToken);
                return Results.File(stream, contentType, fileName);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapGet("/exams/manifest-template", async (
            int certId,
            IQuestionImportService imports,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var (stream, contentType, fileName) = await imports.GenerateManifestTemplateAsync(certId, cancellationToken);
                return Results.File(stream, contentType, fileName);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
