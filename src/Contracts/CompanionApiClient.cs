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
    Task<IReadOnlyList<CertificateDto>> GetCertificatesAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AuthTokenResponse> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AuthUserDto> GetMeAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<ProfileDto> GetProfileAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<ProfileDto> UpdateProfileAsync(UpdateProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task ChangePasswordAsync(ChangePasswordRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task ResendActivationAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<LearnerContextDto> GetLearnerContextAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task SwitchCertificateAsync(string bearerToken, string code, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<LearnerDashboardDto> GetDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PracticeCatalogDto> GetPracticeCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PracticeSessionDto> StartPracticeSessionAsync(StartPracticeRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PracticeSessionDto> GetPracticeSessionInfoAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PracticeQuestionDto> GetPracticeQuestionAsync(int sessionId, int displayOrder, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<RevealHintResponse> RevealPracticeHintAsync(int sessionId, int questionId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<GradedAnswerDto> SubmitPracticeAnswerAsync(int sessionId, int questionId, SubmitPracticeAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PracticeSummaryDto> GetPracticeSummaryAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SavedQuestionItemDto> GetSavedQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SaveQuestionResultDto> UnsaveQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<IReadOnlyList<PackageDto>> GetPublicPackagesAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PackageDto> GetAdminPackageAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task DeactivatePackageAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task CancelOrderAsync(CancelOrderRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<WalletDto> GetWalletAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<MockExamListViewDto> GetMockExamsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<StartMockExamResponse> StartMockExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SaveAnswerResultDto> SaveAttemptAnswerAsync(int attemptId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task CancelAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<ExamHistoryDto> GetExamHistoryAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AdminOrdersSummaryDto> GetAdminOrdersAsync(string? q, string? status, string? gateway, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AdminOrderDetailDto> GetAdminOrderDetailAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PaymentResultDto> ReconcileAdminOrderAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AdminUserDto> LockUserAsync(int userId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AdminUserDto> UnlockUserAsync(int userId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task ResendUserActivationAsync(int userId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AdjustCreditsResultDto> AdjustUserCreditsAsync(int userId, AdjustCreditsRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<WritingInstructionsDto> GetWritingInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<StartWritingAttemptResponse> StartWritingAttemptAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<WritingTakeDto> GetWritingTakeAsync(int attemptId, int? questionId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SaveWritingDraftResponse> SaveWritingDraftAsync(int attemptId, SaveWritingDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SubmitWritingResponse> SubmitWritingAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<WritingGradingPendingDto> GetWritingGradingPendingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task RetryWritingGradingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<WritingReviewDto> GetWritingReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task RetryPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AiInsightDto> GetExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task RetryExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task RetryInsightAsync(int insightId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AiInsightDto> GetDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task RetryDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AiInsightDto> GetPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task RetryPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SpeakingInstructionsDto> GetSpeakingInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<StartSpeakingAttemptResponse> StartSpeakingAttemptAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SpeakingTakeDto> GetSpeakingTakeAsync(int attemptId, int? questionId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<UploadSpeakingAudioResponse> UploadSpeakingAudioAsync(int attemptId, int questionId, Stream audioStream, string fileName, string contentType, int? durationMs, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SubmitSpeakingResponse> SubmitSpeakingAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SpeakingGradingPendingDto> GetSpeakingGradingPendingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task RetrySpeakingGradingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SpeakingReviewDto> GetSpeakingReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<(Stream Stream, string ContentType, string FileName)> GetSpeakingAudioStreamAsync(int attemptId, int questionId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<TargetBandViewDto> GetTargetBandViewAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<LearningPathSummaryDto> GetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<LearningPathSummaryDto> GenerateLearningPathAsync(GenerateLearningPathRequest? request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<LearningPathModuleDetailDto> GetLearningPathModuleAsync(int moduleId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<StartStepResultDto> StartLearningPathStepAsync(int stepId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<ResetLearningPathResultDto> ResetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<LearningPathSummaryDto> UpgradeLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, CreateMentorSlotRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    Task CloseMentorSlotAsync(int mentorId, int slotId, string bearerToken, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
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
