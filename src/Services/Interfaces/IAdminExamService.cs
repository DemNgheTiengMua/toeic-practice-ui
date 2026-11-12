using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IAdminExamService
{
    Task<AdminExamListSummaryDto> GetExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminExamListSummaryDto> GetExamsAsync(string? q, string? status, int? certId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetExamsAsync(q, status, certId, null, page, pageSize, cancellationToken);
    Task<AdminExamDetailDto> GetExamByIdAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminExamDetailDto> CreateExamAsync(CreateAdminExamRequest request, CancellationToken cancellationToken = default);
    Task<AdminExamDetailDto> UpdateExamAsync(int examId, UpdateAdminExamRequest request, CancellationToken cancellationToken = default);
    Task<AdminPublishExamResultDto> PublishExamAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminPublishExamResultDto> WithdrawExamAsync(int examId, CancellationToken cancellationToken = default);
    Task DeleteExamAsync(int examId, CancellationToken cancellationToken = default);

    /// <summary>
    /// #227: a structural repair forks a new edition rather than mutating the one learners sat. The
    /// fork is a draft; it goes live through <see cref="PublishExamAsync"/> like any other paper.
    /// </summary>
    Task<AdminForkEditionResultDto> ForkEditionAsync(int examId, ForkExamEditionRequest request, CancellationToken cancellationToken = default);

    Task<AdminExamQuestionsDto> GetExamQuestionsAsync(int examId, CancellationToken cancellationToken = default);

    /// <summary>
    /// #273: the Studio's group autosave against a pool-authored paper. The address is the Studio's -
    /// a stimulus id, or a solo position's negated id - and it writes the stimulus chrome and the
    /// positions' unit.
    /// <para>
    /// #269: the legacy Create/Update/Delete pair for QuestionGroups and for Questions is GONE with the
    /// tables they wrote. Every authoring door is now the pool's: <c>IAdminItemService.CreateAsync</c> /
    /// <c>UpdateAsync</c> for an item, <c>BindItemAsync</c> for a position, and these two for a group.
    /// No route called the removed members (they went in #265), so nothing a learner or an admin could
    /// reach has changed.
    /// </para>
    /// </summary>
    Task UpdateExamStudioGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default);
    Task DeleteExamStudioGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default);

    Task<AdminExamSectionAudioDto> SaveSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, CancellationToken cancellationToken = default);
    Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default);
    Task<ExamStudioDetailDto> GetExamStudioAsync(int examId, CancellationToken cancellationToken = default);
    Task<ExamAuditResultDto> AuditExamPublishReadinessAsync(int examId, CancellationToken cancellationToken = default);
    Task ReorderSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> CreateExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType, string FileName)> ExportExamPackageAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default);

    /// <summary>#158: the exam as a learner would sit it, with the answer key, without an attempt.</summary>
    Task<ExamPreviewTakeView> GetExamPreviewTakeAsync(int examId, int? questionDisplayOrder, CancellationToken cancellationToken = default);

    /// <summary>
    /// #188: puts an authored item's version on this paper. The item names no exam, so this is the
    /// only way one reaches a sitting, and it is where #157's structure lock and INV-6 apply.
    /// </summary>
    Task<ExamItemBindingDto> BindItemAsync(int examId, BindPoolItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// #225 / S-09: the assembly workbench — per section, what the certificate declares against how
    /// much servable stock the pool can supply, plus the positions the paper already holds.
    /// </summary>
    Task<ExamAssemblyDto> GetExamAssemblyAsync(int examId, CancellationToken cancellationToken = default);
}
