namespace Contracts;

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
