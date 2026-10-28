using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<WritingInstructionsDto> GetWritingInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<WritingInstructionsDto>($"api/learner/writing/{examId}/instructions", bearerToken, cancellationToken);

    public async Task<StartWritingAttemptResponse> StartWritingAttemptAsync(int examId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/writing/{examId}/start");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<StartWritingAttemptResponse>(Json, cancellationToken))!;
    }

    public Task<WritingTakeDto> GetWritingTakeAsync(int attemptId, int? questionId, string bearerToken, CancellationToken cancellationToken = default)
    {
        var path = questionId.HasValue
            ? $"api/learner/writing/attempts/{attemptId}?questionId={questionId.Value}"
            : $"api/learner/writing/attempts/{attemptId}";
        return GetAuthorizedAsync<WritingTakeDto>(path, bearerToken, cancellationToken);
    }

    public async Task<SaveWritingDraftResponse> SaveWritingDraftAsync(int attemptId, SaveWritingDraftRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/writing/attempts/{attemptId}/draft")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SaveWritingDraftResponse>(Json, cancellationToken))!;
    }

    public async Task<SubmitWritingResponse> SubmitWritingAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/writing/attempts/{attemptId}/submit");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SubmitWritingResponse>(Json, cancellationToken))!;
    }

    public Task<WritingGradingPendingDto> GetWritingGradingPendingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<WritingGradingPendingDto>($"api/learner/writing/attempts/{attemptId}/pending", bearerToken, cancellationToken);

    public async Task RetryWritingGradingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/writing/attempts/{attemptId}/retry");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<WritingReviewDto> GetWritingReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<WritingReviewDto>($"api/learner/writing/attempts/{attemptId}/review", bearerToken, cancellationToken);

    public Task<WritingInstructionsDto> GetWritingInstructionsAsync(int examId, CancellationToken cancellationToken = default) =>
        GetWritingInstructionsAsync(examId, string.Empty, cancellationToken);

    public Task<StartWritingAttemptResponse> StartWritingAttemptAsync(int examId, CancellationToken cancellationToken = default) =>
        StartWritingAttemptAsync(examId, string.Empty, cancellationToken);

    public Task<WritingTakeDto> GetWritingTakeAsync(int attemptId, int? questionId, CancellationToken cancellationToken = default) =>
        GetWritingTakeAsync(attemptId, questionId, string.Empty, cancellationToken);

    public Task<SaveWritingDraftResponse> SaveWritingDraftAsync(int attemptId, SaveWritingDraftRequest request, CancellationToken cancellationToken = default) =>
        SaveWritingDraftAsync(attemptId, request, string.Empty, cancellationToken);

    public Task<SubmitWritingResponse> SubmitWritingAttemptAsync(int attemptId, CancellationToken cancellationToken = default) =>
        SubmitWritingAttemptAsync(attemptId, string.Empty, cancellationToken);

    public Task<WritingGradingPendingDto> GetWritingGradingPendingAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetWritingGradingPendingAsync(attemptId, string.Empty, cancellationToken);

    public Task RetryWritingGradingAsync(int attemptId, CancellationToken cancellationToken = default) =>
        RetryWritingGradingAsync(attemptId, string.Empty, cancellationToken);

    public Task<WritingReviewDto> GetWritingReviewAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetWritingReviewAsync(attemptId, string.Empty, cancellationToken);

}
