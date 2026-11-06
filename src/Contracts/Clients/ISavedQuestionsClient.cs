namespace Contracts;

/// <summary>
/// #224: a saved link is addressed by its own row id, and the two save shapes are separate routes
/// because a question id and an item-version id are two counters that collide — one address for both
/// would save or remove the wrong thing.
/// </summary>
public interface ISavedQuestionsClient
{
    Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, CancellationToken cancellationToken = default);
    Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, string bearerToken, CancellationToken cancellationToken = default) => GetSavedQuestionsAsync(source, sectionId, categoryId, cancellationToken);

    Task<SavedQuestionItemDto> GetSavedQuestionAsync(int savedQuestionId, CancellationToken cancellationToken = default);
    Task<SavedQuestionItemDto> GetSavedQuestionAsync(int savedQuestionId, string bearerToken, CancellationToken cancellationToken = default) => GetSavedQuestionAsync(savedQuestionId, cancellationToken);

    /// <summary>Saves a legacy question-shaped link.</summary>
    Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, CancellationToken cancellationToken = default);
    Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, string bearerToken, CancellationToken cancellationToken = default) => SaveQuestionAsync(questionId, source, cancellationToken);

    /// <summary>#224: saves a pool item, pinning the version that was on screen.</summary>
    Task<SaveQuestionResultDto> SaveItemVersionAsync(int itemVersionId, string? source, CancellationToken cancellationToken = default);
    Task<SaveQuestionResultDto> SaveItemVersionAsync(int itemVersionId, string? source, string bearerToken, CancellationToken cancellationToken = default) => SaveItemVersionAsync(itemVersionId, source, cancellationToken);

    Task<SaveQuestionResultDto> UnsaveQuestionAsync(int savedQuestionId, CancellationToken cancellationToken = default);
    Task<SaveQuestionResultDto> UnsaveQuestionAsync(int savedQuestionId, string bearerToken, CancellationToken cancellationToken = default) => UnsaveQuestionAsync(savedQuestionId, cancellationToken);
}
