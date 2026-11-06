using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public interface ISavedQuestionService
{
    Task<SavedQuestionsView> GetSavedQuestionsAsync(int userId, SavedQuestionFilterInput filter, CancellationToken cancellationToken = default);

    /// <summary>Reads one bookmark by its own row identity, which is unique across both shapes.</summary>
    Task<SavedQuestionItem> GetSavedQuestionAsync(int userId, int savedQuestionId, CancellationToken cancellationToken = default);

    /// <summary>Saves a legacy question-shaped link.</summary>
    Task<SaveQuestionResult> SaveQuestionAsync(int userId, int questionId, SavedQuestionSource source = SavedQuestionSource.Manual, CancellationToken cancellationToken = default);

    /// <summary>
    /// #224: saves a pool item, pinning the version that was on screen. A second save of the same item
    /// keeps the version already pinned rather than moving the bookmark to the item's latest one.
    /// </summary>
    Task<SaveQuestionResult> SaveItemVersionAsync(int userId, int itemVersionId, SavedQuestionSource source = SavedQuestionSource.Manual, CancellationToken cancellationToken = default);

    Task<SaveQuestionResult> UnsaveAsync(int userId, int savedQuestionId, CancellationToken cancellationToken = default);

    Task<bool> IsQuestionSavedAsync(int userId, int questionId, CancellationToken cancellationToken = default);
}
