using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (certId.HasValue) query.Add($"certId={certId.Value}");
        if (!string.IsNullOrWhiteSpace(purpose)) query.Add($"purpose={Uri.EscapeDataString(purpose)}");
        var path = $"api/admin/exams?{string.Join("&", query)}";
        return GetAuthorizedAsync<AdminExamListSummaryDto>(path, bearerToken, cancellationToken);
    }

    public Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAdminExamsAsync(q, status, certId, null, page, pageSize, bearerToken, cancellationToken);

    public Task<AdminExamDetailDto> GetAdminExamByIdAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminExamDetailDto>($"api/admin/exams/{examId}", bearerToken, cancellationToken);

    public Task<AdminExamDetailDto> CreateAdminExamAsync(CreateAdminExamRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<CreateAdminExamRequest, AdminExamDetailDto>("api/admin/exams", request, bearerToken, cancellationToken);

    public Task<AdminExamDetailDto> UpdateAdminExamAsync(int examId, UpdateAdminExamRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PutAuthorizedAsync<UpdateAdminExamRequest, AdminExamDetailDto>($"api/admin/exams/{examId}", request, bearerToken, cancellationToken);

    public Task<AdminPublishExamResultDto> PublishAdminExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<object, AdminPublishExamResultDto>($"api/admin/exams/{examId}/publish", new { }, bearerToken, cancellationToken);

    public Task DeleteAdminExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        DeleteAuthorizedAsync($"api/admin/exams/{examId}", bearerToken, cancellationToken);

    public Task<AdminExamQuestionsDto> GetAdminExamQuestionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminExamQuestionsDto>($"api/admin/exams/{examId}/questions", bearerToken, cancellationToken);

    public Task<AdminQuestionGroupDto> CreateAdminQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<CreateQuestionGroupRequest, AdminQuestionGroupDto>($"api/admin/exams/{examId}/groups", request, bearerToken, cancellationToken);

    public Task<AdminQuestionGroupDto> UpdateAdminQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PutAuthorizedAsync<UpdateQuestionGroupRequest, AdminQuestionGroupDto>($"api/admin/exams/{examId}/groups/{groupId}", request, bearerToken, cancellationToken);

    public Task DeleteAdminQuestionGroupAsync(int examId, int groupId, string bearerToken, CancellationToken cancellationToken = default) =>
        DeleteAuthorizedAsync($"api/admin/exams/{examId}/groups/{groupId}", bearerToken, cancellationToken);

    public Task<AdminQuestionDto> CreateAdminQuestionAsync(int examId, CreateQuestionRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<CreateQuestionRequest, AdminQuestionDto>($"api/admin/exams/{examId}/questions", request, bearerToken, cancellationToken);

    public Task<AdminQuestionDto> UpdateAdminQuestionAsync(int questionId, UpdateQuestionRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PutAuthorizedAsync<UpdateQuestionRequest, AdminQuestionDto>($"api/admin/questions/{questionId}", request, bearerToken, cancellationToken);

    public Task DeleteAdminQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) =>
        DeleteAuthorizedAsync($"api/admin/questions/{questionId}", bearerToken, cancellationToken);

    public Task<AdminExamSectionAudioDto> SaveAdminExamSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SaveExamSectionAudioRequest, AdminExamSectionAudioDto>($"api/admin/exams/{examId}/audio", request, bearerToken, cancellationToken);

    public Task<QuickIngestParseResultDto> ParseQuickIngestAsync(int examId, QuickIngestParseRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<QuickIngestParseRequest, QuickIngestParseResultDto>($"api/admin/exams/{examId}/quick-ingest/parse", request, bearerToken, cancellationToken);

    public Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<QuickIngestCommitRequest, QuickIngestCommitResultDto>($"api/admin/exams/{examId}/quick-ingest/commit", request, bearerToken, cancellationToken);

    public Task<QuickIngestSampleTemplateDto> GetQuickIngestSampleTemplateAsync(int sectionId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<QuickIngestSampleTemplateDto>($"api/admin/sections/{sectionId}/quick-ingest/sample", bearerToken, cancellationToken);

    public Task<SectionGenerationResultDto> GenerateSectionQuestionsAsync(int examId, int sectionId, SectionGenerateRequest? request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SectionGenerateRequest?, SectionGenerationResultDto>($"api/admin/exams/{examId}/sections/{sectionId}/ai-generate", request ?? new SectionGenerateRequest(), bearerToken, cancellationToken);

    public Task<ExamStudioDetailDto> GetAdminExamStudioAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<ExamStudioDetailDto>($"api/admin/exams/{examId}/studio", bearerToken, cancellationToken);

    public Task<ExamAuditResultDto> GetAdminExamAuditAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<ExamAuditResultDto>($"api/admin/exams/{examId}/audit", bearerToken, cancellationToken);

    public async Task ReorderAdminSectionItemsAsync(int examId, ReorderSectionItemsRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/exams/{examId}/reorder");
        AttachBearer(message, bearerToken);
        message.Content = JsonContent.Create(request, options: Json);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<AdminQuestionGroupDto> CreateAdminExamClusterAsync(int examId, CreateExamClusterRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/exams/{examId}/clusters");
        AttachBearer(message, bearerToken);
        message.Content = JsonContent.Create(request, options: Json);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminQuestionGroupDto>(Json, cancellationToken))!;
    }

    public async Task<AdminExamMediaUploadResultDto> UploadAdminExamMediaAsync(int examId, Stream stream, string fileName, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/admin/exams/{examId}/media") { Content = form };
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminExamMediaUploadResultDto>(Json, cancellationToken))!;
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> ExportAdminExamAsync(int examId, string? format, string bearerToken, CancellationToken cancellationToken = default)
    {
        var fmt = string.IsNullOrWhiteSpace(format) ? "zip" : format.ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/admin/exams/{examId}/export?format={fmt}");
        AttachBearer(request, bearerToken);

        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? (fmt == "json" ? "application/json" : "application/zip");
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? $"exam_{examId}_export.{(fmt == "json" ? "json" : "zip")}";

        return (stream, contentType, fileName);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> GetManifestTemplateAsync(int certId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/admin/exams/manifest-template?certId={certId}");
        AttachBearer(request, bearerToken);

        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? $"manifest_template_{certId}.json";

        return (stream, contentType, fileName);
    }

    public Task<ExamGenerationResultDto> GenerateAdminExamAsync(ExamBlueprintDto blueprint, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<ExamBlueprintDto, ExamGenerationResultDto>("api/admin/exams/generate", blueprint, bearerToken, cancellationToken);

    public Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminExamsAsync(q, status, certId, purpose, page, pageSize, string.Empty, cancellationToken);

    public Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminExamsAsync(q, status, certId, null, page, pageSize, string.Empty, cancellationToken);

    public Task<AdminExamDetailDto> GetAdminExamByIdAsync(int examId, CancellationToken cancellationToken = default) =>
        GetAdminExamByIdAsync(examId, string.Empty, cancellationToken);

    public Task<AdminExamDetailDto> CreateAdminExamAsync(CreateAdminExamRequest request, CancellationToken cancellationToken = default) =>
        CreateAdminExamAsync(request, string.Empty, cancellationToken);

    public Task<AdminExamDetailDto> UpdateAdminExamAsync(int examId, UpdateAdminExamRequest request, CancellationToken cancellationToken = default) =>
        UpdateAdminExamAsync(examId, request, string.Empty, cancellationToken);

    public Task<AdminPublishExamResultDto> PublishAdminExamAsync(int examId, CancellationToken cancellationToken = default) =>
        PublishAdminExamAsync(examId, string.Empty, cancellationToken);

    public Task DeleteAdminExamAsync(int examId, CancellationToken cancellationToken = default) =>
        DeleteAdminExamAsync(examId, string.Empty, cancellationToken);

    public Task<AdminExamQuestionsDto> GetAdminExamQuestionsAsync(int examId, CancellationToken cancellationToken = default) =>
        GetAdminExamQuestionsAsync(examId, string.Empty, cancellationToken);

    public Task<AdminQuestionGroupDto> CreateAdminQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, CancellationToken cancellationToken = default) =>
        CreateAdminQuestionGroupAsync(examId, request, string.Empty, cancellationToken);

    public Task<AdminQuestionGroupDto> UpdateAdminQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default) =>
        UpdateAdminQuestionGroupAsync(examId, groupId, request, string.Empty, cancellationToken);

    public Task DeleteAdminQuestionGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default) =>
        DeleteAdminQuestionGroupAsync(examId, groupId, string.Empty, cancellationToken);

    public Task<AdminQuestionDto> CreateAdminQuestionAsync(int examId, CreateQuestionRequest request, CancellationToken cancellationToken = default) =>
        CreateAdminQuestionAsync(examId, request, string.Empty, cancellationToken);

    public Task<AdminQuestionDto> UpdateAdminQuestionAsync(int questionId, UpdateQuestionRequest request, CancellationToken cancellationToken = default) =>
        UpdateAdminQuestionAsync(questionId, request, string.Empty, cancellationToken);

    public Task DeleteAdminQuestionAsync(int questionId, CancellationToken cancellationToken = default) =>
        DeleteAdminQuestionAsync(questionId, string.Empty, cancellationToken);

    public Task<AdminExamSectionAudioDto> SaveAdminExamSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, CancellationToken cancellationToken = default) =>
        SaveAdminExamSectionAudioAsync(examId, request, string.Empty, cancellationToken);

    public Task<QuickIngestParseResultDto> ParseQuickIngestAsync(int examId, QuickIngestParseRequest request, CancellationToken cancellationToken = default) =>
        ParseQuickIngestAsync(examId, request, string.Empty, cancellationToken);

    public Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default) =>
        CommitQuickIngestAsync(examId, request, string.Empty, cancellationToken);

    public Task<QuickIngestSampleTemplateDto> GetQuickIngestSampleTemplateAsync(int sectionId, CancellationToken cancellationToken = default) =>
        GetQuickIngestSampleTemplateAsync(sectionId, string.Empty, cancellationToken);

    public Task<SectionGenerationResultDto> GenerateSectionQuestionsAsync(int examId, int sectionId, SectionGenerateRequest? request, CancellationToken cancellationToken = default) =>
        GenerateSectionQuestionsAsync(examId, sectionId, request, string.Empty, cancellationToken);

    public Task<ExamStudioDetailDto> GetAdminExamStudioAsync(int examId, CancellationToken cancellationToken = default) =>
        GetAdminExamStudioAsync(examId, string.Empty, cancellationToken);

    public Task<ExamAuditResultDto> GetAdminExamAuditAsync(int examId, CancellationToken cancellationToken = default) =>
        GetAdminExamAuditAsync(examId, string.Empty, cancellationToken);

    public Task ReorderAdminSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default) =>
        ReorderAdminSectionItemsAsync(examId, request, string.Empty, cancellationToken);

    public Task<AdminQuestionGroupDto> CreateAdminExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default) =>
        CreateAdminExamClusterAsync(examId, request, string.Empty, cancellationToken);

    public Task<AdminExamMediaUploadResultDto> UploadAdminExamMediaAsync(int examId, Stream stream, string fileName, CancellationToken cancellationToken = default) =>
        UploadAdminExamMediaAsync(examId, stream, fileName, string.Empty, cancellationToken);

    public Task<(Stream Stream, string ContentType, string FileName)> ExportAdminExamAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default) =>
        ExportAdminExamAsync(examId, format, string.Empty, cancellationToken);

    public Task<(Stream Stream, string ContentType, string FileName)> GetManifestTemplateAsync(int certId, CancellationToken cancellationToken = default) =>
        GetManifestTemplateAsync(certId, string.Empty, cancellationToken);

    public Task<ExamGenerationResultDto> GenerateAdminExamAsync(ExamBlueprintDto blueprint, CancellationToken cancellationToken = default) =>
        GenerateAdminExamAsync(blueprint, string.Empty, cancellationToken);
}
