namespace Contracts;

public interface ICertificatesClient
{
    Task<IReadOnlyList<CertificateDto>> GetCertificatesAsync(CancellationToken cancellationToken = default);
}

public interface IAuthClient
{
    Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthTokenResponse> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);
    Task<AuthUserDto> GetMeAsync(CancellationToken cancellationToken = default);
    Task<AuthUserDto> GetMeAsync(string bearerToken, CancellationToken cancellationToken = default) => GetMeAsync(cancellationToken);
    Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default);
    Task ResendActivationAsync(CancellationToken cancellationToken = default);
    Task ResendActivationAsync(string bearerToken, CancellationToken cancellationToken = default) => ResendActivationAsync(cancellationToken);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

public interface IProfileClient
{
    Task<ProfileDto> GetProfileAsync(CancellationToken cancellationToken = default);
    Task<ProfileDto> GetProfileAsync(string bearerToken, CancellationToken cancellationToken = default) => GetProfileAsync(cancellationToken);
    Task<ProfileDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default);
    Task<ProfileDto> UpdateProfileAsync(UpdateProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateProfileAsync(request, cancellationToken);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, string bearerToken, CancellationToken cancellationToken = default) => ChangePasswordAsync(request, cancellationToken);
}

public interface ILearnerContextClient
{
    Task<LearnerContextDto> GetLearnerContextAsync(CancellationToken cancellationToken = default);
    Task<LearnerContextDto> GetLearnerContextAsync(string bearerToken, CancellationToken cancellationToken = default) => GetLearnerContextAsync(cancellationToken);
    Task SwitchCertificateAsync(string code, CancellationToken cancellationToken = default);
    Task SwitchCertificateAsync(string bearerToken, string code, CancellationToken cancellationToken = default) => SwitchCertificateAsync(code, cancellationToken);
    Task<LearnerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<LearnerDashboardDto> GetDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) => GetDashboardAsync(cancellationToken);
    Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) => GetDashboardInsightAsync(certId, cancellationToken);
    Task RetryDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default);
    Task RetryDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) => RetryDashboardInsightAsync(certId, cancellationToken);
}

public interface IPracticeClient
{
    Task<PracticeCatalogDto> GetPracticeCatalogAsync(CancellationToken cancellationToken = default);
    Task<PracticeCatalogDto> GetPracticeCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPracticeCatalogAsync(cancellationToken);
    Task<PracticeSessionDto> StartPracticeSessionAsync(StartPracticeRequest request, CancellationToken cancellationToken = default);
    Task<PracticeSessionDto> StartPracticeSessionAsync(StartPracticeRequest request, string bearerToken, CancellationToken cancellationToken = default) => StartPracticeSessionAsync(request, cancellationToken);
    Task<PracticeSessionDto> GetPracticeSessionInfoAsync(int sessionId, CancellationToken cancellationToken = default);
    Task<PracticeSessionDto> GetPracticeSessionInfoAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => GetPracticeSessionInfoAsync(sessionId, cancellationToken);
    Task<PracticeQuestionDto> GetPracticeQuestionAsync(int sessionId, int displayOrder, CancellationToken cancellationToken = default);
    Task<PracticeQuestionDto> GetPracticeQuestionAsync(int sessionId, int displayOrder, string bearerToken, CancellationToken cancellationToken = default) => GetPracticeQuestionAsync(sessionId, displayOrder, cancellationToken);
    Task<IReadOnlyList<PracticeQuestionDto>> GetPracticeSessionQuestionsAsync(int sessionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PracticeQuestionDto>>([]);
    Task<IReadOnlyList<PracticeQuestionDto>> GetPracticeSessionQuestionsAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => GetPracticeSessionQuestionsAsync(sessionId, cancellationToken);
    Task<RevealHintResponse> RevealPracticeHintAsync(int sessionId, int questionId, CancellationToken cancellationToken = default);
    Task<RevealHintResponse> RevealPracticeHintAsync(int sessionId, int questionId, string bearerToken, CancellationToken cancellationToken = default) => RevealPracticeHintAsync(sessionId, questionId, cancellationToken);
    Task<GradedAnswerDto> SubmitPracticeAnswerAsync(int sessionId, int questionId, SubmitPracticeAnswerRequest request, CancellationToken cancellationToken = default);
    Task<GradedAnswerDto> SubmitPracticeAnswerAsync(int sessionId, int questionId, SubmitPracticeAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) => SubmitPracticeAnswerAsync(sessionId, questionId, request, cancellationToken);
    Task<PracticeSummaryDto> GetPracticeSummaryAsync(int sessionId, CancellationToken cancellationToken = default);
    Task<PracticeSummaryDto> GetPracticeSummaryAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => GetPracticeSummaryAsync(sessionId, cancellationToken);
    Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => GetPracticeInsightAsync(sessionId, cancellationToken);
    Task RetryPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default);
    Task RetryPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => RetryPracticeInsightAsync(sessionId, cancellationToken);
}

public interface ISavedQuestionsClient
{
    Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, CancellationToken cancellationToken = default);
    Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, string bearerToken, CancellationToken cancellationToken = default) => GetSavedQuestionsAsync(source, sectionId, categoryId, cancellationToken);
    Task<SavedQuestionItemDto> GetSavedQuestionAsync(int questionId, CancellationToken cancellationToken = default);
    Task<SavedQuestionItemDto> GetSavedQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) => GetSavedQuestionAsync(questionId, cancellationToken);
    Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, CancellationToken cancellationToken = default);
    Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, string bearerToken, CancellationToken cancellationToken = default) => SaveQuestionAsync(questionId, source, cancellationToken);
    Task<SaveQuestionResultDto> UnsaveQuestionAsync(int questionId, CancellationToken cancellationToken = default);
    Task<SaveQuestionResultDto> UnsaveQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) => UnsaveQuestionAsync(questionId, cancellationToken);
}

public interface IWeaknessAnalysisClient
{
    Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(CancellationToken cancellationToken = default);
    Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(string bearerToken, CancellationToken cancellationToken = default) => GetWeaknessAnalysisAsync(cancellationToken);
    Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, CancellationToken cancellationToken = default);
    Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, string bearerToken, CancellationToken cancellationToken = default) => ResolveRecommendationAsync(recId, cancellationToken);
    Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) => GetDashboardInsightAsync(certId, cancellationToken);
}

public interface IOrdersClient
{
    Task<IReadOnlyList<PackageDto>> GetPublicPackagesAsync(CancellationToken cancellationToken = default);
    Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, CancellationToken cancellationToken = default);
    Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) => GetCheckoutPreviewAsync(packageId, cancellationToken);
    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateOrderAsync(request, cancellationToken);
    Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, string bearerToken, CancellationToken cancellationToken = default) => HandleReturnAsync(request, cancellationToken);
    Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, CancellationToken cancellationToken = default);
    Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, string bearerToken, CancellationToken cancellationToken = default) => GetPaymentResultAsync(orderCode, cancellationToken);
    Task CancelOrderAsync(CancelOrderRequest request, CancellationToken cancellationToken = default);
    Task CancelOrderAsync(CancelOrderRequest request, string bearerToken, CancellationToken cancellationToken = default) => CancelOrderAsync(request, cancellationToken);
    Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, string bearerToken, CancellationToken cancellationToken = default) => ApplyMockGatewayActionAsync(request, cancellationToken);
}

public interface IWalletClient
{
    Task<WalletDto> GetWalletAsync(CancellationToken cancellationToken = default);
    Task<WalletDto> GetWalletAsync(string bearerToken, CancellationToken cancellationToken = default) => GetWalletAsync(cancellationToken);
}

public interface IMockExamsClient
{
    Task<MockExamListViewDto> GetMockExamsAsync(CancellationToken cancellationToken = default);
    Task<MockExamListViewDto> GetMockExamsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetMockExamsAsync(cancellationToken);
    Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, CancellationToken cancellationToken = default);
    Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetMockExamInstructionsAsync(examId, cancellationToken);
    Task<StartMockExamResponse> StartMockExamAsync(int examId, CancellationToken cancellationToken = default);
    Task<StartMockExamResponse> StartMockExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => StartMockExamAsync(examId, cancellationToken);
    Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, CancellationToken cancellationToken = default);
    Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, string bearerToken, CancellationToken cancellationToken = default) => GetAttemptTakeAsync(attemptId, q, cancellationToken);
    Task<SaveAnswerResultDto> SaveAttemptAnswerAsync(int attemptId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default);
    Task<SaveAnswerResultDto> SaveAttemptAnswerAsync(int attemptId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveAttemptAnswerAsync(attemptId, questionId, request, cancellationToken);
    Task<SaveAttemptDraftResponse> SaveAttemptDraftAsync(int attemptId, SaveAttemptDraftRequest request, CancellationToken cancellationToken = default);
    Task<SaveAttemptDraftResponse> SaveAttemptDraftAsync(int attemptId, SaveAttemptDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveAttemptDraftAsync(attemptId, request, cancellationToken);
    Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => SubmitAttemptAsync(attemptId, cancellationToken);
    Task<SubmitAttemptResponse> SubmitAttemptWithDraftsAsync(int attemptId, SubmitAttemptRequest request, CancellationToken cancellationToken = default);
    Task<SubmitAttemptResponse> SubmitAttemptWithDraftsAsync(int attemptId, SubmitAttemptRequest request, string bearerToken, CancellationToken cancellationToken = default) => SubmitAttemptWithDraftsAsync(attemptId, request, cancellationToken);
    Task CancelAttemptAsync(int attemptId, CancellationToken cancellationToken = default);
    Task CancelAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => CancelAttemptAsync(attemptId, cancellationToken);
    Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetAttemptResultAsync(attemptId, cancellationToken);
    Task<ExamHistoryDto> GetExamHistoryAsync(CancellationToken cancellationToken = default);
    Task<ExamHistoryDto> GetExamHistoryAsync(string bearerToken, CancellationToken cancellationToken = default) => GetExamHistoryAsync(cancellationToken);
    Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetAttemptReviewAsync(attemptId, cancellationToken);
    Task<AiInsightDto> GetExamInsightAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetExamInsightAsync(attemptId, cancellationToken);
    Task RetryExamInsightAsync(int attemptId, CancellationToken cancellationToken = default);
    Task RetryExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => RetryExamInsightAsync(attemptId, cancellationToken);
}

public interface IWritingClient
{
    Task<WritingInstructionsDto> GetWritingInstructionsAsync(int examId, CancellationToken cancellationToken = default);
    Task<WritingInstructionsDto> GetWritingInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetWritingInstructionsAsync(examId, cancellationToken);
    Task<StartWritingAttemptResponse> StartWritingAttemptAsync(int examId, CancellationToken cancellationToken = default);
    Task<StartWritingAttemptResponse> StartWritingAttemptAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => StartWritingAttemptAsync(examId, cancellationToken);
    Task<WritingTakeDto> GetWritingTakeAsync(int attemptId, int? questionId, CancellationToken cancellationToken = default);
    Task<WritingTakeDto> GetWritingTakeAsync(int attemptId, int? questionId, string bearerToken, CancellationToken cancellationToken = default) => GetWritingTakeAsync(attemptId, questionId, cancellationToken);
    Task<SaveWritingDraftResponse> SaveWritingDraftAsync(int attemptId, SaveWritingDraftRequest request, CancellationToken cancellationToken = default);
    Task<SaveWritingDraftResponse> SaveWritingDraftAsync(int attemptId, SaveWritingDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveWritingDraftAsync(attemptId, request, cancellationToken);
    Task<SubmitWritingResponse> SubmitWritingAttemptAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<SubmitWritingResponse> SubmitWritingAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => SubmitWritingAttemptAsync(attemptId, cancellationToken);
    Task<WritingGradingPendingDto> GetWritingGradingPendingAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<WritingGradingPendingDto> GetWritingGradingPendingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetWritingGradingPendingAsync(attemptId, cancellationToken);
    Task RetryWritingGradingAsync(int attemptId, CancellationToken cancellationToken = default);
    Task RetryWritingGradingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => RetryWritingGradingAsync(attemptId, cancellationToken);
    Task<WritingReviewDto> GetWritingReviewAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<WritingReviewDto> GetWritingReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetWritingReviewAsync(attemptId, cancellationToken);
}

public interface ISpeakingClient
{
    Task<SpeakingInstructionsDto> GetSpeakingInstructionsAsync(int examId, CancellationToken cancellationToken = default);
    Task<SpeakingInstructionsDto> GetSpeakingInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetSpeakingInstructionsAsync(examId, cancellationToken);
    Task<StartSpeakingAttemptResponse> StartSpeakingAttemptAsync(int examId, CancellationToken cancellationToken = default);
    Task<StartSpeakingAttemptResponse> StartSpeakingAttemptAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => StartSpeakingAttemptAsync(examId, cancellationToken);
    Task<SpeakingTakeDto> GetSpeakingTakeAsync(int attemptId, int? questionId, CancellationToken cancellationToken = default);
    Task<SpeakingTakeDto> GetSpeakingTakeAsync(int attemptId, int? questionId, string bearerToken, CancellationToken cancellationToken = default) => GetSpeakingTakeAsync(attemptId, questionId, cancellationToken);
    Task<UploadSpeakingAudioResponse> UploadSpeakingAudioAsync(int attemptId, int questionId, Stream audioStream, string fileName, string contentType, int? durationMs, CancellationToken cancellationToken = default);
    Task<UploadSpeakingAudioResponse> UploadSpeakingAudioAsync(int attemptId, int questionId, Stream audioStream, string fileName, string contentType, int? durationMs, string bearerToken, CancellationToken cancellationToken = default) => UploadSpeakingAudioAsync(attemptId, questionId, audioStream, fileName, contentType, durationMs, cancellationToken);
    Task<SubmitSpeakingResponse> SubmitSpeakingAttemptAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<SubmitSpeakingResponse> SubmitSpeakingAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => SubmitSpeakingAttemptAsync(attemptId, cancellationToken);
    Task<SpeakingGradingPendingDto> GetSpeakingGradingPendingAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<SpeakingGradingPendingDto> GetSpeakingGradingPendingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetSpeakingGradingPendingAsync(attemptId, cancellationToken);
    Task RetrySpeakingGradingAsync(int attemptId, CancellationToken cancellationToken = default);
    Task RetrySpeakingGradingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => RetrySpeakingGradingAsync(attemptId, cancellationToken);
    Task<SpeakingReviewDto> GetSpeakingReviewAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<SpeakingReviewDto> GetSpeakingReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetSpeakingReviewAsync(attemptId, cancellationToken);
    Task<(Stream Stream, string ContentType, string FileName)> GetSpeakingAudioStreamAsync(int attemptId, int questionId, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType, string FileName)> GetSpeakingAudioStreamAsync(int attemptId, int questionId, string bearerToken, CancellationToken cancellationToken = default) => GetSpeakingAudioStreamAsync(attemptId, questionId, cancellationToken);
}

public interface IPlacementClient
{
    Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(CancellationToken cancellationToken = default);
    Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPlacementCatalogAsync(cancellationToken);
    Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, CancellationToken cancellationToken = default);
    Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) => StartPlacementTestAsync(request, cancellationToken);
    Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, CancellationToken cancellationToken = default);
    Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementTakeAsync(placementId, questionDisplayOrder, cancellationToken);
    Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default);
    Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) => SavePlacementAnswerAsync(placementId, questionId, request, cancellationToken);
    Task<SavePlacementDraftResponseDto> SavePlacementDraftAsync(int placementId, SavePlacementDraftRequestDto request, CancellationToken cancellationToken = default);
    Task<SavePlacementDraftResponseDto> SavePlacementDraftAsync(int placementId, SavePlacementDraftRequestDto request, string bearerToken, CancellationToken cancellationToken = default) => SavePlacementDraftAsync(placementId, request, cancellationToken);
    Task<PlacementGradingStatusDto> GetPlacementGradingStatusAsync(int placementId, CancellationToken cancellationToken = default);
    Task<PlacementGradingStatusDto> GetPlacementGradingStatusAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementGradingStatusAsync(placementId, cancellationToken);
    Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, CancellationToken cancellationToken = default);
    Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, string bearerToken, CancellationToken cancellationToken = default) => SubmitPlacementTestAsync(placementId, request, cancellationToken);
    Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, CancellationToken cancellationToken = default);
    Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, string bearerToken, CancellationToken cancellationToken = default) => GetPlacementResultAsync(placementId, cancellationToken);
    Task<TargetBandViewDto> GetTargetBandViewAsync(CancellationToken cancellationToken = default);
    Task<TargetBandViewDto> GetTargetBandViewAsync(string bearerToken, CancellationToken cancellationToken = default) => GetTargetBandViewAsync(cancellationToken);
    Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, CancellationToken cancellationToken = default);
    Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, string bearerToken, CancellationToken cancellationToken = default) => SetTargetBandAsync(request, cancellationToken);
}

public interface ILearningPathClient
{
    Task<LearningPathSummaryDto> GetLearningPathAsync(CancellationToken cancellationToken = default);
    Task<LearningPathSummaryDto> GetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) => GetLearningPathAsync(cancellationToken);
    Task<LearningPathSummaryDto> GenerateLearningPathAsync(GenerateLearningPathRequest? request, CancellationToken cancellationToken = default);
    Task<LearningPathSummaryDto> GenerateLearningPathAsync(GenerateLearningPathRequest? request, string bearerToken, CancellationToken cancellationToken = default) => GenerateLearningPathAsync(request, cancellationToken);
    Task<LearningPathModuleDetailDto> GetLearningPathModuleAsync(int moduleId, CancellationToken cancellationToken = default);
    Task<LearningPathModuleDetailDto> GetLearningPathModuleAsync(int moduleId, string bearerToken, CancellationToken cancellationToken = default) => GetLearningPathModuleAsync(moduleId, cancellationToken);
    Task<StartStepResultDto> StartLearningPathStepAsync(int stepId, CancellationToken cancellationToken = default);
    Task<StartStepResultDto> StartLearningPathStepAsync(int stepId, string bearerToken, CancellationToken cancellationToken = default) => StartLearningPathStepAsync(stepId, cancellationToken);
    Task<ResetLearningPathResultDto> ResetLearningPathAsync(CancellationToken cancellationToken = default);
    Task<ResetLearningPathResultDto> ResetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) => ResetLearningPathAsync(cancellationToken);
    Task<LearningPathSummaryDto> UpgradeLearningPathAsync(CancellationToken cancellationToken = default);
    Task<LearningPathSummaryDto> UpgradeLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) => UpgradeLearningPathAsync(cancellationToken);
    Task<AiInsightDto> GetPathInsightAsync(CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPathInsightAsync(cancellationToken);
    Task RetryPathInsightAsync(CancellationToken cancellationToken = default);
    Task RetryPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) => RetryPathInsightAsync(cancellationToken);
}

public interface IMentorsClient
{
    Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default);
    Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetMentorCertificateOptionsAsync(cancellationToken);
    Task<MentorBookingDto> BookMentorAsync(int mentorId, BookMentorRequest request, CancellationToken cancellationToken = default);
    Task<MentorBookingDto> BookMentorAsync(int mentorId, BookMentorRequest request, string bearerToken, CancellationToken cancellationToken = default) => BookMentorAsync(mentorId, request, cancellationToken);
    Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, CancellationToken cancellationToken = default);
    Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, string bearerToken, CancellationToken cancellationToken = default) => CancelBookingAsync(bookingId, cancellationToken);
    Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, CancellationToken cancellationToken = default);
    Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, string bearerToken, CancellationToken cancellationToken = default) => GetBookingDetailAsync(bookingId, cancellationToken);
    Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetLearnerBookingsAsync(cancellationToken);
    Task<MentorReviewDto> RateBookingAsync(int bookingId, RateMentorRequest request, CancellationToken cancellationToken = default);
    Task<MentorReviewDto> RateBookingAsync(int bookingId, RateMentorRequest request, string bearerToken, CancellationToken cancellationToken = default) => RateBookingAsync(bookingId, request, cancellationToken);
}

public interface ISupportClient
{
    Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetLearnerComplaintsAsync(cancellationToken);
    Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(CancellationToken cancellationToken = default);
    Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetEligibleSubjectsAsync(cancellationToken);
    Task<LearnerComplaintDto> FileComplaintAsync(FileComplaintRequest request, CancellationToken cancellationToken = default);
    Task<LearnerComplaintDto> FileComplaintAsync(FileComplaintRequest request, string bearerToken, CancellationToken cancellationToken = default) => FileComplaintAsync(request, cancellationToken);
}

public interface IAiInsightsClient
{
    Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => GetPracticeInsightAsync(sessionId, cancellationToken);
    Task RetryPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default);
    Task RetryPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => RetryPracticeInsightAsync(sessionId, cancellationToken);
    Task<AiInsightDto> GetExamInsightAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetExamInsightAsync(attemptId, cancellationToken);
    Task RetryExamInsightAsync(int attemptId, CancellationToken cancellationToken = default);
    Task RetryExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => RetryExamInsightAsync(attemptId, cancellationToken);
    Task RetryInsightAsync(int insightId, CancellationToken cancellationToken = default);
    Task RetryInsightAsync(int insightId, string bearerToken, CancellationToken cancellationToken = default) => RetryInsightAsync(insightId, cancellationToken);
    Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) => GetDashboardInsightAsync(certId, cancellationToken);
    Task RetryDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default);
    Task RetryDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) => RetryDashboardInsightAsync(certId, cancellationToken);
    Task<AiInsightDto> GetPathInsightAsync(CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPathInsightAsync(cancellationToken);
    Task RetryPathInsightAsync(CancellationToken cancellationToken = default);
    Task RetryPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) => RetryPathInsightAsync(cancellationToken);
}

public interface IMediaClient
{
    Task<(Stream Stream, string ContentType)> GetImportMediaAsync(int batchId, string fileName, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType)> GetImportMediaAsync(int batchId, string fileName, string bearerToken, CancellationToken cancellationToken = default) => GetImportMediaAsync(batchId, fileName, cancellationToken);
    Task<(Stream Stream, string ContentType)> GetExamMediaAsync(int examId, string fileName, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType)> GetExamMediaAsync(int examId, string fileName, string bearerToken, CancellationToken cancellationToken = default) => GetExamMediaAsync(examId, fileName, cancellationToken);

    /// <summary>Fetches a stored media file (an import batch's or an exam's) forwarding the browser's
    /// Range header, so audio can start mid-track (slices) and seek through the proxy.</summary>
    async Task<MediaStreamResponse> GetMediaAsync(MediaArea area, int ownerId, string fileName, string? range, string bearerToken, CancellationToken cancellationToken = default)
    {
        var (stream, contentType) = area == MediaArea.Imports
            ? await GetImportMediaAsync(ownerId, fileName, bearerToken, cancellationToken)
            : await GetExamMediaAsync(ownerId, fileName, bearerToken, cancellationToken);
        return new MediaStreamResponse(stream, contentType, 200, null, null);
    }
}

/// <summary>Where a media file is stored: under an import batch (ADR-0007) or under an exam (studio uploads).</summary>
public enum MediaArea
{
    Imports,
    Exams,
}

/// <summary>A media body relayed from the API: 200 with the whole file, or 206 with the requested range.</summary>
public sealed record MediaStreamResponse(Stream Stream, string ContentType, int StatusCode, string? ContentRange, long? ContentLength);

public interface IAdminPackagesClient
{
    Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(CancellationToken cancellationToken = default);
    Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminPackagesAsync(cancellationToken);
    Task<PackageDto> GetAdminPackageAsync(int packageId, CancellationToken cancellationToken = default);
    Task<PackageDto> GetAdminPackageAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminPackageAsync(packageId, cancellationToken);
    Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, CancellationToken cancellationToken = default);
    Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreatePackageAsync(request, cancellationToken);
    Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, CancellationToken cancellationToken = default);
    Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdatePackageAsync(packageId, request, cancellationToken);
    Task DeactivatePackageAsync(int packageId, CancellationToken cancellationToken = default);
    Task DeactivatePackageAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) => DeactivatePackageAsync(packageId, cancellationToken);
}

public interface IAdminScoreConversionClient
{
    Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, CancellationToken cancellationToken = default);
    Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminScoreConversionAsync(certCode, examId, cancellationToken);
    Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, CancellationToken cancellationToken = default);
    Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateAdminScoreConversionAsync(certId, request, cancellationToken);

    Task<ScorePreviewResultDto> CalculateScorePreviewAsync(ScorePreviewRequestDto request, CancellationToken cancellationToken = default);
    Task<ScorePreviewResultDto> CalculateScorePreviewAsync(ScorePreviewRequestDto request, string bearerToken, CancellationToken cancellationToken = default) => CalculateScorePreviewAsync(request, cancellationToken);
}

public interface IAdminOrdersClient
{
    Task<AdminOrdersSummaryDto> GetAdminOrdersAsync(string? q, string? status, string? gateway, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminOrdersSummaryDto> GetAdminOrdersAsync(string? q, string? status, string? gateway, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminOrdersAsync(q, status, gateway, page, pageSize, cancellationToken);
    Task<AdminOrderDetailDto> GetAdminOrderDetailAsync(int orderId, CancellationToken cancellationToken = default);
    Task<AdminOrderDetailDto> GetAdminOrderDetailAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminOrderDetailAsync(orderId, cancellationToken);
    Task<PaymentResultDto> ReconcileAdminOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<PaymentResultDto> ReconcileAdminOrderAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default) => ReconcileAdminOrderAsync(orderId, cancellationToken);
}

public interface IAdminUsersClient
{
    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminUsersAsync(q, status, page, pageSize, cancellationToken);
    Task<AdminUserDto> LockUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<AdminUserDto> LockUserAsync(int userId, string bearerToken, CancellationToken cancellationToken = default) => LockUserAsync(userId, cancellationToken);
    Task<AdminUserDto> UnlockUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<AdminUserDto> UnlockUserAsync(int userId, string bearerToken, CancellationToken cancellationToken = default) => UnlockUserAsync(userId, cancellationToken);
    Task ResendUserActivationAsync(int userId, CancellationToken cancellationToken = default);
    Task ResendUserActivationAsync(int userId, string bearerToken, CancellationToken cancellationToken = default) => ResendUserActivationAsync(userId, cancellationToken);
    Task<AdjustCreditsResultDto> AdjustUserCreditsAsync(int userId, AdjustCreditsRequest request, CancellationToken cancellationToken = default);
    Task<AdjustCreditsResultDto> AdjustUserCreditsAsync(int userId, AdjustCreditsRequest request, string bearerToken, CancellationToken cancellationToken = default) => AdjustUserCreditsAsync(userId, request, cancellationToken);
}

public interface IAdminDashboardClient
{
    Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default);
    Task<AdminDashboardDto> GetAdminDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminDashboardAsync(cancellationToken);
}

public interface IAdminMentorsClient
{
    Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminMentorsAsync(cancellationToken);
    Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminMentorByIdAsync(mentorId, cancellationToken);
    Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateMentorAsync(request, cancellationToken);
    Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateMentorAsync(mentorId, request, cancellationToken);
    Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminMentorSlotsAsync(mentorId, cancellationToken);
    Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, CreateMentorSlotRequest request, CancellationToken cancellationToken = default);
    Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, CreateMentorSlotRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateMentorSlotAsync(mentorId, request, cancellationToken);
    Task CloseMentorSlotAsync(int mentorId, int slotId, CancellationToken cancellationToken = default);
    Task CloseMentorSlotAsync(int mentorId, int slotId, string bearerToken, CancellationToken cancellationToken = default) => CloseMentorSlotAsync(mentorId, slotId, cancellationToken);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetMentorCertificateOptionsAsync(cancellationToken);
    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminUsersAsync(q, status, page, pageSize, cancellationToken);
}

public interface IAdminComplaintsClient
{
    Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminComplaintsAsync(q, status, page, pageSize, cancellationToken);
    Task<AdminComplaintDetailDto> GetAdminComplaintDetailAsync(int complaintId, CancellationToken cancellationToken = default);
    Task<AdminComplaintDetailDto> GetAdminComplaintDetailAsync(int complaintId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminComplaintDetailAsync(complaintId, cancellationToken);
    Task<AdminComplaintDetailDto> SetAdminComplaintStatusAsync(int complaintId, UpdateComplaintStatusRequest request, CancellationToken cancellationToken = default);
    Task<AdminComplaintDetailDto> SetAdminComplaintStatusAsync(int complaintId, UpdateComplaintStatusRequest request, string bearerToken, CancellationToken cancellationToken = default) => SetAdminComplaintStatusAsync(complaintId, request, cancellationToken);
    Task<AdminComplaintDetailDto> ResolveAdminComplaintAsync(int complaintId, ResolveComplaintRequest request, CancellationToken cancellationToken = default);
    Task<AdminComplaintDetailDto> ResolveAdminComplaintAsync(int complaintId, ResolveComplaintRequest request, string bearerToken, CancellationToken cancellationToken = default) => ResolveAdminComplaintAsync(complaintId, request, cancellationToken);
}

public interface IAdminExamsClient
{
    Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminExamsAsync(q, status, certId, null, page, pageSize, cancellationToken);
    Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamsAsync(q, status, certId, purpose, page, pageSize, cancellationToken);
    Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamsAsync(q, status, certId, null, page, pageSize, cancellationToken);
    Task<AdminExamDetailDto> GetAdminExamByIdAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminExamDetailDto> GetAdminExamByIdAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamByIdAsync(examId, cancellationToken);
    Task<AdminExamDetailDto> CreateAdminExamAsync(CreateAdminExamRequest request, CancellationToken cancellationToken = default);
    Task<AdminExamDetailDto> CreateAdminExamAsync(CreateAdminExamRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminExamAsync(request, cancellationToken);
    Task<AdminExamDetailDto> UpdateAdminExamAsync(int examId, UpdateAdminExamRequest request, CancellationToken cancellationToken = default);
    Task<AdminExamDetailDto> UpdateAdminExamAsync(int examId, UpdateAdminExamRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateAdminExamAsync(examId, request, cancellationToken);
    Task<AdminPublishExamResultDto> PublishAdminExamAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminPublishExamResultDto> PublishAdminExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => PublishAdminExamAsync(examId, cancellationToken);
    Task DeleteAdminExamAsync(int examId, CancellationToken cancellationToken = default);
    Task DeleteAdminExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => DeleteAdminExamAsync(examId, cancellationToken);
    Task<AdminExamQuestionsDto> GetAdminExamQuestionsAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminExamQuestionsDto> GetAdminExamQuestionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamQuestionsAsync(examId, cancellationToken);
    Task<AdminQuestionGroupDto> CreateAdminQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> CreateAdminQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminQuestionGroupAsync(examId, request, cancellationToken);
    Task<AdminQuestionGroupDto> UpdateAdminQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> UpdateAdminQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateAdminQuestionGroupAsync(examId, groupId, request, cancellationToken);
    Task DeleteAdminQuestionGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default);
    Task DeleteAdminQuestionGroupAsync(int examId, int groupId, string bearerToken, CancellationToken cancellationToken = default) => DeleteAdminQuestionGroupAsync(examId, groupId, cancellationToken);
    Task<AdminQuestionDto> CreateAdminQuestionAsync(int examId, CreateQuestionRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionDto> CreateAdminQuestionAsync(int examId, CreateQuestionRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminQuestionAsync(examId, request, cancellationToken);
    Task<AdminQuestionDto> UpdateAdminQuestionAsync(int questionId, UpdateQuestionRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionDto> UpdateAdminQuestionAsync(int questionId, UpdateQuestionRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateAdminQuestionAsync(questionId, request, cancellationToken);
    Task DeleteAdminQuestionAsync(int questionId, CancellationToken cancellationToken = default);
    Task DeleteAdminQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) => DeleteAdminQuestionAsync(questionId, cancellationToken);
    Task<AdminExamSectionAudioDto> SaveAdminExamSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, CancellationToken cancellationToken = default);
    Task<AdminExamSectionAudioDto> SaveAdminExamSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveAdminExamSectionAudioAsync(examId, request, cancellationToken);
    Task<QuickIngestParseResultDto> ParseQuickIngestAsync(int examId, QuickIngestParseRequest request, CancellationToken cancellationToken = default);
    Task<QuickIngestParseResultDto> ParseQuickIngestAsync(int examId, QuickIngestParseRequest request, string bearerToken, CancellationToken cancellationToken = default) => ParseQuickIngestAsync(examId, request, cancellationToken);
    Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default);
    Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, string bearerToken, CancellationToken cancellationToken = default) => CommitQuickIngestAsync(examId, request, cancellationToken);
    Task<ExamStudioDetailDto> GetAdminExamStudioAsync(int examId, CancellationToken cancellationToken = default);
    Task<ExamStudioDetailDto> GetAdminExamStudioAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamStudioAsync(examId, cancellationToken);
    Task<ExamAuditResultDto> GetAdminExamAuditAsync(int examId, CancellationToken cancellationToken = default);
    Task<ExamAuditResultDto> GetAdminExamAuditAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamAuditAsync(examId, cancellationToken);
    Task ReorderAdminSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default);
    Task ReorderAdminSectionItemsAsync(int examId, ReorderSectionItemsRequest request, string bearerToken, CancellationToken cancellationToken = default) => ReorderAdminSectionItemsAsync(examId, request, cancellationToken);
    Task<AdminQuestionGroupDto> CreateAdminExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> CreateAdminExamClusterAsync(int examId, CreateExamClusterRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminExamClusterAsync(examId, request, cancellationToken);
    Task<AdminExamMediaUploadResultDto> UploadAdminExamMediaAsync(int examId, Stream stream, string fileName, CancellationToken cancellationToken = default);
    Task<AdminExamMediaUploadResultDto> UploadAdminExamMediaAsync(int examId, Stream stream, string fileName, string bearerToken, CancellationToken cancellationToken = default) => UploadAdminExamMediaAsync(examId, stream, fileName, cancellationToken);
    Task<(Stream Stream, string ContentType, string FileName)> ExportAdminExamAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType, string FileName)> ExportAdminExamAsync(int examId, string? format, string bearerToken, CancellationToken cancellationToken = default) => ExportAdminExamAsync(examId, format, cancellationToken);
    Task<ExamGenerationResultDto> GenerateAdminExamAsync(ExamBlueprintDto blueprint, CancellationToken cancellationToken = default);
    Task<ExamGenerationResultDto> GenerateAdminExamAsync(ExamBlueprintDto blueprint, string bearerToken, CancellationToken cancellationToken = default) => GenerateAdminExamAsync(blueprint, cancellationToken);
}

public interface IAdminImportsClient
{
    Task<AdminImportBatchesDto> GetAdminImportBatchesAsync(int? certId, CancellationToken cancellationToken = default);
    Task<AdminImportBatchesDto> GetAdminImportBatchesAsync(int? certId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminImportBatchesAsync(certId, cancellationToken);
    Task<AdminImportBatchDetailDto> GetAdminImportBatchAsync(int batchId, CancellationToken cancellationToken = default);
    Task<AdminImportBatchDetailDto> GetAdminImportBatchAsync(int batchId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminImportBatchAsync(batchId, cancellationToken);
    Task<AdminImportResultDto> UploadAdminImportAsync(int certId, Stream content, string fileName, CancellationToken cancellationToken = default);
    Task<AdminImportResultDto> UploadAdminImportAsync(int certId, Stream content, string fileName, string bearerToken, CancellationToken cancellationToken = default) => UploadAdminImportAsync(certId, content, fileName, cancellationToken);
}

public interface IAdminLessonsClient
{
    Task<IReadOnlyList<AdminLessonSummaryDto>> GetAdminLessonsAsync(int? certId = null, int? skillId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminLessonSummaryDto>> GetAdminLessonsAsync(int? certId, int? skillId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminLessonsAsync(certId, skillId, cancellationToken);

    Task<AdminLessonDetailDto> GetAdminLessonByIdAsync(int lessonId, CancellationToken cancellationToken = default);
    Task<AdminLessonDetailDto> GetAdminLessonByIdAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminLessonByIdAsync(lessonId, cancellationToken);

    Task<AdminLessonDetailDto> CreateAdminLessonAsync(AdminLessonUpsertDto request, CancellationToken cancellationToken = default);
    Task<AdminLessonDetailDto> CreateAdminLessonAsync(AdminLessonUpsertDto request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminLessonAsync(request, cancellationToken);

    Task<AdminLessonDetailDto> UpdateAdminLessonAsync(int lessonId, AdminLessonUpsertDto request, CancellationToken cancellationToken = default);
    Task<AdminLessonDetailDto> UpdateAdminLessonAsync(int lessonId, AdminLessonUpsertDto request, string bearerToken, CancellationToken cancellationToken = default) => UpdateAdminLessonAsync(lessonId, request, cancellationToken);

    Task DeleteAdminLessonAsync(int lessonId, CancellationToken cancellationToken = default);
    Task DeleteAdminLessonAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) => DeleteAdminLessonAsync(lessonId, cancellationToken);

    Task<AdminLessonImportResultDto> UploadAdminLessonImportAsync(int certId, bool overwrite, Stream content, string fileName, CancellationToken cancellationToken = default);
    Task<AdminLessonImportResultDto> UploadAdminLessonImportAsync(int certId, bool overwrite, Stream content, string fileName, string bearerToken, CancellationToken cancellationToken = default) => UploadAdminLessonImportAsync(certId, overwrite, content, fileName, cancellationToken);

    Task<LessonManifestDto> ExportAdminLessonAsync(int lessonId, CancellationToken cancellationToken = default);
    Task<LessonManifestDto> ExportAdminLessonAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) => ExportAdminLessonAsync(lessonId, cancellationToken);

    Task<LessonManifestDto> GetAdminLessonTemplateAsync(CancellationToken cancellationToken = default);
    Task<LessonManifestDto> GetAdminLessonTemplateAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminLessonTemplateAsync(cancellationToken);

    Task<AdminLessonFormOptionsDto> GetAdminLessonFormOptionsAsync(int? certId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AdminLessonFormOptionsDto(Array.Empty<AdminCertificateOptionDto>(), Array.Empty<AdminLessonSkillOptionDto>(), Array.Empty<AdminLessonCategoryOptionDto>()));
    Task<AdminLessonFormOptionsDto> GetAdminLessonFormOptionsAsync(int? certId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminLessonFormOptionsAsync(certId, cancellationToken);
}

public interface IAdminCertificatesClient
{
    Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminCertificatesAsync(cancellationToken);

    Task<IReadOnlyList<CertificateBlueprintPresetDto>> GetPresetsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CertificateBlueprintPresetDto>> GetPresetsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPresetsAsync(cancellationToken);

    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, CancellationToken cancellationToken = default);
    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, string bearerToken, CancellationToken cancellationToken = default) => GetCertificateBlueprintAsync(certId, cancellationToken);

    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintByCodeAsync(string code, string bearerToken, CancellationToken cancellationToken = default) => GetCertificateBlueprintByCodeAsync(code, cancellationToken);

    Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, CancellationToken cancellationToken = default);
    Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveCertificateWizardAsync(request, cancellationToken);
}
