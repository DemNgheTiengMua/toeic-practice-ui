
namespace Contracts;

/// <summary>
/// #240: the reader's wire surface. A lesson is read one module at a time, so every get names the
/// module it wants; the overload without one means the primary module, which every lesson has.
/// </summary>
public interface ILessonClient
{
    /// <summary>#240: one module of one lesson.</summary>
    Task<LessonDetailDto> GetLessonModuleAsync(int lessonId, string module, string bearerToken, CancellationToken cancellationToken = default);

    Task<LessonDetailDto> GetLessonModuleAsync(int lessonId, string module, CancellationToken cancellationToken = default);

    /// <summary>#240: the lesson addressed by a (skill, topic, level) cell.</summary>
    Task<LessonDetailDto> GetLessonForCellAsync(int skillId, int topicId, string level, string module, string bearerToken, CancellationToken cancellationToken = default);

    Task<LessonDetailDto> GetLessonForCellAsync(int skillId, int topicId, string level, string module, CancellationToken cancellationToken = default);

    /// <summary>#240: one module's progress, including the exact card it resumes at.</summary>
    Task<LessonProgressDto> GetLessonProgressModuleAsync(int lessonId, string module, string bearerToken, CancellationToken cancellationToken = default);

    Task<LessonProgressDto> GetLessonProgressModuleAsync(int lessonId, string module, CancellationToken cancellationToken = default);

    Task RecordCardViewAsync(int lessonId, int cardId, CancellationToken cancellationToken = default);
    Task RecordCardViewAsync(int lessonId, int cardId, string bearerToken, CancellationToken cancellationToken = default);

    /// <summary>#155: graded on the server, so the answer reaches the mastery machine and the correct
    /// option is not sitting in the page for the browser to read.</summary>
    Task<MicroCheckResultDto> SubmitMicroCheckAsync(int lessonId, int cardId, string selectedOption, CancellationToken cancellationToken = default);
    Task<MicroCheckResultDto> SubmitMicroCheckAsync(int lessonId, int cardId, string selectedOption, string bearerToken, CancellationToken cancellationToken = default);

    Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, CancellationToken cancellationToken = default);
    Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, string bearerToken, CancellationToken cancellationToken = default);

    /// <summary>The primary module of one lesson, for a caller that names no module.</summary>
    Task<LessonDetailDto> GetLessonAsync(int lessonId, CancellationToken cancellationToken = default) =>
        GetLessonModuleAsync(lessonId, LessonModuleCodes.Primary, cancellationToken);

    Task<LessonDetailDto> GetLessonAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetLessonModuleAsync(lessonId, LessonModuleCodes.Primary, bearerToken, cancellationToken);

    /// <summary>The primary module's progress.</summary>
    Task<LessonProgressDto> GetLessonProgressAsync(int lessonId, CancellationToken cancellationToken = default) =>
        GetLessonProgressModuleAsync(lessonId, LessonModuleCodes.Primary, cancellationToken);

    Task<LessonProgressDto> GetLessonProgressAsync(int lessonId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetLessonProgressModuleAsync(lessonId, LessonModuleCodes.Primary, bearerToken, cancellationToken);
}
