namespace Contracts;

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
    Task<PracticeSessionTakeDto> GetPracticeSessionTakeAsync(int sessionId, int? displayOrder = null, CancellationToken cancellationToken = default) => Task.FromResult(new PracticeSessionTakeDto(null!, null!, [], []));
    Task<PracticeSessionTakeDto> GetPracticeSessionTakeAsync(int sessionId, int? displayOrder, string bearerToken, CancellationToken cancellationToken = default) => GetPracticeSessionTakeAsync(sessionId, displayOrder, cancellationToken);
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
