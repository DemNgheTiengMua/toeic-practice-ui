using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>
/// Composite companion API client aggregating all segregated domain client interfaces.
/// Identity forwarding is handled transparently by the HTTP message handler pipeline (ADR-0002).
/// </summary>
public interface ICompanionApiClient :
    ICertificatesClient,
    IAuthClient,
    IProfileClient,
    ILearnerContextClient,
    IPracticeClient,
    ISavedQuestionsClient,
    IWeaknessAnalysisClient,
    IOrdersClient,
    IWalletClient,
    IMockExamsClient,
    IWritingClient,
    ISpeakingClient,
    IPlacementClient,
    ILearningPathClient,
    ILessonClient,
    IMentorsClient,
    ISupportClient,
    IAiInsightsClient,
    IMediaClient,
    IAdminPackagesClient,
    IAdminScoreConversionClient,
    IAdminOrdersClient,
    IAdminUsersClient,
    IAdminDashboardClient,
    IAdminMentorsClient,
    IAdminComplaintsClient,
    IAdminExamsClient,
    IAdminImportsClient,
    IAdminLessonsClient
{
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

    public string? Title { get; }

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

    public Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<WeaknessAnalysisDto>("api/learner/weakness-analysis", bearerToken, cancellationToken);

    public async Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/recommendations/{recId}/resolve");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResolveRecommendationResultDto>(Json, cancellationToken))!;
    }

    public async Task<IReadOnlyList<PackageDto>> GetPublicPackagesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/packages", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<PackageDto>>(Json, cancellationToken))!;
    }

    public Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminPackagesSummaryDto>("api/admin/packages", bearerToken, cancellationToken);

    public Task<PackageDto> GetAdminPackageAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PackageDto>($"api/admin/packages/{packageId}", bearerToken, cancellationToken);

    public async Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/admin/packages")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PackageDto>(Json, cancellationToken))!;
    }

    public async Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"api/admin/packages/{packageId}")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PackageDto>(Json, cancellationToken))!;
    }

    public async Task DeactivatePackageAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/packages/{packageId}/deactivate");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<CheckoutPreviewDto>($"api/checkout/preview?packageId={packageId}", bearerToken, cancellationToken);

    public async Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/checkout/orders")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CreateOrderResponse>(Json, cancellationToken))!;
    }

    public async Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/checkout/return")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PaymentResultDto>(Json, cancellationToken))!;
    }

    public Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PaymentResultDto>($"api/checkout/orders/{Uri.EscapeDataString(orderCode)}/result", bearerToken, cancellationToken);

    public async Task CancelOrderAsync(CancelOrderRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/checkout/cancel")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/mock-gateway/action")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PaymentResultDto>(Json, cancellationToken))!;
    }

    public Task<WalletDto> GetWalletAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<WalletDto>("api/learner/wallet", bearerToken, cancellationToken);

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

    public async Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/attempts/{attemptId}/submit");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SubmitAttemptResponse>(Json, cancellationToken))!;
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

    public Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(certCode)) query.Add($"certCode={Uri.EscapeDataString(certCode)}");
        if (examId.HasValue) query.Add($"examId={examId.Value}");
        var path = query.Count > 0 ? $"api/admin/score-conversion?{string.Join("&", query)}" : "api/admin/score-conversion";
        return GetAuthorizedAsync<AdminScoreConversionDto>(path, bearerToken, cancellationToken);
    }

    public async Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"api/admin/score-conversion/{certId}")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminScoreConversionDto>(Json, cancellationToken))!;
    }

    public Task<AdminOrdersSummaryDto> GetAdminOrdersAsync(string? q, string? status, string? gateway, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(gateway)) query.Add($"gateway={Uri.EscapeDataString(gateway)}");
        var path = $"api/admin/orders?{string.Join("&", query)}";
        return GetAuthorizedAsync<AdminOrdersSummaryDto>(path, bearerToken, cancellationToken);
    }

    public Task<AdminOrderDetailDto> GetAdminOrderDetailAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminOrderDetailDto>($"api/admin/orders/{orderId}", bearerToken, cancellationToken);

    public async Task<PaymentResultDto> ReconcileAdminOrderAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/orders/{orderId}/reconcile");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PaymentResultDto>(Json, cancellationToken))!;
    }

    public Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        var path = $"api/admin/users?{string.Join("&", query)}";
        return GetAuthorizedAsync<AdminUsersSummaryDto>(path, bearerToken, cancellationToken);
    }

    public async Task<AdminUserDto> LockUserAsync(int userId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/users/{userId}/lock");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminUserDto>(Json, cancellationToken))!;
    }

    public async Task<AdminUserDto> UnlockUserAsync(int userId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/users/{userId}/unlock");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminUserDto>(Json, cancellationToken))!;
    }

    public async Task ResendUserActivationAsync(int userId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/users/{userId}/resend-activation");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<AdjustCreditsResultDto> AdjustUserCreditsAsync(int userId, AdjustCreditsRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/users/{userId}/adjust-credits")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdjustCreditsResultDto>(Json, cancellationToken))!;
    }

    public Task<AdminDashboardDto> GetAdminDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminDashboardDto>("api/admin/dashboard", bearerToken, cancellationToken);

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

    public Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AiInsightDto>($"api/learner/insights/practice/{sessionId}", bearerToken, cancellationToken);

    public async Task RetryPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/insights/practice/{sessionId}/retry");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AiInsightDto> GetExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AiInsightDto>($"api/learner/insights/exam/{attemptId}", bearerToken, cancellationToken);

    public async Task RetryExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/insights/exam/{attemptId}/retry");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RetryInsightAsync(int insightId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/insights/{insightId}/retry");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AiInsightDto> GetDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default)
    {
        var path = certId.HasValue ? $"api/learner/insights/dashboard?certId={certId.Value}" : "api/learner/insights/dashboard";
        return GetAuthorizedAsync<AiInsightDto>(path, bearerToken, cancellationToken);
    }

    public async Task RetryDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default)
    {
        var path = certId.HasValue ? $"api/learner/insights/dashboard/retry?certId={certId.Value}" : "api/learner/insights/dashboard/retry";
        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AiInsightDto> GetPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AiInsightDto>("api/learner/insights/path", bearerToken, cancellationToken);

    public async Task RetryPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/learner/insights/path/retry");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

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

    public Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PlacementCatalogViewDto>("api/learner/placement", bearerToken, cancellationToken);

    public Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<StartPlacementTestRequest, PlacementTestInfoDto>("api/learner/placement/start", request, bearerToken, cancellationToken);

    public Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, string bearerToken, CancellationToken cancellationToken = default)
    {
        var url = questionDisplayOrder.HasValue
            ? $"api/learner/placement/take/{placementId}?q={questionDisplayOrder.Value}"
            : $"api/learner/placement/take/{placementId}";
        return GetAuthorizedAsync<PlacementTakeViewDto>(url, bearerToken, cancellationToken);
    }

    public Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SaveAnswerRequest, SaveAnswerResultDto>($"api/learner/placement/take/{placementId}/answers/{questionId}", request, bearerToken, cancellationToken);

    public Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SubmitPlacementTestRequest, PlacementResultViewDto>($"api/learner/placement/take/{placementId}/submit", request, bearerToken, cancellationToken);

    public Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PlacementResultViewDto>($"api/learner/placement/result/{placementId}", bearerToken, cancellationToken);

    public Task<TargetBandViewDto> GetTargetBandViewAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<TargetBandViewDto>("api/learner/target-band", bearerToken, cancellationToken);

    public Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SetTargetBandRequest, SetTargetBandResultDto>("api/learner/target-band", request, bearerToken, cancellationToken);

    public Task<LearningPathSummaryDto> GetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LearningPathSummaryDto>("api/learner/learning-path", bearerToken, cancellationToken);

    public Task<LearningPathSummaryDto> GenerateLearningPathAsync(GenerateLearningPathRequest? request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<GenerateLearningPathRequest, LearningPathSummaryDto>("api/learner/learning-path/generate", request ?? new GenerateLearningPathRequest(), bearerToken, cancellationToken);

    public Task<LearningPathModuleDetailDto> GetLearningPathModuleAsync(int moduleId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LearningPathModuleDetailDto>($"api/learner/learning-path/modules/{moduleId}", bearerToken, cancellationToken);

    public Task<StartStepResultDto> StartLearningPathStepAsync(int stepId, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<object, StartStepResultDto>($"api/learner/learning-path/steps/{stepId}/start", new { }, bearerToken, cancellationToken);

    public Task<ResetLearningPathResultDto> ResetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<object, ResetLearningPathResultDto>("api/learner/learning-path/reset", new { }, bearerToken, cancellationToken);

    public Task<LearningPathSummaryDto> UpgradeLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<object, LearningPathSummaryDto>("api/learner/learning-path/upgrade", new { }, bearerToken, cancellationToken);

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

    public async Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(certCode)) query.Add($"certCode={Uri.EscapeDataString(certCode)}");
        if (!string.IsNullOrWhiteSpace(skill)) query.Add($"skill={Uri.EscapeDataString(skill)}");
        var uri = "api/mentors" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        using var response = await http.GetAsync(uri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<MentorSummaryDto>>(Json, cancellationToken))!;
    }

    public async Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/mentors/{mentorId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MentorDetailDto>(Json, cancellationToken))!;
    }

    public Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<AdminMentorListItemDto>>("api/admin/mentors", bearerToken, cancellationToken);

    public Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<MentorSummaryDto>($"api/admin/mentors/{mentorId}", bearerToken, cancellationToken);

    public Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<CreateMentorProfileRequest, MentorSummaryDto>("api/admin/mentors", request, bearerToken, cancellationToken);

    public async Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"api/admin/mentors/{mentorId}")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MentorSummaryDto>(Json, cancellationToken))!;
    }

    public Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<AdminMentorSlotDto>>($"api/admin/mentors/{mentorId}/slots", bearerToken, cancellationToken);

    public Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, CreateMentorSlotRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<CreateMentorSlotRequest, AdminMentorSlotDto>($"api/admin/mentors/{mentorId}/slots", request, bearerToken, cancellationToken);

    public async Task CloseMentorSlotAsync(int mentorId, int slotId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/mentors/{mentorId}/slots/{slotId}/close");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<MentorCertificateOptionDto>>("api/admin/mentors/options", bearerToken, cancellationToken);

    public Task<MentorBookingDto> BookMentorAsync(int mentorId, BookMentorRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<BookMentorRequest, MentorBookingDto>($"api/mentors/{mentorId}/book", request, bearerToken, cancellationToken);

    public Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<object, CancelBookingResultDto>($"api/mentors/bookings/{bookingId}/cancel", new { }, bearerToken, cancellationToken);

    public Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<MentorBookingDto>($"api/mentors/bookings/{bookingId}", bearerToken, cancellationToken);

    public Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<MentorBookingDto>>("api/mentors/bookings", bearerToken, cancellationToken);

    public Task<MentorReviewDto> RateBookingAsync(int bookingId, RateMentorRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<RateMentorRequest, MentorReviewDto>($"api/mentors/bookings/{bookingId}/rate", request, bearerToken, cancellationToken);

    public Task<MentorReviewDto> RateBookingAsync(int bookingId, RateMentorRequest request, CancellationToken cancellationToken = default) =>
        RateBookingAsync(bookingId, request, string.Empty, cancellationToken);

    public Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<LearnerComplaintDto>>("api/learner/complaints", bearerToken, cancellationToken);

    public Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<EligibleSubjectsDto>("api/learner/complaints/eligible-subjects", bearerToken, cancellationToken);

    public Task<LearnerComplaintDto> FileComplaintAsync(FileComplaintRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<FileComplaintRequest, LearnerComplaintDto>("api/learner/complaints", request, bearerToken, cancellationToken);

    public Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        var path = $"api/admin/complaints?{string.Join("&", query)}";
        return GetAuthorizedAsync<AdminComplaintsSummaryDto>(path, bearerToken, cancellationToken);
    }

    public Task<AdminComplaintDetailDto> GetAdminComplaintDetailAsync(int complaintId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminComplaintDetailDto>($"api/admin/complaints/{complaintId}", bearerToken, cancellationToken);

    public Task<AdminComplaintDetailDto> SetAdminComplaintStatusAsync(int complaintId, UpdateComplaintStatusRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<UpdateComplaintStatusRequest, AdminComplaintDetailDto>($"api/admin/complaints/{complaintId}/status", request, bearerToken, cancellationToken);

    public Task<AdminComplaintDetailDto> ResolveAdminComplaintAsync(int complaintId, ResolveComplaintRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<ResolveComplaintRequest, AdminComplaintDetailDto>($"api/admin/complaints/{complaintId}/resolve", request, bearerToken, cancellationToken);

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

    public Task<AdminImportBatchesDto> GetAdminImportBatchesAsync(int? certId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminImportBatchesDto>(certId is { } id ? $"api/admin/imports?certId={id}" : "api/admin/imports", bearerToken, cancellationToken);

    public Task<AdminImportBatchDetailDto> GetAdminImportBatchAsync(int batchId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminImportBatchDetailDto>($"api/admin/imports/{batchId}", bearerToken, cancellationToken);

    public async Task<AdminImportResultDto> UploadAdminImportAsync(int certId, Stream content, string fileName, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(certId.ToString()), "certId");
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/admin/imports") { Content = form };
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminImportResultDto>(Json, cancellationToken))!;
    }

    public async Task<(Stream Stream, string ContentType)> GetImportMediaAsync(int batchId, string fileName, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/media/imports/{batchId}/{Uri.EscapeDataString(fileName)}");
        AttachBearer(request, bearerToken);

        // The response stays open for the caller to stream; it is disposed only if it is a failure.
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
        return (stream, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    public async Task<(Stream Stream, string ContentType)> GetExamMediaAsync(int examId, string fileName, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/media/exams/{examId}/{Uri.EscapeDataString(fileName)}");
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
        return (stream, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

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



    // Token-free forwarding methods for segregated domain client interfaces (ADR-0002)
    public Task<AuthUserDto> GetMeAsync(CancellationToken cancellationToken = default) =>
        GetMeAsync(string.Empty, cancellationToken);

    public Task ResendActivationAsync(CancellationToken cancellationToken = default) =>
        ResendActivationAsync(string.Empty, cancellationToken);

    public Task<ProfileDto> GetProfileAsync(CancellationToken cancellationToken = default) =>
        GetProfileAsync(string.Empty, cancellationToken);

    public Task<ProfileDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default) =>
        UpdateProfileAsync(request, string.Empty, cancellationToken);

    public Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        ChangePasswordAsync(request, string.Empty, cancellationToken);

    public Task<LearnerContextDto> GetLearnerContextAsync(CancellationToken cancellationToken = default) =>
        GetLearnerContextAsync(string.Empty, cancellationToken);

    public Task SwitchCertificateAsync(string code, CancellationToken cancellationToken = default) =>
        SwitchCertificateAsync(string.Empty, code, cancellationToken);

    public Task<LearnerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) =>
        GetDashboardAsync(string.Empty, cancellationToken);

    public Task<PracticeCatalogDto> GetPracticeCatalogAsync(CancellationToken cancellationToken = default) =>
        GetPracticeCatalogAsync(string.Empty, cancellationToken);

    public Task<PracticeSessionDto> StartPracticeSessionAsync(StartPracticeRequest request, CancellationToken cancellationToken = default) =>
        StartPracticeSessionAsync(request, string.Empty, cancellationToken);

    public Task<PracticeSessionDto> GetPracticeSessionInfoAsync(int sessionId, CancellationToken cancellationToken = default) =>
        GetPracticeSessionInfoAsync(sessionId, string.Empty, cancellationToken);

    public Task<PracticeQuestionDto> GetPracticeQuestionAsync(int sessionId, int displayOrder, CancellationToken cancellationToken = default) =>
        GetPracticeQuestionAsync(sessionId, displayOrder, string.Empty, cancellationToken);

    public Task<RevealHintResponse> RevealPracticeHintAsync(int sessionId, int questionId, CancellationToken cancellationToken = default) =>
        RevealPracticeHintAsync(sessionId, questionId, string.Empty, cancellationToken);

    public Task<GradedAnswerDto> SubmitPracticeAnswerAsync(int sessionId, int questionId, SubmitPracticeAnswerRequest request, CancellationToken cancellationToken = default) =>
        SubmitPracticeAnswerAsync(sessionId, questionId, request, string.Empty, cancellationToken);

    public Task<PracticeSummaryDto> GetPracticeSummaryAsync(int sessionId, CancellationToken cancellationToken = default) =>
        GetPracticeSummaryAsync(sessionId, string.Empty, cancellationToken);

    public Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, CancellationToken cancellationToken = default) =>
        GetSavedQuestionsAsync(source, sectionId, categoryId, string.Empty, cancellationToken);

    public Task<SavedQuestionItemDto> GetSavedQuestionAsync(int questionId, CancellationToken cancellationToken = default) =>
        GetSavedQuestionAsync(questionId, string.Empty, cancellationToken);

    public Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, CancellationToken cancellationToken = default) =>
        SaveQuestionAsync(questionId, source, string.Empty, cancellationToken);

    public Task<SaveQuestionResultDto> UnsaveQuestionAsync(int questionId, CancellationToken cancellationToken = default) =>
        UnsaveQuestionAsync(questionId, string.Empty, cancellationToken);

    public Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(CancellationToken cancellationToken = default) =>
        GetWeaknessAnalysisAsync(string.Empty, cancellationToken);

    public Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, CancellationToken cancellationToken = default) =>
        ResolveRecommendationAsync(recId, string.Empty, cancellationToken);

    public Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, CancellationToken cancellationToken = default) =>
        GetCheckoutPreviewAsync(packageId, string.Empty, cancellationToken);

    public Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default) =>
        CreateOrderAsync(request, string.Empty, cancellationToken);

    public Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, CancellationToken cancellationToken = default) =>
        HandleReturnAsync(request, string.Empty, cancellationToken);

    public Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, CancellationToken cancellationToken = default) =>
        GetPaymentResultAsync(orderCode, string.Empty, cancellationToken);

    public Task CancelOrderAsync(CancelOrderRequest request, CancellationToken cancellationToken = default) =>
        CancelOrderAsync(request, string.Empty, cancellationToken);

    public Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, CancellationToken cancellationToken = default) =>
        ApplyMockGatewayActionAsync(request, string.Empty, cancellationToken);

    public Task<WalletDto> GetWalletAsync(CancellationToken cancellationToken = default) =>
        GetWalletAsync(string.Empty, cancellationToken);

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

    public Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, CancellationToken cancellationToken = default) =>
        SubmitAttemptAsync(attemptId, string.Empty, cancellationToken);

    public Task CancelAttemptAsync(int attemptId, CancellationToken cancellationToken = default) =>
        CancelAttemptAsync(attemptId, string.Empty, cancellationToken);

    public Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetAttemptResultAsync(attemptId, string.Empty, cancellationToken);

    public Task<ExamHistoryDto> GetExamHistoryAsync(CancellationToken cancellationToken = default) =>
        GetExamHistoryAsync(string.Empty, cancellationToken);

    public Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetAttemptReviewAsync(attemptId, string.Empty, cancellationToken);

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

    public Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(CancellationToken cancellationToken = default) =>
        GetPlacementCatalogAsync(string.Empty, cancellationToken);

    public Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, CancellationToken cancellationToken = default) =>
        StartPlacementTestAsync(request, string.Empty, cancellationToken);

    public Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default) =>
        GetPlacementTakeAsync(placementId, questionDisplayOrder, string.Empty, cancellationToken);

    public Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default) =>
        SavePlacementAnswerAsync(placementId, questionId, request, string.Empty, cancellationToken);

    public Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, CancellationToken cancellationToken = default) =>
        SubmitPlacementTestAsync(placementId, request, string.Empty, cancellationToken);

    public Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, CancellationToken cancellationToken = default) =>
        GetPlacementResultAsync(placementId, string.Empty, cancellationToken);

    public Task<TargetBandViewDto> GetTargetBandViewAsync(CancellationToken cancellationToken = default) =>
        GetTargetBandViewAsync(string.Empty, cancellationToken);

    public Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, CancellationToken cancellationToken = default) =>
        SetTargetBandAsync(request, string.Empty, cancellationToken);

    public Task<LearningPathSummaryDto> GetLearningPathAsync(CancellationToken cancellationToken = default) =>
        GetLearningPathAsync(string.Empty, cancellationToken);

    public Task<LearningPathSummaryDto> GenerateLearningPathAsync(GenerateLearningPathRequest? request, CancellationToken cancellationToken = default) =>
        GenerateLearningPathAsync(request, string.Empty, cancellationToken);

    public Task<LearningPathModuleDetailDto> GetLearningPathModuleAsync(int moduleId, CancellationToken cancellationToken = default) =>
        GetLearningPathModuleAsync(moduleId, string.Empty, cancellationToken);

    public Task<StartStepResultDto> StartLearningPathStepAsync(int stepId, CancellationToken cancellationToken = default) =>
        StartLearningPathStepAsync(stepId, string.Empty, cancellationToken);

    public Task<ResetLearningPathResultDto> ResetLearningPathAsync(CancellationToken cancellationToken = default) =>
        ResetLearningPathAsync(string.Empty, cancellationToken);

    public Task<LearningPathSummaryDto> UpgradeLearningPathAsync(CancellationToken cancellationToken = default) =>
        UpgradeLearningPathAsync(string.Empty, cancellationToken);

    public Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(CancellationToken cancellationToken = default) =>
        GetMentorCertificateOptionsAsync(string.Empty, cancellationToken);

    public Task<MentorBookingDto> BookMentorAsync(int mentorId, BookMentorRequest request, CancellationToken cancellationToken = default) =>
        BookMentorAsync(mentorId, request, string.Empty, cancellationToken);

    public Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
        CancelBookingAsync(bookingId, string.Empty, cancellationToken);

    public Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, CancellationToken cancellationToken = default) =>
        GetBookingDetailAsync(bookingId, string.Empty, cancellationToken);

    public Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(CancellationToken cancellationToken = default) =>
        GetLearnerBookingsAsync(string.Empty, cancellationToken);

    public Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(CancellationToken cancellationToken = default) =>
        GetLearnerComplaintsAsync(string.Empty, cancellationToken);

    public Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(CancellationToken cancellationToken = default) =>
        GetEligibleSubjectsAsync(string.Empty, cancellationToken);

    public Task<LearnerComplaintDto> FileComplaintAsync(FileComplaintRequest request, CancellationToken cancellationToken = default) =>
        FileComplaintAsync(request, string.Empty, cancellationToken);

    public Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default) =>
        GetPracticeInsightAsync(sessionId, string.Empty, cancellationToken);

    public Task RetryPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default) =>
        RetryPracticeInsightAsync(sessionId, string.Empty, cancellationToken);

    public Task<AiInsightDto> GetExamInsightAsync(int attemptId, CancellationToken cancellationToken = default) =>
        GetExamInsightAsync(attemptId, string.Empty, cancellationToken);

    public Task RetryExamInsightAsync(int attemptId, CancellationToken cancellationToken = default) =>
        RetryExamInsightAsync(attemptId, string.Empty, cancellationToken);

    public Task RetryInsightAsync(int insightId, CancellationToken cancellationToken = default) =>
        RetryInsightAsync(insightId, string.Empty, cancellationToken);

    public Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default) =>
        GetDashboardInsightAsync(string.Empty, certId, cancellationToken);

    public Task RetryDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default) =>
        RetryDashboardInsightAsync(string.Empty, certId, cancellationToken);

    public Task<AiInsightDto> GetPathInsightAsync(CancellationToken cancellationToken = default) =>
        GetPathInsightAsync(string.Empty, cancellationToken);

    public Task RetryPathInsightAsync(CancellationToken cancellationToken = default) =>
        RetryPathInsightAsync(string.Empty, cancellationToken);

    public Task<(Stream Stream, string ContentType)> GetImportMediaAsync(int batchId, string fileName, CancellationToken cancellationToken = default) =>
        GetImportMediaAsync(batchId, fileName, string.Empty, cancellationToken);

    public Task<(Stream Stream, string ContentType)> GetExamMediaAsync(int examId, string fileName, CancellationToken cancellationToken = default) =>
        GetExamMediaAsync(examId, fileName, string.Empty, cancellationToken);

    public Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(CancellationToken cancellationToken = default) =>
        GetAdminPackagesAsync(string.Empty, cancellationToken);

    public Task<PackageDto> GetAdminPackageAsync(int packageId, CancellationToken cancellationToken = default) =>
        GetAdminPackageAsync(packageId, string.Empty, cancellationToken);

    public Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, CancellationToken cancellationToken = default) =>
        CreatePackageAsync(request, string.Empty, cancellationToken);

    public Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, CancellationToken cancellationToken = default) =>
        UpdatePackageAsync(packageId, request, string.Empty, cancellationToken);

    public Task DeactivatePackageAsync(int packageId, CancellationToken cancellationToken = default) =>
        DeactivatePackageAsync(packageId, string.Empty, cancellationToken);

    public Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, CancellationToken cancellationToken = default) =>
        GetAdminScoreConversionAsync(certCode, examId, string.Empty, cancellationToken);

    public Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, CancellationToken cancellationToken = default) =>
        UpdateAdminScoreConversionAsync(certId, request, string.Empty, cancellationToken);

    public Task<AdminOrdersSummaryDto> GetAdminOrdersAsync(string? q, string? status, string? gateway, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminOrdersAsync(q, status, gateway, page, pageSize, string.Empty, cancellationToken);

    public Task<AdminOrderDetailDto> GetAdminOrderDetailAsync(int orderId, CancellationToken cancellationToken = default) =>
        GetAdminOrderDetailAsync(orderId, string.Empty, cancellationToken);

    public Task<PaymentResultDto> ReconcileAdminOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        ReconcileAdminOrderAsync(orderId, string.Empty, cancellationToken);

    public Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminUsersAsync(q, status, page, pageSize, string.Empty, cancellationToken);

    public Task<AdminUserDto> LockUserAsync(int userId, CancellationToken cancellationToken = default) =>
        LockUserAsync(userId, string.Empty, cancellationToken);

    public Task<AdminUserDto> UnlockUserAsync(int userId, CancellationToken cancellationToken = default) =>
        UnlockUserAsync(userId, string.Empty, cancellationToken);

    public Task ResendUserActivationAsync(int userId, CancellationToken cancellationToken = default) =>
        ResendUserActivationAsync(userId, string.Empty, cancellationToken);

    public Task<AdjustCreditsResultDto> AdjustUserCreditsAsync(int userId, AdjustCreditsRequest request, CancellationToken cancellationToken = default) =>
        AdjustUserCreditsAsync(userId, request, string.Empty, cancellationToken);

    public Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default) =>
        GetAdminDashboardAsync(string.Empty, cancellationToken);

    public Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(CancellationToken cancellationToken = default) =>
        GetAdminMentorsAsync(string.Empty, cancellationToken);

    public Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, CancellationToken cancellationToken = default) =>
        GetAdminMentorByIdAsync(mentorId, string.Empty, cancellationToken);

    public Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, CancellationToken cancellationToken = default) =>
        CreateMentorAsync(request, string.Empty, cancellationToken);

    public Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, CancellationToken cancellationToken = default) =>
        UpdateMentorAsync(mentorId, request, string.Empty, cancellationToken);

    public Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, CancellationToken cancellationToken = default) =>
        GetAdminMentorSlotsAsync(mentorId, string.Empty, cancellationToken);

    public Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, CreateMentorSlotRequest request, CancellationToken cancellationToken = default) =>
        CreateMentorSlotAsync(mentorId, request, string.Empty, cancellationToken);

    public Task CloseMentorSlotAsync(int mentorId, int slotId, CancellationToken cancellationToken = default) =>
        CloseMentorSlotAsync(mentorId, slotId, string.Empty, cancellationToken);

    public Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminComplaintsAsync(q, status, page, pageSize, string.Empty, cancellationToken);

    public Task<AdminComplaintDetailDto> GetAdminComplaintDetailAsync(int complaintId, CancellationToken cancellationToken = default) =>
        GetAdminComplaintDetailAsync(complaintId, string.Empty, cancellationToken);

    public Task<AdminComplaintDetailDto> SetAdminComplaintStatusAsync(int complaintId, UpdateComplaintStatusRequest request, CancellationToken cancellationToken = default) =>
        SetAdminComplaintStatusAsync(complaintId, request, string.Empty, cancellationToken);

    public Task<AdminComplaintDetailDto> ResolveAdminComplaintAsync(int complaintId, ResolveComplaintRequest request, CancellationToken cancellationToken = default) =>
        ResolveAdminComplaintAsync(complaintId, request, string.Empty, cancellationToken);

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

    public Task<AdminImportBatchesDto> GetAdminImportBatchesAsync(int? certId, CancellationToken cancellationToken = default) =>
        GetAdminImportBatchesAsync(certId, string.Empty, cancellationToken);

    public Task<AdminImportBatchDetailDto> GetAdminImportBatchAsync(int batchId, CancellationToken cancellationToken = default) =>
        GetAdminImportBatchAsync(batchId, string.Empty, cancellationToken);

    public Task<AdminImportResultDto> UploadAdminImportAsync(int certId, Stream content, string fileName, CancellationToken cancellationToken = default) =>
        UploadAdminImportAsync(certId, content, fileName, string.Empty, cancellationToken);

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


    private async Task<TResponse> PutAuthorizedAsync<TRequest, TResponse>(string path, TRequest body, string bearerToken, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, path);
        AttachBearer(message, bearerToken);
        message.Content = JsonContent.Create(body, options: Json);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<TResponse>(Json, cancellationToken))!;
    }

    private async Task DeleteAuthorizedAsync(string path, string bearerToken, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Delete, path);
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }


    private async Task<T> GetAuthorizedAsync<T>(string path, string bearerToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    private async Task<TResponse> PostAuthorizedAsync<TRequest, TResponse>(string path, TRequest body, string bearerToken, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        AttachBearer(message, bearerToken);
        message.Content = JsonContent.Create(body, options: Json);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<TResponse>(Json, cancellationToken))!;
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
