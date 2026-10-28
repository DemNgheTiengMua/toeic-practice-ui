
namespace Contracts;

public interface IAdminExamsClient
{
    Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminExamsAsync(q, status, certId, null, page, pageSize, cancellationToken);
    Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamsAsync(q, status, certId, purpose, page, pageSize, cancellationToken);
    Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamsAsync(q, status, certId, null, page, pageSize, cancellationToken);
    Task<AdminExamDetailDto> GetAdminExamByIdAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminExamDetailDto> GetAdminExamByIdAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamByIdAsync(examId, cancellationToken);
    Task<AdminExamDetailDto> CreateAdminExamAsync(CreateAdminExamRequest request, CancellationToken cancellationToken = default);
    Task<AdminExamDetailDto> CreateAdminExamAsync(CreateAdminExamRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminExamAsync(request, cancellationToken);
    Task<AdminExamDetailDto> UpdateAdminExamAsync(int examId, UpdateAdminExamRequest request, CancellationToken cancellationToken = default);
    Task<AdminExamDetailDto> UpdateAdminExamAsync(int examId, UpdateAdminExamRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateAdminExamAsync(examId, request, cancellationToken);
    Task<AdminPublishExamResultDto> PublishAdminExamAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminPublishExamResultDto> PublishAdminExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => PublishAdminExamAsync(examId, cancellationToken);
    Task DeleteAdminExamAsync(int examId, CancellationToken cancellationToken = default);
    Task DeleteAdminExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => DeleteAdminExamAsync(examId, cancellationToken);
    Task<AdminExamQuestionsDto> GetAdminExamQuestionsAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminExamQuestionsDto> GetAdminExamQuestionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamQuestionsAsync(examId, cancellationToken);
    Task<AdminQuestionGroupDto> CreateAdminQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> CreateAdminQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminQuestionGroupAsync(examId, request, cancellationToken);
    Task<AdminQuestionGroupDto> UpdateAdminQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> UpdateAdminQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateAdminQuestionGroupAsync(examId, groupId, request, cancellationToken);
    Task DeleteAdminQuestionGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default);
    Task DeleteAdminQuestionGroupAsync(int examId, int groupId, string bearerToken, CancellationToken cancellationToken = default) => DeleteAdminQuestionGroupAsync(examId, groupId, cancellationToken);
    Task<AdminQuestionDto> CreateAdminQuestionAsync(int examId, CreateQuestionRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionDto> CreateAdminQuestionAsync(int examId, CreateQuestionRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminQuestionAsync(examId, request, cancellationToken);
    Task<AdminQuestionDto> UpdateAdminQuestionAsync(int questionId, UpdateQuestionRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionDto> UpdateAdminQuestionAsync(int questionId, UpdateQuestionRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateAdminQuestionAsync(questionId, request, cancellationToken);
    Task DeleteAdminQuestionAsync(int questionId, CancellationToken cancellationToken = default);
    Task DeleteAdminQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) => DeleteAdminQuestionAsync(questionId, cancellationToken);
    Task<AdminExamSectionAudioDto> SaveAdminExamSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, CancellationToken cancellationToken = default);
    Task<AdminExamSectionAudioDto> SaveAdminExamSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveAdminExamSectionAudioAsync(examId, request, cancellationToken);
    Task<QuickIngestParseResultDto> ParseQuickIngestAsync(int examId, QuickIngestParseRequest request, CancellationToken cancellationToken = default);
    Task<QuickIngestParseResultDto> ParseQuickIngestAsync(int examId, QuickIngestParseRequest request, string bearerToken, CancellationToken cancellationToken = default) => ParseQuickIngestAsync(examId, request, cancellationToken);
    Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default);
    Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, string bearerToken, CancellationToken cancellationToken = default) => CommitQuickIngestAsync(examId, request, cancellationToken);
    Task<QuickIngestSampleTemplateDto> GetQuickIngestSampleTemplateAsync(int sectionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new QuickIngestSampleTemplateDto(string.Empty, string.Empty));
    Task<QuickIngestSampleTemplateDto> GetQuickIngestSampleTemplateAsync(int sectionId, string bearerToken, CancellationToken cancellationToken = default) => GetQuickIngestSampleTemplateAsync(sectionId, cancellationToken);
    Task<SectionGenerationResultDto> GenerateSectionQuestionsAsync(int examId, int sectionId, SectionGenerateRequest? request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SectionGenerationResultDto(examId, sectionId, 0, 0, string.Empty));
    Task<SectionGenerationResultDto> GenerateSectionQuestionsAsync(int examId, int sectionId, SectionGenerateRequest? request, string bearerToken, CancellationToken cancellationToken = default) => GenerateSectionQuestionsAsync(examId, sectionId, request, cancellationToken);

    Task<ExamStudioDetailDto> GetAdminExamStudioAsync(int examId, CancellationToken cancellationToken = default);
    Task<ExamStudioDetailDto> GetAdminExamStudioAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamStudioAsync(examId, cancellationToken);
    Task<ExamAuditResultDto> GetAdminExamAuditAsync(int examId, CancellationToken cancellationToken = default);
    Task<ExamAuditResultDto> GetAdminExamAuditAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamAuditAsync(examId, cancellationToken);
    Task ReorderAdminSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default);
    Task ReorderAdminSectionItemsAsync(int examId, ReorderSectionItemsRequest request, string bearerToken, CancellationToken cancellationToken = default) => ReorderAdminSectionItemsAsync(examId, request, cancellationToken);
    Task<AdminQuestionGroupDto> CreateAdminExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> CreateAdminExamClusterAsync(int examId, CreateExamClusterRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminExamClusterAsync(examId, request, cancellationToken);
    Task<AdminExamMediaUploadResultDto> UploadAdminExamMediaAsync(int examId, Stream stream, string fileName, CancellationToken cancellationToken = default);
    Task<AdminExamMediaUploadResultDto> UploadAdminExamMediaAsync(int examId, Stream stream, string fileName, string bearerToken, CancellationToken cancellationToken = default) => UploadAdminExamMediaAsync(examId, stream, fileName, cancellationToken);
    Task<(Stream Stream, string ContentType, string FileName)> ExportAdminExamAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType, string FileName)> ExportAdminExamAsync(int examId, string? format, string bearerToken, CancellationToken cancellationToken = default) => ExportAdminExamAsync(examId, format, cancellationToken);
    Task<(Stream Stream, string ContentType, string FileName)> GetManifestTemplateAsync(int certId, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType, string FileName)> GetManifestTemplateAsync(int certId, string bearerToken, CancellationToken cancellationToken = default) => GetManifestTemplateAsync(certId, cancellationToken);
    Task<ExamGenerationResultDto> GenerateAdminExamAsync(ExamBlueprintDto blueprint, CancellationToken cancellationToken = default);
    Task<ExamGenerationResultDto> GenerateAdminExamAsync(ExamBlueprintDto blueprint, string bearerToken, CancellationToken cancellationToken = default) => GenerateAdminExamAsync(blueprint, cancellationToken);
}
