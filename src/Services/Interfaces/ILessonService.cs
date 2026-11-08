using System.Threading.Tasks;
using BusinessObjects;
using Contracts;

namespace Services;

/// <summary>
/// #240: the leveled lesson reader. A lesson is addressed by (skill, topic, level); it carries two
/// sub-modules, and progress is per learner per lesson per module.
/// <para>
/// The two-argument overloads default to the primary module so a caller that only knows a lesson id
/// keeps working; every reader route the path opens passes a module explicitly.
/// </para>
/// </summary>
public interface ILessonService
{
    /// <summary>#240: the lesson addressed by one (skill, topic, level) cell.</summary>
    Task<LessonDetailDto?> GetLessonForCellAsync(int certId, int skillId, int topicId, string cefrLevel, string module);

    /// <summary>#240: one module of one lesson.</summary>
    Task<LessonDetailDto?> GetLessonAsync(int lessonId, int certId, string module);

    /// <summary>#240: one module's progress, including the exact card it resumes at.</summary>
    Task<LessonProgressDto> GetProgressAsync(int userId, int lessonId, string module);

    Task RecordCardViewAsync(int userId, int lessonId, int cardId);

    Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, int certId);

    /// <summary>The primary module of one lesson. A caller that names no module means the one every
    /// lesson has.</summary>
    Task<LessonDetailDto?> GetLessonAsync(int lessonId, int certId) =>
        GetLessonAsync(lessonId, certId, LessonModule.Primary);

    /// <summary>The primary module's progress.</summary>
    Task<LessonProgressDto> GetProgressAsync(int userId, int lessonId) =>
        GetProgressAsync(userId, lessonId, LessonModule.Primary);
}
