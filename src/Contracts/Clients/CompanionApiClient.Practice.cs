using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<PracticeCatalogDto> GetPracticeCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PracticeCatalogDto>("api/learner/practice/catalog", bearerToken, cancellationToken);

    public async Task<PracticeSessionDto> StartPracticeSessionAsync(StartPracticeRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/learner/practice/sessions")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PracticeSessionDto>(Json, cancellationToken))!;
    }

    public Task<PracticeSessionDto> GetPracticeSessionInfoAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PracticeSessionDto>($"api/learner/practice/sessions/{sessionId}", bearerToken, cancellationToken);

    public Task<PracticeQuestionDto> GetPracticeQuestionAsync(int sessionId, int displayOrder, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PracticeQuestionDto>($"api/learner/practice/sessions/{sessionId}/questions/{displayOrder}", bearerToken, cancellationToken);

    public Task<PracticeSessionTakeDto> GetPracticeSessionTakeAsync(int sessionId, int? displayOrder, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PracticeSessionTakeDto>($"api/learner/practice/sessions/{sessionId}/take" + (displayOrder.HasValue ? $"?q={displayOrder.Value}" : ""), bearerToken, cancellationToken);

    public async Task<IReadOnlyList<PracticeQuestionDto>> GetPracticeSessionQuestionsAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default)
    {
        var list = await GetAuthorizedAsync<List<PracticeQuestionDto>>($"api/learner/practice/sessions/{sessionId}/questions", bearerToken, cancellationToken);
        return list ?? (IReadOnlyList<PracticeQuestionDto>)[];
    }

    public async Task<RevealHintResponse> RevealPracticeHintAsync(int sessionId, int questionId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/practice/sessions/{sessionId}/questions/{questionId}/hint");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<RevealHintResponse>(Json, cancellationToken))!;
    }

    public async Task<GradedAnswerDto> SubmitPracticeAnswerAsync(int sessionId, int questionId, SubmitPracticeAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/practice/sessions/{sessionId}/questions/{questionId}/answer")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<GradedAnswerDto>(Json, cancellationToken))!;
    }

    public Task<PracticeSummaryDto> GetPracticeSummaryAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PracticeSummaryDto>($"api/learner/practice/sessions/{sessionId}/summary", bearerToken, cancellationToken);

    public Task<PracticeCatalogDto> GetPracticeCatalogAsync(CancellationToken cancellationToken = default) =>
        GetPracticeCatalogAsync(string.Empty, cancellationToken);

    public Task<PracticeSessionDto> StartPracticeSessionAsync(StartPracticeRequest request, CancellationToken cancellationToken = default) =>
        StartPracticeSessionAsync(request, string.Empty, cancellationToken);

    public Task<PracticeSessionDto> GetPracticeSessionInfoAsync(int sessionId, CancellationToken cancellationToken = default) =>
        GetPracticeSessionInfoAsync(sessionId, string.Empty, cancellationToken);

    public Task<PracticeQuestionDto> GetPracticeQuestionAsync(int sessionId, int displayOrder, CancellationToken cancellationToken = default) =>
        GetPracticeQuestionAsync(sessionId, displayOrder, string.Empty, cancellationToken);

    public Task<PracticeSessionTakeDto> GetPracticeSessionTakeAsync(int sessionId, int? displayOrder = null, CancellationToken cancellationToken = default) =>
        GetPracticeSessionTakeAsync(sessionId, displayOrder, string.Empty, cancellationToken);

    public Task<IReadOnlyList<PracticeQuestionDto>> GetPracticeSessionQuestionsAsync(int sessionId, CancellationToken cancellationToken = default) =>
        GetPracticeSessionQuestionsAsync(sessionId, string.Empty, cancellationToken);

    public Task<RevealHintResponse> RevealPracticeHintAsync(int sessionId, int questionId, CancellationToken cancellationToken = default) =>
        RevealPracticeHintAsync(sessionId, questionId, string.Empty, cancellationToken);

    public Task<GradedAnswerDto> SubmitPracticeAnswerAsync(int sessionId, int questionId, SubmitPracticeAnswerRequest request, CancellationToken cancellationToken = default) =>
        SubmitPracticeAnswerAsync(sessionId, questionId, request, string.Empty, cancellationToken);

    public Task<PracticeSummaryDto> GetPracticeSummaryAsync(int sessionId, CancellationToken cancellationToken = default) =>
        GetPracticeSummaryAsync(sessionId, string.Empty, cancellationToken);
}
