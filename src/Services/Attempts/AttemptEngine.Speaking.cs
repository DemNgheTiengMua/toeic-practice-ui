using System;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    public async Task<SaveSpeakingAudioResult> SaveSpeakingAudioAsync(
        int attemptId,
        int questionId,
        int userId,
        Stream stream,
        string contentType,
        string fileName,
        int? durationMs,
        CancellationToken cancellationToken = default)
    {
        var (isValid, error) = AudioValidation.Validate(stream.Length, contentType, fileName);
        if (!isValid)
        {
            throw new AttemptEngineException(AttemptEngineException.InvalidOption, error ?? "Invalid audio file.");
        }

        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        var now = DateTimeOffset.UtcNow;
        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        if (now >= effectiveDeadline)
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam attempt has expired.");
        }

        var question = await db.Questions
            .Include(q => q.Section)
            .SingleOrDefaultAsync(q => q.QuestionId == questionId && q.ExamId == attempt.ExamId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question {questionId} not found.");

        if (question.QuestionType != QuestionType.FreeResponse.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Audio response can only be attached to free-response questions.");
        }

        var ext = Path.GetExtension(fileName) ?? ".webm";
        if (fileName?.Contains(FakeAiGrader.SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true)
        {
            ext = $"{FakeAiGrader.SimulateFailMarker}{ext}";
        }
        var relativePath = await _fileStore.SaveAudioAsync(attemptId, questionId, stream, contentType, ext, cancellationToken);

        var fr = await db.FreeResponses
            .SingleOrDefaultAsync(r => r.AttemptId == attemptId && r.QuestionId == questionId, cancellationToken);

        if (fr is null)
        {
            fr = new FreeResponse
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                ExamId = attempt.ExamId,
                SkillId = question.SkillId,
                QuestionType = QuestionType.FreeResponse.ToCode(),
                AudioPath = relativePath,
                DurationMs = durationMs,
                Status = FreeResponseStatus.Draft.ToCode(),
                DraftSavedAt = now,
                SubmittedAt = null
            };
            db.FreeResponses.Add(fr);
        }
        else
        {
            var oldPath = fr.AudioPath;
            fr.AudioPath = relativePath;
            fr.DurationMs = durationMs;
            fr.Status = FreeResponseStatus.Draft.ToCode();
            fr.DraftSavedAt = now;
            fr.SubmittedAt = null;

            if (!string.IsNullOrWhiteSpace(oldPath) && !string.Equals(oldPath, relativePath, StringComparison.OrdinalIgnoreCase))
            {
                await _fileStore.DeleteAudioAsync(oldPath, cancellationToken);
            }
        }

        attempt.LastQuestionId = questionId;
        await db.SaveChangesAsync(cancellationToken);

        return new SaveSpeakingAudioResult(
            fr.FreeResponseId,
            fr.QuestionId,
            relativePath,
            durationMs,
            now);
    }

    public async Task<SaveSpeakingAudioResult> SaveSpeakingAudioAsync(
        int attemptId,
        int questionId,
        int userId,
        string audioPath,
        int? durationMs,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        var now = DateTimeOffset.UtcNow;
        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        if (now >= effectiveDeadline)
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam attempt has expired.");
        }

        var question = await db.Questions
            .Include(q => q.Section)
            .SingleOrDefaultAsync(q => q.QuestionId == questionId && q.ExamId == attempt.ExamId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question {questionId} not found.");

        if (question.QuestionType != QuestionType.FreeResponse.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Audio response can only be attached to free-response questions.");
        }

        var fr = await db.FreeResponses
            .SingleOrDefaultAsync(r => r.AttemptId == attemptId && r.QuestionId == questionId, cancellationToken);

        if (fr is null)
        {
            fr = new FreeResponse
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                ExamId = attempt.ExamId,
                SkillId = question.SkillId,
                QuestionType = QuestionType.FreeResponse.ToCode(),
                AudioPath = audioPath,
                DurationMs = durationMs,
                Status = FreeResponseStatus.Draft.ToCode(),
                DraftSavedAt = now,
                SubmittedAt = null
            };
            db.FreeResponses.Add(fr);
        }
        else
        {
            fr.AudioPath = audioPath;
            fr.DurationMs = durationMs;
            fr.Status = FreeResponseStatus.Draft.ToCode();
            fr.DraftSavedAt = now;
            fr.SubmittedAt = null;
        }

        attempt.LastQuestionId = questionId;
        await db.SaveChangesAsync(cancellationToken);

        return new SaveSpeakingAudioResult(
            fr.FreeResponseId,
            fr.QuestionId,
            audioPath,
            durationMs,
            now);
    }
}
