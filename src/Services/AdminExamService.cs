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
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class AdminExamException(string code, string message, int status = 400) : Exception(message)
{
    public string Code => code;
    public int Status => status;
}

public sealed class ExamNotFoundException(int examId)
    : AdminExamException(ApiError.Codes.ExamNotFound, $"Exam with id {examId} not found.", 404);

public sealed class QuestionNotFoundException(int questionId)
    : AdminExamException(ApiError.Codes.QuestionNotFound, $"Question with id {questionId} not found.", 404);

public sealed class QuestionGroupNotFoundException(int groupId)
    : AdminExamException(ApiError.Codes.QuestionGroupNotFound, $"Question group with id {groupId} not found.", 404);

public interface IAdminExamService
{
    Task<AdminExamListSummaryDto> GetExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminExamListSummaryDto> GetExamsAsync(string? q, string? status, int? certId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetExamsAsync(q, status, certId, null, page, pageSize, cancellationToken);
    Task<AdminExamDetailDto> GetExamByIdAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminExamDetailDto> CreateExamAsync(CreateAdminExamRequest request, CancellationToken cancellationToken = default);
    Task<AdminExamDetailDto> UpdateExamAsync(int examId, UpdateAdminExamRequest request, CancellationToken cancellationToken = default);
    Task<AdminPublishExamResultDto> PublishExamAsync(int examId, CancellationToken cancellationToken = default);
    Task DeleteExamAsync(int examId, CancellationToken cancellationToken = default);

    Task<AdminExamQuestionsDto> GetExamQuestionsAsync(int examId, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> CreateQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> UpdateQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default);
    Task DeleteQuestionGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default);

    Task<AdminQuestionDto> CreateQuestionAsync(int examId, CreateQuestionRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionDto> UpdateQuestionAsync(int questionId, UpdateQuestionRequest request, CancellationToken cancellationToken = default);
    Task DeleteQuestionAsync(int questionId, CancellationToken cancellationToken = default);

    Task<AdminExamSectionAudioDto> SaveSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, CancellationToken cancellationToken = default);
    Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default);
    Task<ExamStudioDetailDto> GetExamStudioAsync(int examId, CancellationToken cancellationToken = default);
    Task<ExamAuditResultDto> AuditExamPublishReadinessAsync(int examId, CancellationToken cancellationToken = default);
    Task ReorderSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default);
    Task<AdminQuestionGroupDto> CreateExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType, string FileName)> ExportExamPackageAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default);
}

public sealed class AdminExamService(CompanionDbContext db, TimeProvider clock, IFileStore? fileStore = null) : IAdminExamService
{
    public Task<AdminExamListSummaryDto> GetExamsAsync(string? q, string? status, int? certId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetExamsAsync(q, status, certId, null, page, pageSize, cancellationToken);

    public async Task<AdminExamListSummaryDto> GetExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Exams.AsNoTracking().Include(e => e.Cert).Where(e => !e.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var trimmedQ = q.Trim();
            query = query.Where(e => e.Name.Contains(trimmedQ) || (e.SourceName != null && e.SourceName.Contains(trimmedQ)));
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (EnumCodes.TryParse<ExamStatus>(status, out var parsedStatus))
            {
                var code = parsedStatus.ToCode();
                query = query.Where(e => e.Status == code);
            }
            else
            {
                var lower = status.Trim().ToLowerInvariant();
                query = query.Where(e => e.Status == lower);
            }
        }

        if (certId.HasValue)
        {
            query = query.Where(e => e.CertId == certId.Value);
        }

        if (!string.IsNullOrWhiteSpace(purpose) && !purpose.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = purpose.Trim().ToLowerInvariant();
            if (normalized == "practice")
            {
                query = query.Where(e => e.Purpose == "speaking_practice" || e.Purpose == "writing_practice");
            }
            else if (EnumCodes.TryParse<ExamPurpose>(normalized, out var parsedPurpose))
            {
                var code = parsedPurpose.ToCode();
                query = query.Where(e => e.Purpose == code);
            }
            else
            {
                query = query.Where(e => e.Purpose == normalized);
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var examEntities = await query
            .OrderByDescending(e => e.UpdatedAt ?? e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var examIds = examEntities.Select(e => e.ExamId).ToList();
        var certIds = examEntities.Select(e => e.CertId).Distinct().ToList();

        var questionsCountByExam = await db.Questions.AsNoTracking()
            .Where(q => examIds.Contains(q.ExamId))
            .GroupBy(q => q.ExamId)
            .Select(g => new { ExamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ExamId, x => x.Count, cancellationToken);

        var finishedAttemptsByExam = await db.ExamAttempts.AsNoTracking()
            .Where(a => examIds.Contains(a.ExamId) && (a.Status == "submitted" || a.Status == "graded"))
            .GroupBy(a => a.ExamId)
            .Select(g => new { ExamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ExamId, x => x.Count, cancellationToken);

        var listeningSectionsByCert = await db.Sections.AsNoTracking()
            .Where(s => certIds.Contains(s.CertId) && s.Modality == "listening")
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var audiosByExam = await db.ExamSectionAudios.AsNoTracking()
            .Where(a => examIds.Contains(a.ExamId))
            .ToListAsync(cancellationToken);

        var items = new List<AdminExamListItemDto>(examEntities.Count);
        foreach (var exam in examEntities)
        {
            var actualCount = questionsCountByExam.GetValueOrDefault(exam.ExamId, 0);
            var finishedAttempts = finishedAttemptsByExam.GetValueOrDefault(exam.ExamId, 0);

            var listeningSections = listeningSectionsByCert.Where(s => s.CertId == exam.CertId).ToList();
            var examAudios = audiosByExam.Where(a => a.ExamId == exam.ExamId).ToList();

            string audioStatus;
            if (listeningSections.Count == 0 || exam.Purpose == "speaking_practice" || exam.Purpose == "writing_practice")
            {
                audioStatus = "N/A";
            }
            else
            {
                var uploadedCount = examAudios.Count(a => listeningSections.Any(ls => ls.SectionId == a.SectionId));
                if (uploadedCount == listeningSections.Count)
                {
                    audioStatus = $"{listeningSections.Count} parts complete";
                }
                else if (uploadedCount == 0)
                {
                    audioStatus = "No audio";
                }
                else
                {
                    var missing = listeningSections.FirstOrDefault(ls => !examAudios.Any(a => a.SectionId == ls.SectionId));
                    audioStatus = $"{missing?.Name ?? "Audio"} missing";
                }
            }

            items.Add(new AdminExamListItemDto(
                exam.ExamId,
                exam.CertId,
                exam.Cert.Code,
                exam.Cert.Name,
                exam.Name,
                EnumCodes.Parse<ExamStatusDto>(exam.Status),
                EnumCodes.Parse<ExamPurposeDto>(exam.Purpose),
                exam.DurationMinutes,
                exam.QuestionCount,
                actualCount,
                audioStatus,
                finishedAttempts,
                exam.UpdatedAt,
                exam.CreatedAt,
                exam.SourceName));
        }

        var certificates = await db.Certificates.AsNoTracking()
            .OrderBy(c => c.CertId)
            .Select(c => new AdminCertificateOptionDto(c.CertId, c.Code, c.Name))
            .ToListAsync(cancellationToken);

        return new AdminExamListSummaryDto(items, totalCount, page, pageSize, certificates);
    }

    public async Task<AdminExamDetailDto> GetExamByIdAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .Include(e => e.ExamSectionAudios)
                .ThenInclude(a => a.Section)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var actualQuestionCount = await db.Questions.AsNoTracking().CountAsync(q => q.ExamId == examId, cancellationToken);
        var finishedAttemptCount = await db.ExamAttempts.AsNoTracking().CountAsync(a => a.ExamId == examId && (a.Status == "submitted" || a.Status == "graded"), cancellationToken);

        var sections = await db.Sections.AsNoTracking()
            .Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questionsBySection = await db.Questions.AsNoTracking()
            .Where(q => q.ExamId == examId)
            .GroupBy(q => q.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SectionId, x => x.Count, cancellationToken);

        var sectionProgress = new List<AdminExamSectionProgressDto>(sections.Count);
        foreach (var s in sections)
        {
            var qCount = questionsBySection.GetValueOrDefault(s.SectionId, 0);
            var hasAudio = exam.ExamSectionAudios.Any(a => a.SectionId == s.SectionId && !string.IsNullOrWhiteSpace(a.AudioPath)) || s.Modality != "listening";
            var isComplete = (s.ExpectedQuestionCount == null || qCount >= s.ExpectedQuestionCount) && hasAudio;
            string statusText;
            if (isComplete)
            {
                statusText = "Complete";
            }
            else if (s.ExpectedQuestionCount.HasValue && qCount < s.ExpectedQuestionCount.Value)
            {
                statusText = $"{s.ExpectedQuestionCount.Value - qCount} questions missing";
            }
            else if (!hasAudio)
            {
                statusText = "Audio missing";
            }
            else
            {
                statusText = "In progress";
            }

            sectionProgress.Add(new AdminExamSectionProgressDto(
                s.SectionId,
                s.Code,
                s.Name,
                EnumCodes.Parse<ModalityDto>(s.Modality),
                qCount,
                s.ExpectedQuestionCount,
                hasAudio,
                isComplete,
                statusText));
        }

        var audios = exam.ExamSectionAudios
            .OrderBy(a => a.Section.DisplayOrder)
            .Select(a => new AdminExamSectionAudioDto(
                a.ExamId,
                a.SectionId,
                a.Section.Code,
                a.Section.Name,
                a.AudioPath,
                a.DurationSec,
                a.SizeBytes,
                a.UploadedAt))
            .ToList();

        return new AdminExamDetailDto(
            exam.ExamId,
            exam.CertId,
            exam.Cert.Code,
            exam.Cert.Name,
            exam.Name,
            EnumCodes.Parse<ExamStatusDto>(exam.Status),
            exam.DurationMinutes,
            EnumCodes.Parse<ExamPurposeDto>(exam.Purpose),
            exam.Description,
            exam.SourceName,
            exam.InternalNote,
            exam.QuestionCount,
            actualQuestionCount,
            finishedAttemptCount,
            exam.CreatedAt,
            exam.UpdatedAt,
            audios,
            sectionProgress);
    }

    public async Task<AdminExamDetailDto> CreateExamAsync(CreateAdminExamRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Exam name is required.");
        }

        var trimmedName = request.Name.Trim();
        if (trimmedName.Length > 160)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Exam name cannot exceed 160 characters.");
        }

        if (request.DurationMinutes <= 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Duration must be greater than 0 minutes.");
        }

        if (request.QuestionCount.HasValue && request.QuestionCount.Value <= 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Question count must be greater than 0.");
        }

        var certExists = await db.Certificates.AnyAsync(c => c.CertId == request.CertId, cancellationToken);
        if (!certExists)
        {
            throw new AdminExamException(ApiError.Codes.CertificateUnavailable, $"Certificate with id {request.CertId} not found.", 404);
        }

        var nameExists = await db.Exams.AnyAsync(e => e.CertId == request.CertId && e.Name == trimmedName && !e.IsDeleted, cancellationToken);
        if (nameExists)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "An exam with this name already exists for this certificate.");
        }

        var exam = new Exam
        {
            CertId = request.CertId,
            Name = trimmedName,
            DurationMinutes = request.DurationMinutes,
            Purpose = request.Purpose.ToCode(),
            Status = request.Status.ToCode(),
            Description = Normalize(request.Description),
            SourceName = Normalize(request.SourceName),
            InternalNote = Normalize(request.InternalNote),
            QuestionCount = request.QuestionCount,
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = null,
            IsDeleted = false
        };

        db.Exams.Add(exam);
        await db.SaveChangesAsync(cancellationToken);

        return await GetExamByIdAsync(exam.ExamId, cancellationToken);
    }

    public async Task<AdminExamDetailDto> UpdateExamAsync(int examId, UpdateAdminExamRequest request, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Exam name is required.");
        }

        var trimmedName = request.Name.Trim();
        if (trimmedName.Length > 160)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Exam name cannot exceed 160 characters.");
        }

        if (request.DurationMinutes <= 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Duration must be greater than 0 minutes.");
        }

        if (request.QuestionCount.HasValue && request.QuestionCount.Value <= 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Question count must be greater than 0.");
        }

        var certExists = await db.Certificates.AnyAsync(c => c.CertId == request.CertId, cancellationToken);
        if (!certExists)
        {
            throw new AdminExamException(ApiError.Codes.CertificateUnavailable, $"Certificate with id {request.CertId} not found.", 404);
        }

        var hasFinishedAttempts = await db.ExamAttempts.AnyAsync(a => a.ExamId == examId && (a.Status == "submitted" || a.Status == "graded"), cancellationToken);
        if (hasFinishedAttempts)
        {
            if (exam.CertId != request.CertId)
            {
                throw new AdminExamException(ApiError.Codes.ExamHasFinishedAttempts, "Cannot change certificate of an exam with finished attempts.");
            }

            if (exam.Purpose != request.Purpose.ToCode())
            {
                throw new AdminExamException(ApiError.Codes.ExamHasFinishedAttempts, "Cannot change purpose of an exam with finished attempts.");
            }
        }

        if (exam.CertId != request.CertId)
        {
            var hasExamContent = await db.QuestionGroups.AnyAsync(g => g.ExamId == examId, cancellationToken)
                || await db.ExamSectionAudios.AnyAsync(a => a.ExamId == examId, cancellationToken)
                || await db.ExamAttempts.AnyAsync(a => a.ExamId == examId, cancellationToken);

            if (hasExamContent)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Cannot change certificate of an exam that already contains questions, audio, or attempts.");
            }
        }

        if (exam.Purpose != request.Purpose.ToCode())
        {
            if (request.Purpose == ExamPurposeDto.SpeakingPractice)
            {
                var hasNonSpeaking = await db.QuestionGroups.AnyAsync(g => g.ExamId == examId && g.Section.Modality != "speaking", cancellationToken);
                if (hasNonSpeaking)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, "A speaking-practice exam can only hold speaking sections.");
                }
            }
            else if (request.Purpose == ExamPurposeDto.WritingPractice)
            {
                var hasNonWriting = await db.QuestionGroups.AnyAsync(g => g.ExamId == examId && g.Section.Modality != "writing", cancellationToken);
                if (hasNonWriting)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, "A writing-practice exam can only hold writing sections.");
                }
            }
        }

        var nameExists = await db.Exams.AnyAsync(e => e.CertId == request.CertId && e.Name == trimmedName && !e.IsDeleted && e.ExamId != examId, cancellationToken);
        if (nameExists)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "An exam with this name already exists for this certificate.");
        }

        exam.CertId = request.CertId;
        exam.Name = trimmedName;
        exam.DurationMinutes = request.DurationMinutes;
        exam.Purpose = request.Purpose.ToCode();
        exam.Status = request.Status.ToCode();
        exam.Description = Normalize(request.Description);
        exam.SourceName = Normalize(request.SourceName);
        exam.InternalNote = Normalize(request.InternalNote);
        exam.QuestionCount = request.QuestionCount;
        exam.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        return await GetExamByIdAsync(exam.ExamId, cancellationToken);
    }

    public async Task<AdminPublishExamResultDto> PublishExamAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var audit = await AuditExamPublishReadinessAsync(examId, cancellationToken);
        if (!audit.CanPublish || audit.BlockingErrorCount > 0)
        {
            var errs = audit.Issues
                .Where(i => i.Severity == "error")
                .Select(i => i.Message)
                .Concat(audit.MissingItems)
                .Distinct()
                .ToList();
            throw new AdminExamException(ApiError.Codes.ExamIncomplete, string.Join("; ", errs), 400);
        }

        exam.Status = ExamStatus.Published.ToCode();
        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new AdminPublishExamResultDto(true, "Exam published successfully.", Array.Empty<string>());
    }

    public async Task DeleteExamAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var hasFinishedAttempts = await db.ExamAttempts.AnyAsync(a => a.ExamId == examId && (a.Status == "submitted" || a.Status == "graded"), cancellationToken);
        if (hasFinishedAttempts)
        {
            throw new AdminExamException(ApiError.Codes.ExamHasFinishedAttempts, "Cannot delete an exam that has recorded finished learner attempts.", 400);
        }

        var hasActiveAttempts = await db.ExamAttempts.AnyAsync(a => a.ExamId == examId && a.Status == "in_progress", cancellationToken);
        if (hasActiveAttempts)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Cannot delete an exam that has attempts currently in progress.", 400);
        }

        exam.IsDeleted = true;
        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminExamQuestionsDto> GetExamQuestionsAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var groups = await db.QuestionGroups.AsNoTracking()
            .Include(g => g.Section)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.Category)
            .Where(g => g.ExamId == examId)
            .OrderBy(g => g.DisplayOrder)
            .ToListAsync(cancellationToken);

        var allQuestionIds = groups.SelectMany(g => g.QuestionGroups.Select(q => q.QuestionId)).ToList();
        var answeredQuestionIds = new HashSet<int>(await db.AttemptAnswers.AsNoTracking()
            .Where(a => allQuestionIds.Contains(a.QuestionId))
            .Select(a => a.QuestionId)
            .Concat(db.FreeResponses.AsNoTracking().Where(r => allQuestionIds.Contains(r.QuestionId)).Select(r => r.QuestionId))
            .ToListAsync(cancellationToken));

        var groupDtos = groups.Select(g => new AdminQuestionGroupDto(
            g.GroupId,
            g.ExamId,
            g.CertId,
            g.SectionId,
            g.Section.Code,
            g.Section.Name,
            EnumCodes.Parse<ModalityDto>(g.Section.Modality),
            g.Passage,
            g.AudioPath,
            g.AudioStartMs,
            g.AudioEndMs,
            g.ImagePath,
            g.ImageAlt,
            g.Transcript,
            g.PrepSeconds,
            g.ResponseSeconds,
            g.DisplayOrder,
            g.QuestionGroups.OrderBy(q => q.DisplayOrder).Select(q => new AdminQuestionDto(
                q.QuestionId,
                q.GroupId,
                q.ExamId,
                q.SectionId,
                g.Section.Code,
                g.Section.Name,
                q.SkillId,
                g.Section.Code,
                q.Stem,
                q.DifficultyLevel,
                EnumCodes.Parse<QuestionTypeDto>(q.QuestionType),
                q.Explanation,
                q.CategoryId,
                q.Category?.Name,
                q.PrepSeconds,
                q.ResponseSeconds,
                q.MinWords,
                q.MaxWords,
                q.Hint,
                q.DisplayOrder,
                q.UpdatedAt,
                q.QuestionOptionQuestionNavigations.OrderBy(o => o.Label).Select(o => new AdminQuestionOptionDto(
                    o.OptionId,
                    o.QuestionId,
                    o.Label,
                    o.Text,
                    o.IsCorrect,
                    o.Rationale)).ToList(),
                answeredQuestionIds.Contains(q.QuestionId),
                MaxSelections: q.MaxSelections,
                AllowNumber: q.AllowNumber,
                AcceptedAnswersJson: q.AcceptedAnswersJson,
                RequiredKeywords: q.RequiredKeywords)).ToList(),
            UnitNumber: g.UnitNumber
        )).ToList();

        var availableSections = await db.Sections.AsNoTracking()
            .Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new AdminSectionOptionDto(
                s.SectionId,
                s.Code,
                s.Name,
                EnumCodes.Parse<ModalityDto>(s.Modality),
                s.ExpectedQuestionCount,
                s.DisplayOrder))
            .ToListAsync(cancellationToken);

        var availableCategories = await db.QuestionCategories.AsNoTracking()
            .Where(c => c.CertId == exam.CertId)
            .OrderBy(c => c.Code)
            .Select(c => new AdminQuestionCategoryOptionDto(c.CategoryId, c.Code, c.Name))
            .ToListAsync(cancellationToken);

        return new AdminExamQuestionsDto(
            exam.ExamId,
            exam.Name,
            exam.CertId,
            exam.Cert.Code,
            groupDtos,
            availableSections,
            availableCategories);
    }

    public async Task<AdminQuestionGroupDto> CreateQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var section = await db.Sections.FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, $"Section with id {request.SectionId} not found for certificate {exam.CertId}.");
        }

        ValidateQuestionGroup(exam.Purpose, section.Modality, request.ImagePath, request.ImageAlt, request.AudioStartMs, request.AudioEndMs, request.PrepSeconds, request.ResponseSeconds);

        var group = new QuestionGroup
        {
            ExamId = examId,
            CertId = exam.CertId,
            SectionId = request.SectionId,
            Passage = Normalize(request.Passage),
            AudioPath = Normalize(request.AudioPath),
            AudioStartMs = request.AudioStartMs,
            AudioEndMs = request.AudioEndMs,
            ImagePath = Normalize(request.ImagePath),
            ImageAlt = Normalize(request.ImageAlt),
            Transcript = Normalize(request.Transcript),
            PrepSeconds = request.PrepSeconds,
            ResponseSeconds = request.ResponseSeconds,
            DisplayOrder = request.DisplayOrder,
            UnitNumber = request.UnitNumber > 0 ? request.UnitNumber : 1
        };

        db.QuestionGroups.Add(group);
        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new AdminQuestionGroupDto(
            group.GroupId,
            group.ExamId,
            group.CertId,
            group.SectionId,
            section.Code,
            section.Name,
            EnumCodes.Parse<ModalityDto>(section.Modality),
            group.Passage,
            group.AudioPath,
            group.AudioStartMs,
            group.AudioEndMs,
            group.ImagePath,
            group.ImageAlt,
            group.Transcript,
            group.PrepSeconds,
            group.ResponseSeconds,
            group.DisplayOrder,
            Array.Empty<AdminQuestionDto>(),
            group.UnitNumber);
    }

    public async Task<AdminQuestionGroupDto> UpdateQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var group = await db.QuestionGroups
            .FirstOrDefaultAsync(g => g.GroupId == groupId && g.ExamId == examId, cancellationToken);

        if (group == null)
        {
            throw new QuestionGroupNotFoundException(groupId);
        }

        var section = await db.Sections.FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, $"Section with id {request.SectionId} not found for certificate {exam.CertId}.");
        }

        ValidateQuestionGroup(exam.Purpose, section.Modality, request.ImagePath, request.ImageAlt, request.AudioStartMs, request.AudioEndMs, request.PrepSeconds, request.ResponseSeconds);

        group.SectionId = request.SectionId;
        group.Passage = Normalize(request.Passage);
        group.AudioPath = Normalize(request.AudioPath);
        group.AudioStartMs = request.AudioStartMs;
        group.AudioEndMs = request.AudioEndMs;
        group.ImagePath = Normalize(request.ImagePath);
        group.ImageAlt = Normalize(request.ImageAlt);
        group.Transcript = Normalize(request.Transcript);
        group.PrepSeconds = request.PrepSeconds;
        group.ResponseSeconds = request.ResponseSeconds;
        group.DisplayOrder = request.DisplayOrder;
        group.UnitNumber = request.UnitNumber > 0 ? request.UnitNumber : 1;

        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new AdminQuestionGroupDto(
            group.GroupId,
            group.ExamId,
            group.CertId,
            group.SectionId,
            section.Code,
            section.Name,
            EnumCodes.Parse<ModalityDto>(section.Modality),
            group.Passage,
            group.AudioPath,
            group.AudioStartMs,
            group.AudioEndMs,
            group.ImagePath,
            group.ImageAlt,
            group.Transcript,
            group.PrepSeconds,
            group.ResponseSeconds,
            group.DisplayOrder,
            Array.Empty<AdminQuestionDto>(),
            group.UnitNumber);
    }

    public async Task DeleteQuestionGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default)
    {
        var group = await db.QuestionGroups
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .FirstOrDefaultAsync(g => g.GroupId == groupId && g.ExamId == examId, cancellationToken);

        if (group == null)
        {
            throw new QuestionGroupNotFoundException(groupId);
        }

        var qIds = await db.Questions.Where(q => q.GroupId == groupId).Select(q => q.QuestionId).ToListAsync(cancellationToken);
        var hasAnswers = await db.AttemptAnswers.AnyAsync(a => qIds.Contains(a.QuestionId), cancellationToken)
            || await db.FreeResponses.AnyAsync(r => qIds.Contains(r.QuestionId), cancellationToken);

        if (hasAnswers)
        {
            throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot delete a question group containing questions answered in attempts.", 400);
        }

        foreach (var q in group.QuestionGroups)
        {
            var saved = await db.SavedQuestions.Where(s => s.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
            if (saved.Count > 0) db.SavedQuestions.RemoveRange(saved);

            var pa = await db.PracticeAnswers.Where(p => p.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
            if (pa.Count > 0) db.PracticeAnswers.RemoveRange(pa);

            db.QuestionOptions.RemoveRange(q.QuestionOptionQuestionNavigations);
            db.Questions.Remove(q);
        }

        db.QuestionGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminQuestionDto> CreateQuestionAsync(int examId, CreateQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var group = await db.QuestionGroups
            .Include(g => g.Exam)
            .Include(g => g.Section)
            .FirstOrDefaultAsync(g => g.GroupId == request.GroupId && g.ExamId == examId, cancellationToken);

        if (group == null)
        {
            throw new QuestionGroupNotFoundException(request.GroupId);
        }

        if (!string.IsNullOrWhiteSpace(group.Section.AllowedComponentTypesJson))
        {
            try
            {
                var allowed = JsonSerializer.Deserialize<List<string>>(group.Section.AllowedComponentTypesJson);
                if (allowed != null && allowed.Count > 0 && !allowed.Contains(request.QuestionType.ToCode(), StringComparer.OrdinalIgnoreCase))
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question type '{request.QuestionType.ToCode()}' is not allowed in section '{group.Section.Name}'.");
                }
            }
            catch (JsonException) { }
        }

        ValidateQuestionInputs(request.Stem, request.DifficultyLevel, request.QuestionType, request.PrepSeconds, request.ResponseSeconds, request.MinWords, request.MaxWords, request.Hint, request.Options, request.MaxSelections, request.AllowNumber, request.AcceptedAnswersJson, request.RequiredKeywords);

        if (request.CategoryId.HasValue)
        {
            var catExists = await db.QuestionCategories.AnyAsync(c => c.CategoryId == request.CategoryId.Value && c.CertId == group.CertId, cancellationToken);
            if (!catExists)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Question category not found for certificate.");
            }
        }

        var question = new Question
        {
            GroupId = group.GroupId,
            ExamId = group.ExamId,
            SectionId = group.SectionId,
            SkillId = group.Section.SkillId,
            Stem = request.Stem.Trim(),
            DifficultyLevel = request.DifficultyLevel,
            QuestionType = request.QuestionType.ToCode(),
            Explanation = Normalize(request.Explanation),
            CategoryId = request.CategoryId,
            PrepSeconds = request.PrepSeconds,
            ResponseSeconds = request.ResponseSeconds,
            MinWords = request.MinWords,
            MaxWords = request.MaxWords,
            MaxSelections = request.MaxSelections,
            AllowNumber = request.AllowNumber,
            AcceptedAnswersJson = Normalize(request.AcceptedAnswersJson),
            RequiredKeywords = Normalize(request.RequiredKeywords),
            Hint = Normalize(request.Hint),
            DisplayOrder = request.DisplayOrder,
            UpdatedAt = clock.GetUtcNow()
        };

        db.Questions.Add(question);
        await db.SaveChangesAsync(cancellationToken);

        var createdOptions = new List<QuestionOption>();
        if (request.QuestionType != QuestionTypeDto.FreeResponse && request.Options != null)
        {
            foreach (var opt in request.Options)
            {
                var option = new QuestionOption
                {
                    QuestionId = question.QuestionId,
                    QuestionType = question.QuestionType,
                    Label = opt.Label.Trim().ToUpperInvariant(),
                    Text = opt.Text.Trim(),
                    IsCorrect = opt.IsCorrect,
                    Rationale = Normalize(opt.Rationale)
                };
                db.QuestionOptions.Add(option);
                createdOptions.Add(option);
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        if (request.QuestionType == QuestionTypeDto.FreeResponse && request.CriterionIds != null)
        {
            await SaveSectionCriteriaAsync(
                group.SectionId,
                group.Section.SkillId,
                group.CertId,
                request.CriterionIds,
                cancellationToken);
        }

        group.Exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new AdminQuestionDto(
            question.QuestionId,
            question.GroupId,
            question.ExamId,
            question.SectionId,
            group.Section.Code,
            group.Section.Name,
            question.SkillId,
            group.Section.Code,
            question.Stem,
            question.DifficultyLevel,
            request.QuestionType,
            question.Explanation,
            question.CategoryId,
            null,
            question.PrepSeconds,
            question.ResponseSeconds,
            question.MinWords,
            question.MaxWords,
            question.Hint,
            question.DisplayOrder,
            question.UpdatedAt,
            createdOptions.Select(o => new AdminQuestionOptionDto(o.OptionId, o.QuestionId, o.Label, o.Text, o.IsCorrect, o.Rationale)).ToList(),
            HasAnswers: false,
            MaxSelections: question.MaxSelections,
            AllowNumber: question.AllowNumber,
            AcceptedAnswersJson: question.AcceptedAnswersJson,
            RequiredKeywords: question.RequiredKeywords);
    }

    public async Task<AdminQuestionDto> UpdateQuestionAsync(int questionId, UpdateQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var question = await db.Questions
            .Include(q => q.Group)
                .ThenInclude(g => g.Section)
            .Include(q => q.QuestionOptionQuestionNavigations)
            .FirstOrDefaultAsync(q => q.QuestionId == questionId, cancellationToken);

        if (question == null)
        {
            throw new QuestionNotFoundException(questionId);
        }

        if (!string.IsNullOrWhiteSpace(question.Group.Section.AllowedComponentTypesJson))
        {
            try
            {
                var allowed = JsonSerializer.Deserialize<List<string>>(question.Group.Section.AllowedComponentTypesJson);
                if (allowed != null && allowed.Count > 0 && !allowed.Contains(request.QuestionType.ToCode(), StringComparer.OrdinalIgnoreCase))
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question type '{request.QuestionType.ToCode()}' is not allowed in section '{question.Group.Section.Name}'.");
                }
            }
            catch (JsonException) { }
        }

        ValidateQuestionInputs(request.Stem, request.DifficultyLevel, request.QuestionType, request.PrepSeconds, request.ResponseSeconds, request.MinWords, request.MaxWords, request.Hint, request.Options, request.MaxSelections, request.AllowNumber, request.AcceptedAnswersJson, request.RequiredKeywords);

        var hasAnswers = await db.AttemptAnswers.AnyAsync(a => a.QuestionId == questionId, cancellationToken)
            || await db.FreeResponses.AnyAsync(r => r.QuestionId == questionId, cancellationToken);

        if (hasAnswers && question.QuestionType != request.QuestionType.ToCode())
        {
            throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot change question type of a question with recorded attempt answers.");
        }

        if (request.CategoryId.HasValue)
        {
            var catExists = await db.QuestionCategories.AnyAsync(c => c.CategoryId == request.CategoryId.Value && c.CertId == question.Group.CertId, cancellationToken);
            if (!catExists)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Question category not found for certificate.");
            }
        }

        if (request.GroupId.HasValue && request.GroupId.Value != question.GroupId)
        {
            var newGroup = await db.QuestionGroups
                .Include(g => g.Section)
                .FirstOrDefaultAsync(g => g.GroupId == request.GroupId.Value && g.ExamId == question.ExamId, cancellationToken);

            if (newGroup == null)
            {
                throw new QuestionGroupNotFoundException(request.GroupId.Value);
            }

            if (newGroup.SectionId != question.SectionId)
            {
                if (hasAnswers)
                {
                    throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot move a question with recorded answers to a different section.");
                }

                var hasPracticeAnswers = await db.PracticeAnswers.AnyAsync(p => p.QuestionId == questionId, cancellationToken);
                if (hasPracticeAnswers)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, "Cannot move a question with recorded practice answers to a different section.");
                }
            }

            question.GroupId = newGroup.GroupId;
            question.SectionId = newGroup.SectionId;
            question.SkillId = newGroup.Section.SkillId;
        }

        question.Stem = request.Stem.Trim();
        question.DifficultyLevel = request.DifficultyLevel;
        question.QuestionType = request.QuestionType.ToCode();
        question.Explanation = Normalize(request.Explanation);
        question.CategoryId = request.CategoryId;
        question.PrepSeconds = request.PrepSeconds;
        question.ResponseSeconds = request.ResponseSeconds;
        question.MinWords = request.MinWords;
        question.MaxWords = request.MaxWords;
        question.MaxSelections = request.MaxSelections;
        question.AllowNumber = request.AllowNumber;
        question.AcceptedAnswersJson = Normalize(request.AcceptedAnswersJson);
        question.RequiredKeywords = Normalize(request.RequiredKeywords);
        question.Hint = Normalize(request.Hint);
        question.DisplayOrder = request.DisplayOrder;
        question.UpdatedAt = clock.GetUtcNow();

        if (request.QuestionType != QuestionTypeDto.FreeResponse && request.Options != null)
        {
            var incomingOptionLabels = request.Options.Select(o => o.Label.Trim().ToUpperInvariant()).ToHashSet();
            var optionsToRemove = question.QuestionOptionQuestionNavigations
                .Where(o => !incomingOptionLabels.Contains(o.Label))
                .ToList();

            foreach (var opt in optionsToRemove)
            {
                var optionAnswered = await db.AttemptAnswers.AnyAsync(a => a.SelectedOptionId == opt.OptionId, cancellationToken);
                if (optionAnswered)
                {
                    throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, $"Cannot delete option {opt.Label} because it has recorded learner answers.");
                }

                var practiceAnswersWithOpt = await db.PracticeAnswers.Where(p => p.SelectedOptionId == opt.OptionId).ToListAsync(cancellationToken);
                foreach (var p in practiceAnswersWithOpt)
                {
                    p.SelectedOptionId = null;
                }

                db.QuestionOptions.Remove(opt);
            }

            // Save option removals & prevent UX_Options_OneCorrect duplicate key errors by clearing outgoing correct option first (for non multi_select)
            if (request.QuestionType != QuestionTypeDto.MultiSelect)
            {
                var currentlyCorrect = question.QuestionOptionQuestionNavigations.FirstOrDefault(o => o.IsCorrect);
                var incomingCorrect = request.Options.FirstOrDefault(o => o.IsCorrect);
                if (currentlyCorrect != null && incomingCorrect != null && !currentlyCorrect.Label.Equals(incomingCorrect.Label.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    currentlyCorrect.IsCorrect = false;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            foreach (var optInput in request.Options)
            {
                var label = optInput.Label.Trim().ToUpperInvariant();
                var existingOpt = question.QuestionOptionQuestionNavigations.FirstOrDefault(o => o.Label == label);
                if (existingOpt != null)
                {
                    existingOpt.QuestionType = question.QuestionType;
                    existingOpt.Text = optInput.Text.Trim();
                    existingOpt.IsCorrect = optInput.IsCorrect;
                    existingOpt.Rationale = Normalize(optInput.Rationale);
                }
                else
                {
                    db.QuestionOptions.Add(new QuestionOption
                    {
                        QuestionId = question.QuestionId,
                        QuestionType = question.QuestionType,
                        Label = label,
                        Text = optInput.Text.Trim(),
                        IsCorrect = optInput.IsCorrect,
                        Rationale = Normalize(optInput.Rationale)
                    });
                }
            }
        }
        else if (request.QuestionType == QuestionTypeDto.FreeResponse)
        {
            if (question.QuestionOptionQuestionNavigations.Count > 0)
            {
                db.QuestionOptions.RemoveRange(question.QuestionOptionQuestionNavigations);
            }

            if (request.CriterionIds != null)
            {
                await SaveSectionCriteriaAsync(
                    question.SectionId,
                    question.SkillId,
                    question.Group.CertId,
                    request.CriterionIds,
                    cancellationToken);
            }
        }

        var exam = await db.Exams.FindAsync([question.ExamId], cancellationToken);
        if (exam != null)
        {
            exam.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);

        var finalOptions = await db.QuestionOptions.AsNoTracking()
            .Where(o => o.QuestionId == questionId)
            .OrderBy(o => o.Label)
            .Select(o => new AdminQuestionOptionDto(o.OptionId, o.QuestionId, o.Label, o.Text, o.IsCorrect, o.Rationale))
            .ToListAsync(cancellationToken);

        return new AdminQuestionDto(
            question.QuestionId,
            question.GroupId,
            question.ExamId,
            question.SectionId,
            question.Group.Section.Code,
            question.Group.Section.Name,
            question.SkillId,
            question.Group.Section.Code,
            question.Stem,
            question.DifficultyLevel,
            request.QuestionType,
            question.Explanation,
            question.CategoryId,
            null,
            question.PrepSeconds,
            question.ResponseSeconds,
            question.MinWords,
            question.MaxWords,
            question.Hint,
            question.DisplayOrder,
            question.UpdatedAt,
            finalOptions,
            hasAnswers,
            MaxSelections: question.MaxSelections,
            AllowNumber: question.AllowNumber,
            AcceptedAnswersJson: question.AcceptedAnswersJson,
            RequiredKeywords: question.RequiredKeywords);
    }

    public async Task DeleteQuestionAsync(int questionId, CancellationToken cancellationToken = default)
    {
        var question = await db.Questions
            .Include(q => q.QuestionOptionQuestionNavigations)
            .FirstOrDefaultAsync(q => q.QuestionId == questionId, cancellationToken);

        if (question == null)
        {
            throw new QuestionNotFoundException(questionId);
        }

        var hasAnswers = await db.AttemptAnswers.AnyAsync(a => a.QuestionId == questionId, cancellationToken)
            || await db.FreeResponses.AnyAsync(r => r.QuestionId == questionId, cancellationToken);

        if (hasAnswers)
        {
            throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot delete a question that has recorded learner answers in attempts.", 400);
        }

        var saved = await db.SavedQuestions.Where(s => s.QuestionId == questionId).ToListAsync(cancellationToken);
        if (saved.Count > 0) db.SavedQuestions.RemoveRange(saved);

        var pa = await db.PracticeAnswers.Where(p => p.QuestionId == questionId).ToListAsync(cancellationToken);
        if (pa.Count > 0) db.PracticeAnswers.RemoveRange(pa);

        db.QuestionOptions.RemoveRange(question.QuestionOptionQuestionNavigations);
        db.Questions.Remove(question);

        var exam = await db.Exams.FindAsync([question.ExamId], cancellationToken);
        if (exam != null)
        {
            exam.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminExamSectionAudioDto> SaveSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var section = await db.Sections.FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Section not found for this exam certificate.");
        }

        if (section.Modality != "listening")
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Section audio can only be uploaded for listening sections.");
        }

        var audioPath = Normalize(request.AudioPath);
        if (audioPath == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Audio path is required.");
        }

        if (audioPath.Length > 400)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Audio path cannot exceed 400 characters.");
        }

        if (request.DurationSec <= 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Duration must be greater than 0 seconds.");
        }

        if (request.SizeBytes <= 0 || request.SizeBytes > 62914560)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Audio file size must be between 1 and 60 MB.");
        }

        var existing = await db.ExamSectionAudios.FirstOrDefaultAsync(a => a.ExamId == examId && a.SectionId == request.SectionId, cancellationToken);
        if (existing != null)
        {
            existing.AudioPath = audioPath;
            existing.DurationSec = request.DurationSec;
            existing.SizeBytes = request.SizeBytes;
            existing.UploadedAt = clock.GetUtcNow();
        }
        else
        {
            existing = new ExamSectionAudio
            {
                ExamId = examId,
                SectionId = request.SectionId,
                CertId = exam.CertId,
                AudioPath = audioPath,
                DurationSec = request.DurationSec,
                SizeBytes = request.SizeBytes,
                UploadedAt = clock.GetUtcNow()
            };
            db.ExamSectionAudios.Add(existing);
        }

        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new AdminExamSectionAudioDto(
            existing.ExamId,
            existing.SectionId,
            section.Code,
            section.Name,
            existing.AudioPath,
            existing.DurationSec,
            existing.SizeBytes,
            existing.UploadedAt);
    }

    private static void ValidateQuestionGroup(string examPurpose, string sectionModality, string? imagePath, string? imageAlt, int? audioStartMs, int? audioEndMs, int? prepSeconds, int? responseSeconds)
    {
        if (examPurpose == ExamPurpose.SpeakingPractice.ToCode() && sectionModality != "speaking")
        {
            throw new AdminExamException(ApiError.Codes.Validation, "A speaking-practice exam can only hold speaking sections.");
        }

        if (examPurpose == ExamPurpose.WritingPractice.ToCode() && sectionModality != "writing")
        {
            throw new AdminExamException(ApiError.Codes.Validation, "A writing-practice exam can only hold writing sections.");
        }

        var normImg = Normalize(imagePath);
        var normAlt = Normalize(imageAlt);

        if (normImg != null && normAlt == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "ImageAlt is required when ImagePath is provided.");
        }

        if (normImg != null && normImg.Length > 400)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "ImagePath cannot exceed 400 characters.");
        }

        if (normAlt != null && normAlt.Length > 400)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "ImageAlt cannot exceed 400 characters.");
        }

        if ((audioStartMs.HasValue && !audioEndMs.HasValue) || (!audioStartMs.HasValue && audioEndMs.HasValue))
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Both AudioStartMs and AudioEndMs must be specified together.");
        }

        if (audioStartMs.HasValue && audioEndMs.HasValue)
        {
            if (audioStartMs.Value < 0 || audioEndMs.Value <= audioStartMs.Value)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "AudioStartMs must be >= 0 and AudioEndMs must be > AudioStartMs.");
            }
        }

        if (prepSeconds.HasValue && prepSeconds.Value < 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "PrepSeconds must be greater than or equal to 0.");
        }

        if (responseSeconds.HasValue && responseSeconds.Value <= 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "ResponseSeconds must be greater than 0.");
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task SaveSectionCriteriaAsync(
        int sectionId,
        int skillId,
        int certId,
        IReadOnlyList<int> criterionIds,
        CancellationToken cancellationToken)
    {
        var requestedIds = criterionIds.Distinct().ToHashSet();
        if (requestedIds.Count != criterionIds.Count)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Rubric criteria must not contain duplicates.");
        }

        var validIds = await db.GradingCriteria
            .Where(c => requestedIds.Contains(c.CriterionId) && c.CertId == certId && c.SkillId == skillId)
            .Select(c => c.CriterionId)
            .ToListAsync(cancellationToken);
        if (validIds.Count != requestedIds.Count)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Rubric criteria must belong to the question's certificate and skill.");
        }

        var existing = await db.SectionCriteria
            .Where(sc => sc.SectionId == sectionId)
            .ToListAsync(cancellationToken);
        var validIdSet = validIds.ToHashSet();
        db.SectionCriteria.RemoveRange(existing.Where(sc => !validIdSet.Contains(sc.CriterionId)));

        foreach (var criterionId in validIds)
        {
            if (existing.All(sc => sc.CriterionId != criterionId))
            {
                db.SectionCriteria.Add(new SectionCriterion
                {
                    SectionId = sectionId,
                    CriterionId = criterionId,
                    SkillId = skillId
                });
            }
        }
    }

    private static void ValidateQuestionInputs(
        string stem,
        int difficulty,
        QuestionTypeDto type,
        int? prepSeconds,
        int? responseSeconds,
        int? minWords,
        int? maxWords,
        string? hint,
        IReadOnlyList<AdminQuestionOptionInput>? options,
        int? maxSelections,
        bool allowNumber,
        string? acceptedAnswersJson,
        string? requiredKeywords)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Question stem is required.");
        }

        if (difficulty < 1 || difficulty > 5)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Difficulty level must be between 1 and 5.");
        }

        if (hint != null && hint.Length > 1000)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Hint must not exceed 1000 characters.");
        }

        if (type != QuestionTypeDto.FreeResponse)
        {
            if (prepSeconds.HasValue || responseSeconds.HasValue)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"PrepSeconds and ResponseSeconds must be null for {type} questions.");
            }
        }

        if (type != QuestionTypeDto.FreeResponse && type != QuestionTypeDto.InlineCompletion)
        {
            if (minWords.HasValue || maxWords.HasValue)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"MinWords and MaxWords must be null for {type} questions.");
            }
        }

        if (type == QuestionTypeDto.FreeResponse)
        {
            if (options != null && options.Count > 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Free response questions cannot have options.");
            }

            if (prepSeconds.HasValue && prepSeconds.Value < 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "PrepSeconds must be greater than or equal to 0.");
            }

            if (responseSeconds.HasValue && responseSeconds.Value <= 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "ResponseSeconds must be greater than 0.");
            }

            if (minWords.HasValue && minWords.Value <= 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "MinWords must be greater than 0.");
            }

            if (maxWords.HasValue && maxWords.Value < (minWords ?? 1))
            {
                throw new AdminExamException(ApiError.Codes.Validation, "MaxWords must be greater than or equal to MinWords.");
            }
        }
        else if (type is QuestionTypeDto.Mcq or QuestionTypeDto.Mcq_3)
        {
            if (options == null || options.Count < 2)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"{type} questions must have at least 2 options.");
            }

            var correctCount = options.Count(o => o.IsCorrect);
            if (correctCount != 1)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"{type} questions must have exactly 1 correct option.");
            }

            ValidateOptionEntries(options);
        }
        else if (type == QuestionTypeDto.MultiSelect)
        {
            if (options == null || options.Count < 2)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Multi-select questions must have at least 2 options.");
            }

            var correctCount = options.Count(o => o.IsCorrect);
            if (correctCount < 1)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Multi-select questions must have at least 1 correct option.");
            }

            if (maxSelections.HasValue && maxSelections.Value < 2)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "MaxSelections must be at least 2 when specified.");
            }

            ValidateOptionEntries(options);
        }
        else if (type == QuestionTypeDto.InlineCompletion)
        {
            if (minWords.HasValue && minWords.Value <= 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "MinWords must be greater than 0.");
            }

            if (maxWords.HasValue && maxWords.Value < (minWords ?? 1))
            {
                throw new AdminExamException(ApiError.Codes.Validation, "MaxWords must be greater than or equal to MinWords.");
            }

            if (options != null && options.Count > 0)
            {
                ValidateOptionEntries(options);
            }
        }
        else if (type is QuestionTypeDto.Dropdown or QuestionTypeDto.DragDrop)
        {
            if (options != null && options.Count > 0)
            {
                ValidateOptionEntries(options);
            }
        }
    }

    private static void ValidateOptionEntries(IReadOnlyList<AdminQuestionOptionInput> options)
    {
        foreach (var opt in options)
        {
            if (string.IsNullOrWhiteSpace(opt.Label))
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Option label is required.");
            }

            if (string.IsNullOrWhiteSpace(opt.Text))
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Option text is required.");
            }
        }

        var uniqueLabels = options.Select(o => o.Label.Trim().ToUpperInvariant()).Distinct().Count();
        if (uniqueLabels != options.Count)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Option labels must be unique.");
        }
    }

    public async Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Commit request cannot be null.", 400);
        }

        var exam = await db.Exams.Include(e => e.Cert).FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var section = await db.Sections.FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException("SectionNotFound", $"Section with id {request.SectionId} not found for certificate '{exam.Cert.Name}'.", 404);
        }

        if (request.Questions == null || request.Questions.Count == 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "No questions provided to commit.", 400);
        }

        // Validate each question
        for (var i = 0; i < request.Questions.Count; i++)
        {
            var q = request.Questions[i];
            if (string.IsNullOrWhiteSpace(q.Stem))
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} has an empty stem.", 400);
            }
            if (q.Options == null || q.Options.Count < 2)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have at least 2 options.", 400);
            }
            var correctCount = q.Options.Count(o => o.IsCorrect);
            if (correctCount != 1)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have exactly one correct option (found {correctCount}).", 400);
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var isReplace = string.Equals(request.Mode, "replace", StringComparison.OrdinalIgnoreCase);
        int currentDisplayOrder = 1;

        if (isReplace)
        {
            var existingGroups = await db.QuestionGroups
                .Include(g => g.QuestionGroups)
                    .ThenInclude(q => q.QuestionOptionQuestionNavigations)
                .Where(g => g.ExamId == examId && g.SectionId == request.SectionId)
                .ToListAsync(cancellationToken);

            var existingQuestionIds = existingGroups.SelectMany(g => g.QuestionGroups.Select(q => q.QuestionId)).ToList();

            var hasAnswers = await db.AttemptAnswers.AnyAsync(a => existingQuestionIds.Contains(a.QuestionId), cancellationToken)
                || await db.FreeResponses.AnyAsync(r => existingQuestionIds.Contains(r.QuestionId), cancellationToken);

            if (hasAnswers)
            {
                throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot replace section content because completed or active learner attempts exist.", 400);
            }

            foreach (var g in existingGroups)
            {
                foreach (var q in g.QuestionGroups)
                {
                    var saved = await db.SavedQuestions.Where(s => s.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
                    if (saved.Count > 0) db.SavedQuestions.RemoveRange(saved);

                    var pa = await db.PracticeAnswers.Where(p => p.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
                    if (pa.Count > 0) db.PracticeAnswers.RemoveRange(pa);

                    db.QuestionOptions.RemoveRange(q.QuestionOptionQuestionNavigations);
                    db.Questions.Remove(q);
                }
                db.QuestionGroups.Remove(g);
            }

            await db.SaveChangesAsync(cancellationToken);
            currentDisplayOrder = 1;
        }
        else
        {
            var maxGroupOrder = await db.QuestionGroups
                .Where(g => g.ExamId == examId && g.SectionId == request.SectionId)
                .Select(g => (int?)g.DisplayOrder)
                .MaxAsync(cancellationToken) ?? 0;

            var maxQuestionOrder = await db.Questions
                .Where(q => q.ExamId == examId && q.SectionId == request.SectionId)
                .Select(q => (int?)q.DisplayOrder)
                .MaxAsync(cancellationToken) ?? 0;

            currentDisplayOrder = Math.Max(maxGroupOrder, maxQuestionOrder) + 1;
        }

        var categories = await db.QuestionCategories
            .Where(c => c.CertId == exam.CertId)
            .ToDictionaryAsync(c => c.Code, c => c.CategoryId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var groupsCreated = 0;
        var questionsCreated = 0;

        foreach (var q in request.Questions)
        {
            var group = new QuestionGroup
            {
                ExamId = examId,
                CertId = exam.CertId,
                SectionId = request.SectionId,
                DisplayOrder = currentDisplayOrder,
            };
            db.QuestionGroups.Add(group);
            await db.SaveChangesAsync(cancellationToken);
            groupsCreated++;

            int? categoryId = null;
            if (!string.IsNullOrWhiteSpace(q.CategoryCode) && categories.TryGetValue(q.CategoryCode, out var cid))
            {
                categoryId = cid;
            }

            var question = new Question
            {
                ExamId = examId,
                SectionId = request.SectionId,
                SkillId = section.SkillId,
                GroupId = group.GroupId,
                Stem = q.Stem.Trim(),
                DifficultyLevel = q.DifficultyLevel is >= 1 and <= 5 ? q.DifficultyLevel : 3,
                QuestionType = QuestionType.Mcq.ToCode(),
                Explanation = q.Explanation?.Trim(),
                CategoryId = categoryId,
                DisplayOrder = currentDisplayOrder,
            };
            db.Questions.Add(question);
            await db.SaveChangesAsync(cancellationToken);
            questionsCreated++;

            foreach (var opt in q.Options)
            {
                var option = new QuestionOption
                {
                    QuestionId = question.QuestionId,
                    QuestionType = question.QuestionType,
                    Label = opt.Label.Trim().ToUpperInvariant(),
                    Text = opt.Text.Trim(),
                    IsCorrect = opt.IsCorrect,
                    Rationale = opt.Rationale?.Trim()
                };
                db.QuestionOptions.Add(option);
            }

            currentDisplayOrder++;
        }

        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return new QuickIngestCommitResultDto(
            Success: true,
            QuestionsCommitted: questionsCreated,
            GroupsCreated: groupsCreated,
            Message: $"Successfully committed {questionsCreated} questions to section '{section.Name}'.");
    }

    public async Task<ExamStudioDetailDto> GetExamStudioAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var sections = await db.Sections.AsNoTracking()
            .Include(s => s.Skill)
            .Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var sectionAudios = await db.ExamSectionAudios.AsNoTracking()
            .Where(a => a.ExamId == examId)
            .ToListAsync(cancellationToken);

        var groups = await db.QuestionGroups.AsNoTracking()
            .Include(g => g.Section)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.Category)
            .Where(g => g.ExamId == examId)
            .OrderBy(g => g.DisplayOrder)
            .ToListAsync(cancellationToken);

        var allQuestionIds = groups.SelectMany(g => g.QuestionGroups.Select(q => q.QuestionId)).ToList();
        var answeredQuestionIds = new HashSet<int>(await db.AttemptAnswers.AsNoTracking()
            .Where(a => allQuestionIds.Contains(a.QuestionId))
            .Select(a => a.QuestionId)
            .Concat(db.FreeResponses.AsNoTracking().Where(r => allQuestionIds.Contains(r.QuestionId)).Select(r => r.QuestionId))
            .ToListAsync(cancellationToken));

        var availableCategories = await db.QuestionCategories.AsNoTracking()
            .Where(c => c.CertId == exam.CertId)
            .OrderBy(c => c.Code)
            .Select(c => new AdminQuestionCategoryOptionDto(c.CategoryId, c.Code, c.Name))
            .ToListAsync(cancellationToken);

        var availableCriteria = await db.GradingCriteria.AsNoTracking()
            .Where(c => c.CertId == exam.CertId)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new ExamStudioCriterionDto(c.CriterionId, c.Code, c.Name, null, c.MaxScore, c.SkillId))
            .ToListAsync(cancellationToken);

        var sectionCriteria = await db.SectionCriteria.AsNoTracking()
            .Where(sc => sections.Select(s => s.SectionId).Contains(sc.SectionId))
            .ToListAsync(cancellationToken);

        var audit = AuditExamPublishReadinessInternal(exam, sections, sectionAudios, groups, sectionCriteria);

        var sectionDtos = new List<ExamStudioSectionDto>();
        foreach (var sec in sections)
        {
            var secAudio = sectionAudios.FirstOrDefault(a => a.SectionId == sec.SectionId);
            var secGroups = groups.Where(g => g.SectionId == sec.SectionId).OrderBy(g => g.DisplayOrder).ToList();
            var secQuestions = secGroups.SelectMany(g => g.QuestionGroups).ToList();

            var groupDtos = new List<ExamStudioGroupDto>();
            foreach (var g in secGroups)
            {
                var qDtos = new List<ExamStudioQuestionDto>();
                foreach (var q in g.QuestionGroups.OrderBy(x => x.DisplayOrder))
                {
                    var opts = q.QuestionOptionQuestionNavigations
                        .OrderBy(o => o.Label)
                        .Select(o => new ExamStudioQuestionOptionDto(o.OptionId, o.QuestionId, o.Label, o.Text, o.IsCorrect, o.Rationale))
                        .ToList();

                    qDtos.Add(new ExamStudioQuestionDto(
                        QuestionId: q.QuestionId,
                        GroupId: q.GroupId,
                        ExamId: q.ExamId,
                        SectionId: q.SectionId,
                        SectionCode: sec.Code,
                        SectionName: sec.Name,
                        SkillId: q.SkillId,
                        SkillCode: sec.Skill?.Code ?? sec.Code,
                        Stem: q.Stem,
                        DifficultyLevel: q.DifficultyLevel,
                        QuestionType: EnumCodes.Parse<QuestionType>(q.QuestionType).MapTo<QuestionTypeDto>(),
                        Explanation: q.Explanation,
                        CategoryId: q.CategoryId,
                        CategoryCode: q.Category?.Code,
                        CategoryName: q.Category?.Name,
                        PrepSeconds: q.PrepSeconds,
                        ResponseSeconds: q.ResponseSeconds,
                        MinWords: q.MinWords,
                        MaxWords: q.MaxWords,
                        Hint: q.Hint,
                        DisplayOrder: q.DisplayOrder,
                        UpdatedAt: q.UpdatedAt,
                        Options: opts,
                        Criteria: availableCriteria
                            .Where(c => c.SkillId == q.SkillId
                                && sectionCriteria.Any(sc => sc.SectionId == q.SectionId && sc.CriterionId == c.CriterionId))
                            .ToList(),
                        HasAnswers: answeredQuestionIds.Contains(q.QuestionId),
                        MaxSelections: q.MaxSelections,
                        AllowNumber: q.AllowNumber,
                        AcceptedAnswersJson: q.AcceptedAnswersJson,
                        RequiredKeywords: q.RequiredKeywords));
                }

                groupDtos.Add(new ExamStudioGroupDto(
                    GroupId: g.GroupId,
                    ExamId: g.ExamId,
                    CertId: g.CertId,
                    SectionId: g.SectionId,
                    SectionCode: sec.Code,
                    SectionName: sec.Name,
                    Modality: EnumCodes.Parse<Modality>(sec.Modality).MapTo<ModalityDto>(),
                    Passage: g.Passage,
                    AudioPath: MediaUrlHelper.NormalizeMediaUrl(g.AudioPath),
                    AudioStartMs: g.AudioStartMs,
                    AudioEndMs: g.AudioEndMs,
                    ImagePath: MediaUrlHelper.NormalizeMediaUrl(g.ImagePath),
                    ImageAlt: g.ImageAlt,
                    Transcript: g.Transcript,
                    PrepSeconds: g.PrepSeconds,
                    ResponseSeconds: g.ResponseSeconds,
                    DisplayOrder: g.DisplayOrder,
                    Questions: qDtos,
                    UnitNumber: g.UnitNumber));
            }

            var hasAudio = secAudio != null || secGroups.Any(g => !string.IsNullOrWhiteSpace(g.AudioPath));
            sectionDtos.Add(new ExamStudioSectionDto(
                SectionId: sec.SectionId,
                CertId: sec.CertId,
                Code: sec.Code,
                Name: sec.Name,
                Modality: EnumCodes.Parse<Modality>(sec.Modality).MapTo<ModalityDto>(),
                ExpectedQuestionCount: sec.ExpectedQuestionCount,
                DisplayOrder: sec.DisplayOrder,
                CurrentQuestionCount: secQuestions.Count,
                HasAudio: hasAudio,
                MasterAudioPath: MediaUrlHelper.NormalizeMediaUrl(secAudio?.AudioPath),
                MasterAudioDurationSec: secAudio?.DurationSec,
                Groups: groupDtos,
                OptionCount: sec.OptionCount,
                ClusterSize: sec.ClusterSize,
                UnitCount: sec.UnitCount,
                HideOptionText: sec.HideOptionText,
                RequirePassage: sec.RequirePassage,
                RequireAudio: sec.RequireAudio,
                RequireImage: sec.RequireImage,
                RequiredKeywordCount: sec.RequiredKeywordCount,
                AllowedComponentTypesJson: sec.AllowedComponentTypesJson,
                LayoutType: sec.LayoutType,
                NavigationMode: sec.NavigationMode,
                TimerPolicyJson: sec.TimerPolicyJson,
                ToolsConfigJson: sec.ToolsConfigJson,
                SkillId: sec.SkillId,
                SkillCode: sec.Skill?.Code ?? sec.Code,
                SkillName: sec.Skill?.Name ?? sec.Name));
        }

        var finishedAttemptCount = await db.ExamAttempts.CountAsync(
            a => a.ExamId == examId && (a.Status == "submitted" || a.Status == "graded"), cancellationToken);

        return new ExamStudioDetailDto(
            ExamId: exam.ExamId,
            CertId: exam.CertId,
            CertCode: exam.Cert.Code,
            CertName: exam.Cert.Name,
            Name: exam.Name,
            Status: EnumCodes.Parse<ExamStatus>(exam.Status).MapTo<ExamStatusDto>(),
            Purpose: EnumCodes.Parse<ExamPurpose>(exam.Purpose).MapTo<ExamPurposeDto>(),
            DurationMinutes: exam.DurationMinutes,
            Description: exam.Description,
            SourceName: exam.SourceName,
            InternalNote: exam.InternalNote,
            QuestionCount: exam.QuestionCount,
            ActualQuestionCount: allQuestionIds.Count,
            FinishedAttemptCount: finishedAttemptCount,
            CreatedAt: exam.CreatedAt,
            UpdatedAt: exam.UpdatedAt,
            Sections: sectionDtos,
            AvailableCategories: availableCategories,
            AvailableCriteria: availableCriteria,
            Audit: audit);
    }

    public async Task<ExamAuditResultDto> AuditExamPublishReadinessAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var sections = await db.Sections.AsNoTracking()
            .Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var sectionAudios = await db.ExamSectionAudios.AsNoTracking()
            .Where(a => a.ExamId == examId)
            .ToListAsync(cancellationToken);

        var groups = await db.QuestionGroups.AsNoTracking()
            .Include(g => g.Section)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Where(g => g.ExamId == examId)
            .OrderBy(g => g.DisplayOrder)
            .ToListAsync(cancellationToken);

        var sectionIds = sections.Select(s => s.SectionId).ToHashSet();
        var sectionCriteria = await db.SectionCriteria.AsNoTracking()
            .Where(sc => sectionIds.Contains(sc.SectionId))
            .ToListAsync(cancellationToken);

        return AuditExamPublishReadinessInternal(exam, sections, sectionAudios, groups, sectionCriteria);
    }

    private static ExamAuditResultDto AuditExamPublishReadinessInternal(
        Exam exam,
        List<Section> sections,
        List<ExamSectionAudio> sectionAudios,
        List<QuestionGroup> groups,
        List<SectionCriterion> sectionCriteria)
    {
        var issues = new List<ExamAuditIssueDto>();
        var missingItems = new List<string>();

        var relevantSections = exam.Purpose switch
        {
            "speaking_practice" => sections.Where(s => s.Modality == "speaking").ToList(),
            "writing_practice" => sections.Where(s => s.Modality == "writing").ToList(),
            _ => sections
        };

        var relevantSectionIds = relevantSections.Select(s => s.SectionId).ToHashSet();
        var relevantGroups = groups.Where(g => relevantSectionIds.Contains(g.SectionId)).ToList();
        var totalQuestions = relevantGroups.Sum(g => g.QuestionGroups.Count);

        var sectionExpectedSum = relevantSections.Where(s => s.ExpectedQuestionCount.HasValue).Sum(s => s.ExpectedQuestionCount!.Value);
        var expectedTotal = sectionExpectedSum > 0 ? sectionExpectedSum : (exam.QuestionCount ?? 0);

        if (groups.Count == 0 || totalQuestions == 0)
        {
            var msg = "Exam must have at least one question group and question.";
            issues.Add(new ExamAuditIssueDto(
                Code: "EXAM_EMPTY",
                Severity: "error",
                Message: msg));
            missingItems.Add(msg);
        }

        if (exam.QuestionCount.HasValue && totalQuestions < exam.QuestionCount.Value)
        {
            var diff = exam.QuestionCount.Value - totalQuestions;
            var msg = $"Exam requires at least {exam.QuestionCount.Value} questions, but only has {totalQuestions} ({diff} missing).";
            issues.Add(new ExamAuditIssueDto(
                Code: "EXAM_QUESTION_COUNT_LOW",
                Severity: "error",
                Message: msg));
            missingItems.Add(msg);
        }

        foreach (var sec in relevantSections)
        {
            var secGroups = groups.Where(g => g.SectionId == sec.SectionId).ToList();
            var secQuestions = secGroups.SelectMany(g => g.QuestionGroups).ToList();

            if (sec.ExpectedQuestionCount.HasValue && sec.ExpectedQuestionCount.Value > 0)
            {
                if (secQuestions.Count < sec.ExpectedQuestionCount.Value)
                {
                    var diff = sec.ExpectedQuestionCount.Value - secQuestions.Count;
                    var msg = $"Section [{sec.Code}] '{sec.Name}' requires {sec.ExpectedQuestionCount.Value} questions, but only has {secQuestions.Count} ({diff} missing).";
                    issues.Add(new ExamAuditIssueDto(
                        Code: "SEC_QUESTION_COUNT_LOW",
                        Severity: "error",
                        Message: msg,
                        SectionId: sec.SectionId,
                        SectionCode: sec.Code,
                        ElementTarget: $"section-link-{sec.SectionId}"));
                    missingItems.Add(msg);
                }
            }

            if (sec.ClusterSize > 1 && secGroups.Count > 0)
            {
                foreach (var g in secGroups)
                {
                    if (g.QuestionGroups.Count != sec.ClusterSize)
                    {
                        var msg = $"Group #{g.DisplayOrder} in [{sec.Code}] requires {sec.ClusterSize} questions per cluster, but has {g.QuestionGroups.Count}.";
                        issues.Add(new ExamAuditIssueDto(
                            Code: "GRP_CLUSTER_SIZE_MISMATCH",
                            Severity: "error",
                            Message: msg,
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            ElementTarget: $"group-card-{g.GroupId}"));
                        missingItems.Add(msg);
                    }
                }
            }

            if (sec.RequirePassage && secGroups.Count > 0)
            {
                foreach (var g in secGroups)
                {
                    if (string.IsNullOrWhiteSpace(g.Passage))
                    {
                        var msg = $"Group #{g.DisplayOrder} in [{sec.Code}] requires a stimulus passage (RequirePassage is enabled).";
                        issues.Add(new ExamAuditIssueDto(
                            Code: "GRP_PASSAGE_MISSING",
                            Severity: "error",
                            Message: msg,
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            ElementTarget: $"group-card-{g.GroupId}"));
                        missingItems.Add(msg);
                    }
                }
            }

            if (sec.RequireImage && secGroups.Count > 0)
            {
                foreach (var g in secGroups)
                {
                    if (string.IsNullOrWhiteSpace(g.ImagePath))
                    {
                        var msg = $"Group #{g.DisplayOrder} in [{sec.Code}] requires an image stimulus (RequireImage is enabled).";
                        issues.Add(new ExamAuditIssueDto(
                            Code: "GRP_IMAGE_MISSING",
                            Severity: "error",
                            Message: msg,
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            ElementTarget: $"group-card-{g.GroupId}"));
                        missingItems.Add(msg);
                    }
                }
            }

            if (sec.RequireAudio || (sec.Modality == "listening" && (secGroups.Count > 0 || (sec.ExpectedQuestionCount.HasValue && sec.ExpectedQuestionCount.Value > 0))))
            {
                var hasSecAudio = sectionAudios.Any(a => a.SectionId == sec.SectionId && !string.IsNullOrWhiteSpace(a.AudioPath));
                var allGroupsAudio = secGroups.Count > 0 && secGroups.All(g => !string.IsNullOrWhiteSpace(g.AudioPath));
                if (!hasSecAudio && !allGroupsAudio)
                {
                    var msg = $"Listening section [{sec.Code}] '{sec.Name}' is missing section audio or question group audio tracks.";
                    issues.Add(new ExamAuditIssueDto(
                        Code: "SEC_AUDIO_MISSING",
                        Severity: "error",
                        Message: msg,
                        SectionId: sec.SectionId,
                        SectionCode: sec.Code,
                        ElementTarget: $"section-link-{sec.SectionId}"));
                    missingItems.Add(msg);
                }
            }

            List<string>? allowedTypes = null;
            if (!string.IsNullOrWhiteSpace(sec.AllowedComponentTypesJson))
            {
                try
                {
                    allowedTypes = JsonSerializer.Deserialize<List<string>>(sec.AllowedComponentTypesJson);
                }
                catch { }
            }

            foreach (var g in secGroups)
            {
                if (!string.IsNullOrWhiteSpace(g.ImagePath) && string.IsNullOrWhiteSpace(g.ImageAlt))
                {
                    issues.Add(new ExamAuditIssueDto(
                        Code: "GRP_IMAGE_ALT_MISSING",
                        Severity: "error",
                        Message: $"Group #{g.DisplayOrder} in [{sec.Code}] has an image but is missing required accessibility alt text (CK_QGroups_ImageAlt).",
                        SectionId: sec.SectionId,
                        SectionCode: sec.Code,
                        GroupId: g.GroupId,
                        ElementTarget: $"group-card-{g.GroupId}"));
                }

                if (g.AudioStartMs.HasValue || g.AudioEndMs.HasValue)
                {
                    if (!g.AudioStartMs.HasValue || !g.AudioEndMs.HasValue || g.AudioStartMs.Value < 0 || g.AudioEndMs.Value <= g.AudioStartMs.Value)
                    {
                        issues.Add(new ExamAuditIssueDto(
                            Code: "GRP_AUDIO_SLICE_INVALID",
                            Severity: "error",
                            Message: $"Group #{g.DisplayOrder} in [{sec.Code}] has invalid audio timestamps: start must be >= 0 and end > start (CK_QGroups_AudioSlice).",
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            ElementTarget: $"group-card-{g.GroupId}"));
                    }
                }

                var master = sectionAudios.FirstOrDefault(a => a.SectionId == sec.SectionId)?.AudioPath;
                var hasMaster = sec.Modality == "listening" && !string.IsNullOrWhiteSpace(master);
                if (hasMaster && !string.IsNullOrWhiteSpace(g.AudioPath)
                    && MediaUrlHelper.NormalizeMediaUrl(g.AudioPath) != MediaUrlHelper.NormalizeMediaUrl(master))
                {
                    issues.Add(new ExamAuditIssueDto(
                        Code: "GRP_AUDIO_OVERRIDDEN",
                        Severity: "warning",
                        Message: $"Group #{g.DisplayOrder} in [{sec.Code}] has its own audio file, but the section's master audio is played instead (any slice is applied to the master). Clear the group's audio path or remove the master audio.",
                        SectionId: sec.SectionId,
                        SectionCode: sec.Code,
                        GroupId: g.GroupId,
                        ElementTarget: $"group-card-{g.GroupId}"));
                }

                if (hasMaster && secGroups.Count > 1
                    && ExamAudio.Resolve(g.AudioPath, g.AudioStartMs, g.AudioEndMs, master)?.StartMs is null)
                {
                    issues.Add(new ExamAuditIssueDto(
                        Code: "GRP_AUDIO_UNSLICED",
                        Severity: "error",
                        Message: $"Group #{g.DisplayOrder} in [{sec.Code}] has no slice of the section's master audio, so learners would hear the whole track. Set its start/end in the audio slicer.",
                        SectionId: sec.SectionId,
                        SectionCode: sec.Code,
                        GroupId: g.GroupId,
                        ElementTarget: $"group-card-{g.GroupId}"));
                }

                foreach (var q in g.QuestionGroups)
                {
                    if (string.IsNullOrWhiteSpace(q.Stem))
                    {
                        issues.Add(new ExamAuditIssueDto(
                            Code: "Q_STEM_EMPTY",
                            Severity: "error",
                            Message: $"Question #{q.DisplayOrder} in [{sec.Code}] has an empty stem.",
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            QuestionId: q.QuestionId,
                            ElementTarget: $"question-card-{q.QuestionId}"));
                    }

                    if (allowedTypes != null && allowedTypes.Count > 0 && !allowedTypes.Contains(q.QuestionType, StringComparer.OrdinalIgnoreCase))
                    {
                        var msg = $"Question #{q.DisplayOrder} in [{sec.Code}] has type '{q.QuestionType}', which is not allowed by the section blueprint (allowed: {sec.AllowedComponentTypesJson}).";
                        issues.Add(new ExamAuditIssueDto(
                            Code: "Q_TYPE_NOT_ALLOWED",
                            Severity: "error",
                            Message: msg,
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            QuestionId: q.QuestionId,
                            ElementTarget: $"question-card-{q.QuestionId}"));
                        missingItems.Add(msg);
                    }

                    if (sec.RequiredKeywordCount > 0 && string.Equals(q.QuestionType, "free_response", StringComparison.OrdinalIgnoreCase))
                    {
                        var kwList = string.IsNullOrWhiteSpace(q.RequiredKeywords) ? new List<string>() : AttemptEngine.ParseKeywords(q.RequiredKeywords);
                        if (kwList.Count < sec.RequiredKeywordCount)
                        {
                            var msg = $"Question #{q.DisplayOrder} in [{sec.Code}] requires at least {sec.RequiredKeywordCount} keywords (found {kwList.Count}).";
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_KEYWORDS_MISSING",
                                Severity: "error",
                                Message: msg,
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                            missingItems.Add(msg);
                        }
                    }

                    if (string.Equals(q.QuestionType, "mcq", StringComparison.OrdinalIgnoreCase) || string.Equals(q.QuestionType, "mcq_3", StringComparison.OrdinalIgnoreCase))
                    {
                        var optCount = q.QuestionOptionQuestionNavigations.Count;
                        if (optCount < 2)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_FEW",
                                Severity: "error",
                                Message: $"{q.QuestionType.ToUpperInvariant()} Question #{q.DisplayOrder} in [{sec.Code}] must have at least 2 options (found {optCount}).",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        if (sec.OptionCount > 0 && optCount != sec.OptionCount)
                        {
                            var msg = $"Question #{q.DisplayOrder} in [{sec.Code}] has {optCount} options (blueprint expects {sec.OptionCount}).";
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_COUNT_MISMATCH",
                                Severity: "warning",
                                Message: msg,
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        var correctCount = q.QuestionOptionQuestionNavigations.Count(o => o.IsCorrect);
                        if (correctCount != 1)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_ONE_CORRECT",
                                Severity: "error",
                                Message: $"{q.QuestionType.ToUpperInvariant()} Question #{q.DisplayOrder} in [{sec.Code}] must have exactly one correct option (found {correctCount}) (UX_Options_OneCorrect).",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }
                    }
                    else if (string.Equals(q.QuestionType, "multi_select", StringComparison.OrdinalIgnoreCase))
                    {
                        var optCount = q.QuestionOptionQuestionNavigations.Count;
                        if (optCount < 2)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_FEW",
                                Severity: "error",
                                Message: $"Multi-select Question #{q.DisplayOrder} in [{sec.Code}] must have at least 2 options (found {optCount}).",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        var correctCount = q.QuestionOptionQuestionNavigations.Count(o => o.IsCorrect);
                        if (correctCount < 1)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_NONE_CORRECT",
                                Severity: "error",
                                Message: $"Multi-select Question #{q.DisplayOrder} in [{sec.Code}] must have at least one correct option.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }
                    }
                    else if (string.Equals(q.QuestionType, "inline_completion", StringComparison.OrdinalIgnoreCase))
                    {
                        var hasAccepted = !string.IsNullOrWhiteSpace(q.AcceptedAnswersJson) || q.QuestionOptionQuestionNavigations.Any(o => o.IsCorrect);
                        if (!hasAccepted)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_ACCEPTED_ANSWERS_MISSING",
                                Severity: "error",
                                Message: $"Inline completion Question #{q.DisplayOrder} in [{sec.Code}] must have at least one accepted answer.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }
                    }
                    else if (string.Equals(q.QuestionType, "dropdown", StringComparison.OrdinalIgnoreCase) || string.Equals(q.QuestionType, "drag_drop", StringComparison.OrdinalIgnoreCase))
                    {
                        var hasAnswers = !string.IsNullOrWhiteSpace(q.AcceptedAnswersJson) || q.QuestionOptionQuestionNavigations.Any(o => o.IsCorrect);
                        if (!hasAnswers)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_NONE_CORRECT",
                                Severity: "error",
                                Message: $"{q.QuestionType} Question #{q.DisplayOrder} in [{sec.Code}] must have at least one correct option or accepted answer.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }
                    }
                    else if (string.Equals(q.QuestionType, "free_response", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!sectionCriteria.Any(sc => sc.SectionId == sec.SectionId && sc.SkillId == q.SkillId))
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_FR_CRITERIA_MISSING",
                                Severity: "error",
                                Message: $"Free response Question #{q.DisplayOrder} in [{sec.Code}] must have at least one rubric criterion assigned to its section.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        if (q.ResponseSeconds is null or <= 0)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_FR_RESPONSE_SEC",
                                Severity: "error",
                                Message: $"Free response Question #{q.DisplayOrder} in [{sec.Code}] must have response seconds > 0.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        if (sec.Modality == "writing"
                            && (q.MinWords is null or <= 0 || q.MaxWords is null or <= 0 || q.MaxWords < q.MinWords))
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_FR_WORD_BOUNDS",
                                Severity: "error",
                                Message: $"Free response Writing question #{q.DisplayOrder} in [{sec.Code}] must have positive minimum and maximum word counts, with max >= min.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        if (sec.Modality == "speaking" && q.PrepSeconds is null or <= 0)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_FR_PREP_SEC",
                                Severity: "error",
                                Message: $"Free response Speaking question #{q.DisplayOrder} in [{sec.Code}] must have preparation seconds > 0.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }
                    }
                }
            }
        }

        var blockingCount = issues.Count(i => i.Severity == "error");
        var warningCount = issues.Count(i => i.Severity == "warning");
        var canPublish = blockingCount == 0 && totalQuestions > 0;

        return new ExamAuditResultDto(
            CanPublish: canPublish,
            TotalQuestions: totalQuestions,
            ExpectedQuestions: expectedTotal > 0 ? expectedTotal : totalQuestions,
            BlockingErrorCount: blockingCount,
            WarningCount: warningCount,
            Issues: issues,
            MissingItems: missingItems);
    }

    public async Task ReorderSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || request.Items == null || request.Items.Count == 0) return;

        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null) throw new ExamNotFoundException(examId);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var sorted = request.Items.OrderBy(i => i.DisplayOrder).ToList();
        var nextOrder = 1;

        foreach (var item in sorted)
        {
            if (item.GroupId.HasValue)
            {
                var grp = await db.QuestionGroups.FirstOrDefaultAsync(g => g.GroupId == item.GroupId.Value && g.ExamId == examId && g.SectionId == request.SectionId, cancellationToken);
                if (grp != null)
                {
                    grp.DisplayOrder = nextOrder;
                }
            }

            if (item.QuestionId.HasValue)
            {
                var q = await db.Questions.FirstOrDefaultAsync(x => x.QuestionId == item.QuestionId.Value && x.ExamId == examId && x.SectionId == request.SectionId, cancellationToken);
                if (q != null)
                {
                    q.DisplayOrder = nextOrder;
                }
            }

            nextOrder++;
        }

        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<AdminQuestionGroupDto> CreateExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null) throw new ExamNotFoundException(examId);

        var section = await db.Sections.Include(s => s.Skill).FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, $"Section with id {request.SectionId} not found for certificate {exam.CertId}.");
        }

        var maxOrder = await db.QuestionGroups
            .Where(g => g.ExamId == examId && g.SectionId == request.SectionId)
            .Select(g => (int?)g.DisplayOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var qMaxOrder = await db.Questions
            .Where(q => q.ExamId == examId && q.SectionId == request.SectionId)
            .Select(q => (int?)q.DisplayOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var isFreeResponse = section.Modality is "speaking" or "writing"
            || (section.AllowedComponentTypesJson != null && section.AllowedComponentTypesJson.Contains("free_response") && !section.AllowedComponentTypesJson.Contains("mcq"));

        var group = new QuestionGroup
        {
            ExamId = examId,
            CertId = exam.CertId,
            SectionId = request.SectionId,
            Passage = Normalize(request.Passage),
            Transcript = Normalize(request.Transcript),
            AudioPath = Normalize(request.AudioPath),
            AudioStartMs = request.AudioStartMs,
            AudioEndMs = request.AudioEndMs,
            DisplayOrder = maxOrder + 1,
            UnitNumber = request.UnitNumber > 0 ? request.UnitNumber : 1
        };

        db.QuestionGroups.Add(group);
        await db.SaveChangesAsync(cancellationToken);

        var count = request.QuestionCount > 0 ? Math.Clamp(request.QuestionCount, 1, 10) : (section.ClusterSize > 0 ? section.ClusterSize : 3);
        var questions = new List<Question>();

        for (var i = 0; i < count; i++)
        {
            string stem;
            if (section.RequireImage)
            {
                stem = "Look at the photograph and choose the statement that best describes what you see in the image.";
            }
            else if (section.HideOptionText)
            {
                stem = "Mark your answer on your answer sheet.";
            }
            else if (isFreeResponse)
            {
                stem = "Respond to the prompt as instructed.";
            }
            else
            {
                stem = $"Question {qMaxOrder + i + 1}";
            }

            var qType = isFreeResponse ? QuestionTypeDto.FreeResponse
                : (section.OptionCount == 3 ? QuestionTypeDto.Mcq_3 : QuestionTypeDto.Mcq);

            var q = new Question
            {
                GroupId = group.GroupId,
                ExamId = examId,
                SectionId = request.SectionId,
                SkillId = section.SkillId,
                Stem = stem,
                DifficultyLevel = 2,
                QuestionType = qType.ToCode(),
                Explanation = section.HideOptionText ? "Option A is the most appropriate response." : "Correct answer option A.",
                PrepSeconds = isFreeResponse ? (section.Modality == "speaking" ? 15 : 60) : null,
                ResponseSeconds = isFreeResponse ? (section.Modality == "speaking" ? 45 : 1800) : null,
                MinWords = section.Modality == "writing" ? 150 : null,
                MaxWords = section.Modality == "writing" ? 300 : null,
                DisplayOrder = qMaxOrder + i + 1
            };
            db.Questions.Add(q);
            questions.Add(q);
        }

        await db.SaveChangesAsync(cancellationToken);

        var questionDtos = new List<AdminQuestionDto>();

        foreach (var q in questions)
        {
            var optionsList = new List<AdminQuestionOptionDto>();
            if (!isFreeResponse)
            {
                var optionLabels = section.OptionCount == 3 ? new[] { "A", "B", "C" } : new[] { "A", "B", "C", "D" };
                foreach (var lbl in optionLabels)
                {
                    var opt = new QuestionOption
                    {
                        QuestionId = q.QuestionId,
                        QuestionType = q.QuestionType,
                        Label = lbl,
                        Text = $"Option {lbl}",
                        IsCorrect = lbl == "A",
                        Rationale = lbl == "A" ? "Correct response" : "Distractor response"
                    };
                    db.QuestionOptions.Add(opt);
                    await db.SaveChangesAsync(cancellationToken);
                    optionsList.Add(new AdminQuestionOptionDto(opt.OptionId, q.QuestionId, opt.Label, opt.Text, opt.IsCorrect, opt.Rationale));
                }
            }

            questionDtos.Add(new AdminQuestionDto(
                q.QuestionId,
                group.GroupId,
                examId,
                request.SectionId,
                section.Code,
                section.Name,
                section.SkillId,
                section.Skill?.Code ?? "skill",
                q.Stem,
                q.DifficultyLevel,
                EnumCodes.Parse<QuestionTypeDto>(q.QuestionType),
                q.Explanation,
                q.CategoryId,
                null,
                q.PrepSeconds,
                q.ResponseSeconds,
                q.MinWords,
                q.MaxWords,
                q.Hint,
                q.DisplayOrder,
                clock.GetUtcNow(),
                optionsList,
                HasAnswers: false,
                MaxSelections: q.MaxSelections,
                AllowNumber: q.AllowNumber,
                AcceptedAnswersJson: q.AcceptedAnswersJson,
                RequiredKeywords: q.RequiredKeywords));
        }

        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new AdminQuestionGroupDto(
            group.GroupId,
            group.ExamId,
            group.CertId,
            group.SectionId,
            section.Code,
            section.Name,
            EnumCodes.Parse<ModalityDto>(section.Modality),
            group.Passage,
            group.AudioPath,
            group.AudioStartMs,
            group.AudioEndMs,
            group.ImagePath,
            group.ImageAlt,
            group.Transcript,
            group.PrepSeconds,
            group.ResponseSeconds,
            group.DisplayOrder,
            questionDtos,
            group.UnitNumber);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> ExportExamPackageAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null) throw new ExamNotFoundException(examId);

        var groups = await db.QuestionGroups.AsNoTracking()
            .Include(g => g.Section)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.Category)
            .Where(g => g.ExamId == examId)
            .OrderBy(g => g.DisplayOrder)
            .ToListAsync(cancellationToken);

        var manifest = new ImportManifest
        {
            Version = "1.0",
            Exam = new ImportManifestExam
            {
                Title = exam.Name,
                Purpose = exam.Purpose,
                DurationMinutes = exam.DurationMinutes,
                SourceName = exam.SourceName,
                Description = exam.Description,
                QuestionCount = exam.QuestionCount
            },
            Sections = groups
                .GroupBy(g => g.Section)
                .OrderBy(s => s.Key.DisplayOrder)
                .Select(sg => new ImportManifestSection
                {
                    SectionCode = sg.Key.Code,
                    Groups = sg.OrderBy(g => g.DisplayOrder).Select(g => new ImportManifestGroup
                    {
                        Passage = g.Passage,
                        AudioFile = g.AudioPath,
                        AudioStartMs = g.AudioStartMs,
                        AudioEndMs = g.AudioEndMs,
                        ImageFile = g.ImagePath,
                        ImageAlt = g.ImageAlt,
                        Transcript = g.Transcript,
                        PrepSeconds = g.PrepSeconds,
                        ResponseSeconds = g.ResponseSeconds,
                        DisplayOrder = g.DisplayOrder,
                        Questions = g.QuestionGroups.OrderBy(q => q.DisplayOrder).Select(q => new ImportManifestQuestion
                        {
                            Stem = q.Stem,
                            DifficultyLevel = q.DifficultyLevel,
                            QuestionType = q.QuestionType,
                            CategoryCode = q.Category?.Code,
                            Explanation = q.Explanation,
                            Hint = q.Hint,
                            PrepSeconds = q.PrepSeconds,
                            ResponseSeconds = q.ResponseSeconds,
                            MinWords = q.MinWords,
                            MaxWords = q.MaxWords,
                            DisplayOrder = q.DisplayOrder,
                            Options = q.QuestionOptionQuestionNavigations.OrderBy(o => o.Label).Select(o => new ImportManifestOption
                            {
                                Label = o.Label,
                                Text = o.Text,
                                IsCorrect = o.IsCorrect,
                                Rationale = o.Rationale
                            }).ToList()
                        }).ToList()
                    }).ToList()
                }).ToList()
        };

        var safeTitle = string.Join("_", exam.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ImportManifest.Json);
            var ms = new MemoryStream(jsonBytes);
            return (ms, "application/json", $"{safeTitle}_manifest.json");
        }
        else
        {
            var zipMs = new MemoryStream();
            using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = archive.CreateEntry("manifest.json");
                using (var entryStream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(entryStream, manifest, ImportManifest.Json, cancellationToken);
                }

                if (fileStore != null)
                {
                    var mediaPaths = groups
                        .SelectMany(g => new[] { g.AudioPath, g.ImagePath })
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Distinct();

                    foreach (var path in mediaPaths)
                    {
                        var cleanPath = path!.TrimStart('/').Replace("media/", "");
                        var media = await fileStore.GetMediaAsync(cleanPath, cancellationToken);
                        if (media != null)
                        {
                            var entryName = Path.GetFileName(cleanPath);
                            var mediaEntry = archive.CreateEntry($"media/{entryName}");
                            using var mediaStream = mediaEntry.Open();
                            await media.Stream.CopyToAsync(mediaStream, cancellationToken);
                        }
                    }
                }
            }

            zipMs.Position = 0;
            return (zipMs, "application/zip", $"{safeTitle}_package.zip");
        }
    }
}
