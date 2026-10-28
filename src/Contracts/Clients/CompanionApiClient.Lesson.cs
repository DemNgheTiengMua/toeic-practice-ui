using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<LessonDetailDto> GetLessonAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LessonDetailDto>($"api/learner/lessons/{lessonId}", bearerToken, cancellationToken);

    public Task<LessonDetailDto> GetLessonAsync(int lessonId, CancellationToken cancellationToken = default) =>
        GetLessonAsync(lessonId, string.Empty, cancellationToken);

    public Task<LessonProgressDto> GetLessonProgressAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LessonProgressDto>($"api/learner/lessons/{lessonId}/progress", bearerToken, cancellationToken);

    public Task<LessonProgressDto> GetLessonProgressAsync(int lessonId, CancellationToken cancellationToken = default) =>
        GetLessonProgressAsync(lessonId, string.Empty, cancellationToken);

    public async Task RecordCardViewAsync(int lessonId, int cardId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/lessons/{lessonId}/cards/{cardId}/view");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task RecordCardViewAsync(int lessonId, int cardId, CancellationToken cancellationToken = default) =>
        RecordCardViewAsync(lessonId, cardId, string.Empty, cancellationToken);

    public Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LessonDetailDto?>($"api/learner/lessons/by-category/{categoryId}", bearerToken, cancellationToken);

    public Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, CancellationToken cancellationToken = default) =>
        GetLessonByCategoryAsync(categoryId, string.Empty, cancellationToken);

    public async Task UnlockPathAsync(int learnerPathId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/learning-path/{learnerPathId}/unlock");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task UnlockPathAsync(int learnerPathId, CancellationToken cancellationToken = default) =>
        UnlockPathAsync(learnerPathId, string.Empty, cancellationToken);
}
