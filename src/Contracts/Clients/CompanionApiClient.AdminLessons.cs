using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<IReadOnlyList<AdminLessonSummaryDto>> GetAdminLessonsAsync(int? certId, int? skillId, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (certId.HasValue) query.Add($"certId={certId.Value}");
        if (skillId.HasValue) query.Add($"skillId={skillId.Value}");
        var url = query.Count > 0 ? $"api/admin/lessons?{string.Join("&", query)}" : "api/admin/lessons";
        return GetAuthorizedAsync<IReadOnlyList<AdminLessonSummaryDto>>(url, bearerToken, cancellationToken);
    }

    public Task<AdminLessonDetailDto> GetAdminLessonByIdAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminLessonDetailDto>($"api/admin/lessons/{lessonId}", bearerToken, cancellationToken);

    public Task<AdminLessonDetailDto> CreateAdminLessonAsync(AdminLessonUpsertDto request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<AdminLessonUpsertDto, AdminLessonDetailDto>("api/admin/lessons", request, bearerToken, cancellationToken);

    public Task<AdminLessonDetailDto> UpdateAdminLessonAsync(int lessonId, AdminLessonUpsertDto request, string bearerToken, CancellationToken cancellationToken = default) =>
        PutAuthorizedAsync<AdminLessonUpsertDto, AdminLessonDetailDto>($"api/admin/lessons/{lessonId}", request, bearerToken, cancellationToken);

    public Task DeleteAdminLessonAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) =>
        DeleteAuthorizedAsync($"api/admin/lessons/{lessonId}", bearerToken, cancellationToken);

    public async Task<AdminLessonImportResultDto> UploadAdminLessonImportAsync(int certId, bool overwrite, Stream content, string fileName, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(certId.ToString()), "certId");
        form.Add(new StringContent(overwrite.ToString().ToLowerInvariant()), "overwrite");
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/admin/lessons/import?certId={certId}&overwrite={overwrite.ToString().ToLowerInvariant()}") { Content = form };
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminLessonImportResultDto>(Json, cancellationToken))!;
    }

    public Task<LessonManifestDto> ExportAdminLessonAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LessonManifestDto>($"api/admin/lessons/{lessonId}/export", bearerToken, cancellationToken);

    public Task<LessonManifestDto> GetAdminLessonTemplateAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LessonManifestDto>("api/admin/lessons/template", bearerToken, cancellationToken);

    public Task<AdminLessonFormOptionsDto> GetAdminLessonFormOptionsAsync(int? certId, string bearerToken, CancellationToken cancellationToken = default)
    {
        var url = certId.HasValue ? $"api/admin/lessons/options?certId={certId.Value}" : "api/admin/lessons/options";
        return GetAuthorizedAsync<AdminLessonFormOptionsDto>(url, bearerToken, cancellationToken);
    }

    public Task<IReadOnlyList<AdminLessonSummaryDto>> GetAdminLessonsAsync(int? certId = null, int? skillId = null, CancellationToken cancellationToken = default) =>
        GetAdminLessonsAsync(certId, skillId, string.Empty, cancellationToken);

    public Task<AdminLessonDetailDto> GetAdminLessonByIdAsync(int lessonId, CancellationToken cancellationToken = default) =>
        GetAdminLessonByIdAsync(lessonId, string.Empty, cancellationToken);

    public Task<AdminLessonDetailDto> CreateAdminLessonAsync(AdminLessonUpsertDto request, CancellationToken cancellationToken = default) =>
        CreateAdminLessonAsync(request, string.Empty, cancellationToken);

    public Task<AdminLessonDetailDto> UpdateAdminLessonAsync(int lessonId, AdminLessonUpsertDto request, CancellationToken cancellationToken = default) =>
        UpdateAdminLessonAsync(lessonId, request, string.Empty, cancellationToken);

    public Task DeleteAdminLessonAsync(int lessonId, CancellationToken cancellationToken = default) =>
        DeleteAdminLessonAsync(lessonId, string.Empty, cancellationToken);

    public Task<AdminLessonImportResultDto> UploadAdminLessonImportAsync(int certId, bool overwrite, Stream content, string fileName, CancellationToken cancellationToken = default) =>
        UploadAdminLessonImportAsync(certId, overwrite, content, fileName, string.Empty, cancellationToken);

    public Task<LessonManifestDto> ExportAdminLessonAsync(int lessonId, CancellationToken cancellationToken = default) =>
        ExportAdminLessonAsync(lessonId, string.Empty, cancellationToken);

    public Task<LessonManifestDto> GetAdminLessonTemplateAsync(CancellationToken cancellationToken = default) =>
        GetAdminLessonTemplateAsync(string.Empty, cancellationToken);

    public Task<AdminLessonFormOptionsDto> GetAdminLessonFormOptionsAsync(int? certId = null, CancellationToken cancellationToken = default) =>
        GetAdminLessonFormOptionsAsync(certId, string.Empty, cancellationToken);
}
