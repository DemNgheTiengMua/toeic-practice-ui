using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<LessonDetailDto> GetLessonModuleAsync(int lessonId, string module, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LessonDetailDto>(
            $"api/learner/lessons/{lessonId}?module={Uri.EscapeDataString(module)}", bearerToken, cancellationToken);

    public Task<LessonDetailDto> GetLessonModuleAsync(int lessonId, string module, CancellationToken cancellationToken = default) =>
        GetLessonModuleAsync(lessonId, module, string.Empty, cancellationToken);

    /// <summary>#240: the lesson of a cell, so a caller holding a step's coordinate asks for the cell
    /// it means rather than a lesson id it would have to have been told.</summary>
    public Task<LessonDetailDto> GetLessonForCellAsync(
        int skillId, int topicId, string level, string module, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LessonDetailDto>(
            $"api/learner/lessons/cell?skillId={skillId}&topicId={topicId}" +
            $"&level={Uri.EscapeDataString(level)}&module={Uri.EscapeDataString(module)}",
            bearerToken, cancellationToken);

    public Task<LessonDetailDto> GetLessonForCellAsync(
        int skillId, int topicId, string level, string module, CancellationToken cancellationToken = default) =>
        GetLessonForCellAsync(skillId, topicId, level, module, string.Empty, cancellationToken);

    public Task<LessonProgressDto> GetLessonProgressModuleAsync(int lessonId, string module, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LessonProgressDto>(
            $"api/learner/lessons/{lessonId}/progress?module={Uri.EscapeDataString(module)}",
            bearerToken, cancellationToken);

    public Task<LessonProgressDto> GetLessonProgressModuleAsync(int lessonId, string module, CancellationToken cancellationToken = default) =>
        GetLessonProgressModuleAsync(lessonId, module, string.Empty, cancellationToken);

    /// <summary>The primary module, for a caller that names none. Concrete as well as on the interface,
    /// so a caller holding the client directly compiles too.</summary>
    public Task<LessonDetailDto> GetLessonAsync(int lessonId, CancellationToken cancellationToken = default) =>
        GetLessonModuleAsync(lessonId, LessonModuleCodes.Primary, cancellationToken);

    public Task<LessonDetailDto> GetLessonAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetLessonModuleAsync(lessonId, LessonModuleCodes.Primary, bearerToken, cancellationToken);

    public Task<LessonProgressDto> GetLessonProgressAsync(int lessonId, CancellationToken cancellationToken = default) =>
        GetLessonProgressModuleAsync(lessonId, LessonModuleCodes.Primary, cancellationToken);

    public Task<LessonProgressDto> GetLessonProgressAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetLessonProgressModuleAsync(lessonId, LessonModuleCodes.Primary, bearerToken, cancellationToken);

    public async Task RecordCardViewAsync(int lessonId, int cardId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/lessons/{lessonId}/cards/{cardId}/view");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task RecordCardViewAsync(int lessonId, int cardId, CancellationToken cancellationToken = default) =>
        RecordCardViewAsync(lessonId, cardId, string.Empty, cancellationToken);

    public async Task<MicroCheckResultDto> SubmitMicroCheckAsync(int lessonId, int cardId, string selectedOption, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/lessons/{lessonId}/cards/{cardId}/micro-check")
        {
            Content = JsonContent.Create(new MicroCheckRequest(selectedOption), options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MicroCheckResultDto>(Json, cancellationToken))!;
    }

    public Task<MicroCheckResultDto> SubmitMicroCheckAsync(int lessonId, int cardId, string selectedOption, CancellationToken cancellationToken = default) =>
        SubmitMicroCheckAsync(lessonId, cardId, selectedOption, string.Empty, cancellationToken);

    public Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LessonDetailDto?>($"api/learner/lessons/by-category/{categoryId}", bearerToken, cancellationToken);

    public Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, CancellationToken cancellationToken = default) =>
        GetLessonByCategoryAsync(categoryId, string.Empty, cancellationToken);
}
