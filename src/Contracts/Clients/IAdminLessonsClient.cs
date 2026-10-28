
namespace Contracts;

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
