using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(source)) query.Add($"source={Uri.EscapeDataString(source)}");
        if (sectionId.HasValue) query.Add($"sectionId={sectionId.Value}");
        if (categoryId.HasValue) query.Add($"categoryId={categoryId.Value}");
        var path = query.Count > 0 ? $"api/learner/saved-questions?{string.Join("&", query)}" : "api/learner/saved-questions";
        return GetAuthorizedAsync<SavedQuestionsViewDto>(path, bearerToken, cancellationToken);
    }

    public Task<SavedQuestionItemDto> GetSavedQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<SavedQuestionItemDto>($"api/learner/saved-questions/{questionId}", bearerToken, cancellationToken);

    public async Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/saved-questions/{questionId}")
        {
            Content = JsonContent.Create(new SaveQuestionRequest(source ?? "manual"), options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SaveQuestionResultDto>(Json, cancellationToken))!;
    }

    public async Task<SaveQuestionResultDto> UnsaveQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Delete, $"api/learner/saved-questions/{questionId}");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SaveQuestionResultDto>(Json, cancellationToken))!;
    }

    public Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, CancellationToken cancellationToken = default) =>
        GetSavedQuestionsAsync(source, sectionId, categoryId, string.Empty, cancellationToken);

    public Task<SavedQuestionItemDto> GetSavedQuestionAsync(int questionId, CancellationToken cancellationToken = default) =>
        GetSavedQuestionAsync(questionId, string.Empty, cancellationToken);

    public Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, CancellationToken cancellationToken = default) =>
        SaveQuestionAsync(questionId, source, string.Empty, cancellationToken);

    public Task<SaveQuestionResultDto> UnsaveQuestionAsync(int questionId, CancellationToken cancellationToken = default) =>
        UnsaveQuestionAsync(questionId, string.Empty, cancellationToken);
}
