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
    public Task<SpeakingInstructionsDto> GetSpeakingInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<SpeakingInstructionsDto>($"api/learner/speaking/{examId}/instructions", bearerToken, cancellationToken);

    public async Task<StartSpeakingAttemptResponse> StartSpeakingAttemptAsync(int examId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/speaking/{examId}/start");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<StartSpeakingAttemptResponse>(Json, cancellationToken))!;
    }

    public Task<SpeakingTakeDto> GetSpeakingTakeAsync(int attemptId, int? questionId, string bearerToken, CancellationToken cancellationToken = default)
    {
        var path = questionId.HasValue
            ? $"api/learner/speaking/attempts/{attemptId}?questionId={questionId.Value}"
            : $"api/learner/speaking/attempts/{attemptId}";
        return GetAuthorizedAsync<SpeakingTakeDto>(path, bearerToken, cancellationToken);
    }

    public async Task<UploadSpeakingAudioResponse> UploadSpeakingAudioAsync(int attemptId, int questionId, Stream audioStream, string fileName, string contentType, int? durationMs, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(audioStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "audioFile", fileName);

        if (durationMs.HasValue)
        {
            content.Add(new StringContent(durationMs.Value.ToString()), "durationMs");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/learner/speaking/attempts/{attemptId}/questions/{questionId}/audio")
        {
            Content = content
        };
        AttachBearer(request, bearerToken);

        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<UploadSpeakingAudioResponse>(Json, cancellationToken))!;
    }

    public async Task<SubmitSpeakingResponse> SubmitSpeakingAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/speaking/attempts/{attemptId}/submit");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SubmitSpeakingResponse>(Json, cancellationToken))!;
    }

    public Task<SpeakingGradingPendingDto> GetSpeakingGradingPendingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<SpeakingGradingPendingDto>($"api/learner/speaking/attempts/{attemptId}/pending", bearerToken, cancellationToken);

    public async Task RetrySpeakingGradingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/speaking/attempts/{attemptId}/retry");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<SpeakingReviewDto> GetSpeakingReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<SpeakingReviewDto>($"api/learner/speaking/attempts/{attemptId}/review", bearerToken, cancellationToken);

    public async Task<(Stream Stream, string ContentType, string FileName)> GetSpeakingAudioStreamAsync(int attemptId, int questionId, string bearerToken, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/learner/speaking/attempts/{attemptId}/questions/{questionId}/audio");
        AttachBearer(request, bearerToken);

        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? $"audio_{attemptId}_{questionId}.webm";
        return (stream, contentType, fileName);
    }

    public Task<SpeakingInstructionsDto> GetSpeakingInstructionsAsync(int examId, CancellationToken cancellationToken = default) =>
        GetSpeakingInstructionsAsync(examId, string.Empty, cancellationToken);

    public Task<StartSpeakingAttemptResponse> StartSpeakingAttemptAsync(int examId, CancellationToken cancellationToken = default) =>
        StartSpeakingAttemptAsync(examId, string.Empty, cancellationToken);

    public Task<SpeakingTakeDto> GetSpeakingTakeAsync(int attemptId, int? questionId, CancellationToken cancellationToken = default) =>
        GetSpeakingTakeAsync(attemptId, questionId, string.Empty, cancellationToken);

    public Task<UploadSpeakingAudioResponse> UploadSpeakingAudioAsync(int attemptId, int questionId, Stream audioStream, string fileName, string contentType, int? durationMs, CancellationToken cancellationToken = default) =>
        UploadSpeakingAudioAsync(attemptId, questionId, audioStream, fileName, contentType, durationMs, string.Empty, cancellationToken);

    public Task<SubmitSpeakingResponse> SubmitSpeakingAttemptAsync(int attemptId, CancellationToken cancellationToken = default) =>
        SubmitSpeakingAttemptAsync(attemptId, string.Empty, cancellationToken);

    public Task<SpeakingGradingPendingDto> GetSpeakingGradingPendingAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetSpeakingGradingPendingAsync(attemptId, string.Empty, cancellationToken);

    public Task RetrySpeakingGradingAsync(int attemptId, CancellationToken cancellationToken = default) =>
        RetrySpeakingGradingAsync(attemptId, string.Empty, cancellationToken);

    public Task<SpeakingReviewDto> GetSpeakingReviewAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetSpeakingReviewAsync(attemptId, string.Empty, cancellationToken);

    public Task<(Stream Stream, string ContentType, string FileName)> GetSpeakingAudioStreamAsync(int attemptId, int questionId, CancellationToken cancellationToken = default) =>
        GetSpeakingAudioStreamAsync(attemptId, questionId, string.Empty, cancellationToken);

}
