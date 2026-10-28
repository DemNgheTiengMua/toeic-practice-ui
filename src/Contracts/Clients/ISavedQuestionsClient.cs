namespace Contracts;

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
