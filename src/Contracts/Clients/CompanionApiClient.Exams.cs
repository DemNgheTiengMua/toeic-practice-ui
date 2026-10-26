using System.Net.Http.Json;

namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<MockExamListViewDto> GetMockExamsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<MockExamListViewDto>("api/learner/exams", bearerToken, cancellationToken);

    public Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<MockExamInstructionsDto>($"api/learner/exams/{examId}/instructions", bearerToken, cancellationToken);

    public async Task<StartMockExamResponse> StartMockExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/exams/{examId}/start");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<StartMockExamResponse>(Json, cancellationToken))!;
    }

    public Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, string bearerToken, CancellationToken cancellationToken = default)
    {
        var path = q.HasValue ? $"api/learner/attempts/{attemptId}?q={q.Value}" : $"api/learner/attempts/{attemptId}";
        return GetAuthorizedAsync<AttemptTakeDto>(path, bearerToken, cancellationToken);
    }

    public async Task<SaveAnswerResultDto> SaveAttemptAnswerAsync(int attemptId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/answers/{questionId}")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SaveAnswerResultDto>(Json, cancellationToken))!;
    }

    public async Task<SaveAttemptDraftResponse> SaveAttemptDraftAsync(
        int attemptId,
        SaveAttemptDraftRequest request,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/draft")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SaveAttemptDraftResponse>(Json, cancellationToken))!;
    }

    public async Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/submit");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SubmitAttemptResponse>(Json, cancellationToken))!;
    }

    public async Task<SubmitAttemptResponse> SubmitAttemptWithDraftsAsync(
        int attemptId,
        SubmitAttemptRequest request,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/submit-with-drafts")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SubmitAttemptResponse>(Json, cancellationToken))!;
    }

    public async Task<AttemptTransitionResponse> TransitionSkillAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/transition/skill");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AttemptTransitionResponse>(Json, cancellationToken))!;
    }

    public async Task<AttemptTransitionResponse> TransitionUnitAsync(int attemptId, TransitionUnitRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/transition/unit")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AttemptTransitionResponse>(Json, cancellationToken))!;
    }

    public async Task<AttemptTransitionResponse> TransitionGroupAsync(int attemptId, TransitionGroupRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/transition/group")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AttemptTransitionResponse>(Json, cancellationToken))!;
    }

    public async Task<AttemptTransitionResponse> StartAudioReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/audio-review/start");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AttemptTransitionResponse>(Json, cancellationToken))!;
    }

    public async Task CancelAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/cancel");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AttemptResultDto>($"api/learner/attempts/{attemptId}/result", bearerToken, cancellationToken);

    public Task<ExamHistoryDto> GetExamHistoryAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<ExamHistoryDto>("api/learner/exams/history", bearerToken, cancellationToken);

    public Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AttemptReviewDto>($"api/learner/attempts/{attemptId}/review", bearerToken, cancellationToken);

    public Task<PreflightStatusDto> GetPreflightStatusAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PreflightStatusDto>($"api/learner/attempts/{attemptId}/preflight", bearerToken, cancellationToken);

    public async Task<PreflightStatusDto> VerifyPreflightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/preflight/verify");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PreflightStatusDto>(Json, cancellationToken))!;
    }

    public Task<SittingSyncDto> SaveSittingSyncAsync(int attemptId, SittingSyncRequestDto request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SittingSyncRequestDto, SittingSyncDto>($"api/learner/attempts/{attemptId}/sync", request, bearerToken, cancellationToken);

    public Task<AttemptResumeDto> ResumeAttemptByCodeAsync(ResumeAttemptRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<ResumeAttemptRequest, AttemptResumeDto>("api/learner/attempts/resume", request, bearerToken, cancellationToken);

    public Task<AiInsightDto> GetExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AiInsightDto>($"api/learner/insights/exam/{attemptId}", bearerToken, cancellationToken);

    public async Task RetryExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/insights/exam/{attemptId}/retry");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<MockExamListViewDto> GetMockExamsAsync(CancellationToken cancellationToken = default) =>
        GetMockExamsAsync(string.Empty, cancellationToken);

    public Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, CancellationToken cancellationToken = default) =>
        GetMockExamInstructionsAsync(examId, string.Empty, cancellationToken);

    public Task<StartMockExamResponse> StartMockExamAsync(int examId, CancellationToken cancellationToken = default) =>
        StartMockExamAsync(examId, string.Empty, cancellationToken);

    public Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, CancellationToken cancellationToken = default) =>
        GetAttemptTakeAsync(attemptId, q, string.Empty, cancellationToken);

    public Task<SaveAnswerResultDto> SaveAttemptAnswerAsync(int attemptId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default) =>
        SaveAttemptAnswerAsync(attemptId, questionId, request, string.Empty, cancellationToken);

    public Task<SaveAttemptDraftResponse> SaveAttemptDraftAsync(
        int attemptId,
        SaveAttemptDraftRequest request,
        CancellationToken cancellationToken = default) =>
        SaveAttemptDraftAsync(attemptId, request, string.Empty, cancellationToken);

    public Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, CancellationToken cancellationToken = default) =>
        SubmitAttemptAsync(attemptId, string.Empty, cancellationToken);

    public Task<SubmitAttemptResponse> SubmitAttemptWithDraftsAsync(
        int attemptId,
        SubmitAttemptRequest request,
        CancellationToken cancellationToken = default) =>
        SubmitAttemptWithDraftsAsync(attemptId, request, string.Empty, cancellationToken);

    public Task<AttemptTransitionResponse> TransitionSkillAsync(int attemptId, CancellationToken cancellationToken = default) =>
        TransitionSkillAsync(attemptId, string.Empty, cancellationToken);

    public Task<AttemptTransitionResponse> TransitionUnitAsync(int attemptId, TransitionUnitRequest request, CancellationToken cancellationToken = default) =>
        TransitionUnitAsync(attemptId, request, string.Empty, cancellationToken);

    public Task<AttemptTransitionResponse> TransitionGroupAsync(int attemptId, TransitionGroupRequest request, CancellationToken cancellationToken = default) =>
        TransitionGroupAsync(attemptId, request, string.Empty, cancellationToken);

    public Task<AttemptTransitionResponse> StartAudioReviewAsync(int attemptId, CancellationToken cancellationToken = default) =>
        StartAudioReviewAsync(attemptId, string.Empty, cancellationToken);

    public Task CancelAttemptAsync(int attemptId, CancellationToken cancellationToken = default) =>
        CancelAttemptAsync(attemptId, string.Empty, cancellationToken);

    public Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetAttemptResultAsync(attemptId, string.Empty, cancellationToken);

    public Task<ExamHistoryDto> GetExamHistoryAsync(CancellationToken cancellationToken = default) =>
        GetExamHistoryAsync(string.Empty, cancellationToken);

    public Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetAttemptReviewAsync(attemptId, string.Empty, cancellationToken);

    public Task<PreflightStatusDto> GetPreflightStatusAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetPreflightStatusAsync(attemptId, string.Empty, cancellationToken);

    public Task<PreflightStatusDto> VerifyPreflightAsync(int attemptId, CancellationToken cancellationToken = default) =>
        VerifyPreflightAsync(attemptId, string.Empty, cancellationToken);

    public Task<SittingSyncDto> SaveSittingSyncAsync(int attemptId, SittingSyncRequestDto request, CancellationToken cancellationToken = default) =>
        SaveSittingSyncAsync(attemptId, request, string.Empty, cancellationToken);

    public Task<AttemptResumeDto> ResumeAttemptByCodeAsync(ResumeAttemptRequest request, CancellationToken cancellationToken = default) =>
        ResumeAttemptByCodeAsync(request, string.Empty, cancellationToken);

    public Task<AiInsightDto> GetExamInsightAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetExamInsightAsync(attemptId, string.Empty, cancellationToken);

    public Task RetryExamInsightAsync(int attemptId, CancellationToken cancellationToken = default) =>
        RetryExamInsightAsync(attemptId, string.Empty, cancellationToken);
}
