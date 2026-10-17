namespace Contracts;

public interface ILessonClient
{
    Task<LessonDetailDto> GetLessonAsync(int lessonId, CancellationToken cancellationToken = default);
    Task<LessonDetailDto> GetLessonAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) => GetLessonAsync(lessonId, cancellationToken);

    Task<LessonProgressDto> GetLessonProgressAsync(int lessonId, CancellationToken cancellationToken = default);
    Task<LessonProgressDto> GetLessonProgressAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) => GetLessonProgressAsync(lessonId, cancellationToken);

    Task RecordCardViewAsync(int lessonId, int cardId, CancellationToken cancellationToken = default);
    Task RecordCardViewAsync(int lessonId, int cardId, string bearerToken, CancellationToken cancellationToken = default) => RecordCardViewAsync(lessonId, cardId, cancellationToken);

    Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, CancellationToken cancellationToken = default);
    Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, string bearerToken, CancellationToken cancellationToken = default) => GetLessonByCategoryAsync(categoryId, cancellationToken);

    Task UnlockPathAsync(int learnerPathId, CancellationToken cancellationToken = default);
    Task UnlockPathAsync(int learnerPathId, string bearerToken, CancellationToken cancellationToken = default) => UnlockPathAsync(learnerPathId, cancellationToken);
}
