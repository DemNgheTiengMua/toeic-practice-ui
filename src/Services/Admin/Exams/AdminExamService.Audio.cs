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
}
