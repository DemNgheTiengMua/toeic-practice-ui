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

public sealed partial class AdminExamService
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
}
