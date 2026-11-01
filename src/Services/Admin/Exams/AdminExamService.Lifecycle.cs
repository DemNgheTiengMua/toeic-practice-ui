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
            // #157: always a draft. Publishing has one door, POST /{id}/publish, which runs the
            // full audit; a client-supplied status here used to skip it entirely.
            Status = ExamStatus.Draft.ToCode(),
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
        // #157: Status is deliberately not assigned. It reaches 'published' only through
        // PublishExamAsync, which runs the readiness audit first.
        exam.Description = Normalize(request.Description);
        exam.SourceName = Normalize(request.SourceName);
        exam.InternalNote = Normalize(request.InternalNote);
        exam.QuestionCount = request.QuestionCount;
        exam.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        return await GetExamByIdAsync(exam.ExamId, cancellationToken);
    }

    /// <summary>
    /// #157: 'hidden' is the deliberate way off the learner-facing catalog. An exam that has been
    /// sat can no longer be deleted, so withdrawing it has to exist, or the only state an
    /// audited-but-retired exam can reach is 'published'. Withdrawing does not need the readiness
    /// audit: it only ever removes the exam from the catalog.
    /// </summary>
    public async Task<AdminPublishExamResultDto> WithdrawExamAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        exam.Status = ExamStatus.Hidden.ToCode();
        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new AdminPublishExamResultDto(true, "Exam withdrawn from the catalog.", Array.Empty<string>());
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
            // #157: the admin has to fix it from this text, so every blocking issue arrives with its
            // audit code. "Exam is incomplete" alone says nothing about which of the ~26 checks failed.
            var errs = audit.Issues
                .Where(i => i.Severity == "error")
                .Select(i => $"{i.Code}: {i.Message}")
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
}
