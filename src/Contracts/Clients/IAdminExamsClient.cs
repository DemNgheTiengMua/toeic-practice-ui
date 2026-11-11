
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
    Task<AdminPublishExamResultDto> WithdrawAdminExamAsync(int examId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AdminPublishExamResultDto(false, string.Empty, []));
    Task<AdminPublishExamResultDto> WithdrawAdminExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => WithdrawAdminExamAsync(examId, cancellationToken);
    Task DeleteAdminExamAsync(int examId, CancellationToken cancellationToken = default);
    Task DeleteAdminExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => DeleteAdminExamAsync(examId, cancellationToken);
    Task<AdminExamQuestionsDto> GetAdminExamQuestionsAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminExamQuestionsDto> GetAdminExamQuestionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamQuestionsAsync(examId, cancellationToken);
    Task<AdminQuestionGroupDto> CreateAdminQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> CreateAdminQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminQuestionGroupAsync(examId, request, cancellationToken);
    /// <summary>
    /// #273: the Studio's group write, addressed as the Studio reads it - a stimulus id, or a solo
    /// position's negated id. The deleted <c>UpdateAdminQuestionGroupAsync</c> posted to a route #265
    /// removed and wrote a table #269 drops.
    /// </summary>
    Task UpdateExamStudioGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default);
    Task UpdateExamStudioGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateExamStudioGroupAsync(examId, groupId, request, cancellationToken);
    Task DeleteExamStudioGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default);
    Task DeleteExamStudioGroupAsync(int examId, int groupId, string bearerToken, CancellationToken cancellationToken = default) => DeleteExamStudioGroupAsync(examId, groupId, cancellationToken);
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

    Task<ExamStudioDetailDto> GetAdminExamStudioAsync(int examId, CancellationToken cancellationToken = default);
    Task<ExamStudioDetailDto> GetAdminExamStudioAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamStudioAsync(examId, cancellationToken);

    /// <summary>#225 / S-09: the assembly workbench — declared against servable supply, per section.</summary>
    Task<ExamAssemblyDto> GetAdminExamAssemblyAsync(int examId, CancellationToken cancellationToken = default);
    Task<ExamAssemblyDto> GetAdminExamAssemblyAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamAssemblyAsync(examId, cancellationToken);
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

    /// <summary>#158: the real sitting view plus the answer key, for a draft or published exam.</summary>
    Task<ExamPreviewTakeDto> GetAdminExamPreviewAsync(int examId, int? q, CancellationToken cancellationToken = default);
    Task<ExamPreviewTakeDto> GetAdminExamPreviewAsync(int examId, int? q, string bearerToken, CancellationToken cancellationToken = default) => GetAdminExamPreviewAsync(examId, q, cancellationToken);
}
