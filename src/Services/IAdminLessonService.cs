using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Contracts;

namespace Services;

public class AdminLessonException(string code, string message, int status = 400) : Exception(message)
{
    public string Code => code;
    public int Status => status;
}

public interface IAdminLessonService
{
    Task<IReadOnlyList<AdminLessonSummaryDto>> GetLessonsAsync(int? certId = null, int? skillId = null, CancellationToken cancellationToken = default);

    Task<AdminLessonDetailDto?> GetLessonAsync(int lessonId, CancellationToken cancellationToken = default);

    Task<AdminLessonDetailDto> CreateLessonAsync(AdminLessonUpsertDto request, CancellationToken cancellationToken = default);

    Task<AdminLessonDetailDto> UpdateLessonAsync(int lessonId, AdminLessonUpsertDto request, CancellationToken cancellationToken = default);

    Task DeleteLessonAsync(int lessonId, CancellationToken cancellationToken = default);

    Task<AdminLessonImportResultDto> ImportLessonsAsync(int certId, bool overwrite, Stream stream, string fileName, CancellationToken cancellationToken = default);

    Task<LessonManifestDto> ExportLessonAsync(int lessonId, CancellationToken cancellationToken = default);

    LessonManifestDto GetTemplate();

    Task<AdminLessonFormOptionsDto> GetFormOptionsAsync(int? certId = null, CancellationToken cancellationToken = default);
}
