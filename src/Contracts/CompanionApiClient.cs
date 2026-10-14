using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>What the MVC site knows about the API: typed calls, errors surfaced with their stable code.
/// Calls that need identity take the JWT from the MVC site's cookie as <paramref name="bearerToken"/>
/// and forward it — the browser never sees the token (ADR-0002).</summary>
public interface ICompanionApiClient
{
    Task<IReadOnlyList<CertificateDto>> GetCertificatesAsync(CancellationToken cancellationToken = default);

    Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<AuthTokenResponse> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);

    Task<AuthUserDto> GetMeAsync(string bearerToken, CancellationToken cancellationToken = default);

    Task<ProfileDto> GetProfileAsync(string bearerToken, CancellationToken cancellationToken = default);

    Task<ProfileDto> UpdateProfileAsync(UpdateProfileRequest request, string bearerToken, CancellationToken cancellationToken = default);

    Task ChangePasswordAsync(ChangePasswordRequest request, string bearerToken, CancellationToken cancellationToken = default);

    Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default);

    Task ResendActivationAsync(string bearerToken, CancellationToken cancellationToken = default);

    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task<LearnerContextDto> GetLearnerContextAsync(string bearerToken, CancellationToken cancellationToken = default);

    Task SwitchCertificateAsync(string bearerToken, string code, CancellationToken cancellationToken = default);

    Task<LearnerDashboardDto> GetDashboardAsync(string bearerToken, CancellationToken cancellationToken = default);

    Task<PracticeCatalogDto> GetPracticeCatalogAsync(string bearerToken, CancellationToken cancellationToken = default);

    Task<PracticeSessionDto> StartPracticeSessionAsync(StartPracticeRequest request, string bearerToken, CancellationToken cancellationToken = default);

    Task<PracticeSessionDto> GetPracticeSessionInfoAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default);

    Task<PracticeQuestionDto> GetPracticeQuestionAsync(int sessionId, int displayOrder, string bearerToken, CancellationToken cancellationToken = default);

    Task<RevealHintResponse> RevealPracticeHintAsync(int sessionId, int questionId, string bearerToken, CancellationToken cancellationToken = default);

    Task<GradedAnswerDto> SubmitPracticeAnswerAsync(int sessionId, int questionId, SubmitPracticeAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default);

    Task<PracticeSummaryDto> GetPracticeSummaryAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default);

    Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, string bearerToken, CancellationToken cancellationToken = default);

    Task<SavedQuestionItemDto> GetSavedQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default);

    Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, string bearerToken, CancellationToken cancellationToken = default);

    Task<SaveQuestionResultDto> UnsaveQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default);
}

/// <summary>Raised when the API answers with a non-success status; carries the ProblemDetails code.</summary>
public sealed class ApiException : HttpRequestException
{
    public ApiException(string code, string? title, int status, string? detail = null)
        : base($"API error {status} ({code}): {title}", null, (System.Net.HttpStatusCode)status)
    {
        Code = code;
        Title = title;
        Status = status;
        Detail = detail;
    }

    public string Code { get; }

    public new string? Title { get; }

    /// <summary>The human-readable explanation (e.g. which validation rule failed), when the API gave one.</summary>
    public string? Detail { get; }

    public int Status { get; }
}

public sealed class CompanionApiClient(HttpClient http) : ICompanionApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CertificateDto>> GetCertificatesAsync(CancellationToken cancellationToken = default)
    {
        // The certificate catalog is anonymous — no bearer to forward here.
        var certificates = await http.GetFromJsonAsync<List<CertificateDto>>("api/certificates", Json, cancellationToken);
        return certificates ?? [];
    }

    public async Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/auth/register", request, Json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AuthTokenResponse>(Json, cancellationToken))!;
    }

    public async Task<AuthTokenResponse> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/auth/sign-in", request, Json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AuthTokenResponse>(Json, cancellationToken))!;
    }

    public async Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/auth/verify-email", request, Json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ResendActivationAsync(string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/resend-activation");
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/auth/forgot-password", request, Json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/auth/reset-password", request, Json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<AuthUserDto> GetMeAsync(string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AuthUserDto>(Json, cancellationToken))!;
    }

    public async Task<ProfileDto> GetProfileAsync(string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/profile");
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ProfileDto>(Json, cancellationToken))!;
    }

    public async Task<ProfileDto> UpdateProfileAsync(UpdateProfileRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, "api/profile")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ProfileDto>(Json, cancellationToken))!;
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/profile/password")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<LearnerContextDto> GetLearnerContextAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LearnerContextDto>("api/learner/context", bearerToken, cancellationToken);

    public async Task SwitchCertificateAsync(string bearerToken, string code, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "api/learner/active-certificate")
        {
            Content = JsonContent.Create(new SwitchCertificateRequest(code), options: Json),
        };
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<LearnerDashboardDto> GetDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LearnerDashboardDto>("api/learner/dashboard", bearerToken, cancellationToken);

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

    private async Task<T> GetAuthorizedAsync<T>(string path, string bearerToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    private static void AttachBearer(HttpRequestMessage request, string? bearerToken)
    {
        if (!string.IsNullOrEmpty(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // The API answers failures with RFC 9457 ProblemDetails plus a stable "code" extension.
        // Fall back to the generic code when the body is missing or not the expected shape.
        var code = ApiError.Codes.Unexpected;
        string? title = response.ReasonPhrase;
        string? detail = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>(Json, cancellationToken);
            if (problem is not null)
            {
                if (!string.IsNullOrEmpty(problem.Code))
                {
                    code = problem.Code;
                }

                title = problem.Title ?? title;
                detail = problem.Detail;
            }
        }
        catch (JsonException)
        {
            // Body wasn't ProblemDetails JSON; keep the fallback code.
        }

        throw new ApiException(code, title, (int)response.StatusCode, detail);
    }

    private sealed class ProblemDetailsBody
    {
        public string? Title { get; set; }

        public string? Detail { get; set; }

        public int? Status { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }
}
