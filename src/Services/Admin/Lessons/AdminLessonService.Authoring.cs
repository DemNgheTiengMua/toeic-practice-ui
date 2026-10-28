using System.IO.Compression;
using System.Text.Json;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class AdminLessonService
{

    public async Task<AdminLessonDetailDto> CreateLessonAsync(AdminLessonUpsertDto request, CancellationToken cancellationToken = default)
    {
        await ValidateUpsertAsync(request, cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var lesson = new Lesson
        {
            CertId = request.CertId,
            SkillId = request.SkillId,
            CategoryId = request.CategoryId,
            Title = request.Title.Trim(),
            Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim(),
            EstimatedMinutes = request.EstimatedMinutes,
            DisplayOrder = request.DisplayOrder,
            CreatedAt = clock.GetUtcNow(),
        };

        db.Lessons.Add(lesson);
        await db.SaveChangesAsync(cancellationToken);

        for (int i = 0; i < request.Cards.Count; i++)
        {
            var cardDto = request.Cards[i];
            db.LessonCards.Add(BuildLessonCard(
                lesson.LessonId,
                lesson.CertId,
                cardDto.CardType,
                cardDto.Title,
                cardDto.ContentMarkdown,
                cardDto.QuestionPrompt,
                cardDto.OptionA,
                cardDto.OptionB,
                cardDto.OptionC,
                cardDto.OptionD,
                cardDto.CorrectOption,
                cardDto.Explanation,
                cardDto.Rationale,
                i));
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        log?.LogInformation("Created lesson {LessonId} '{Title}' for Cert {CertId}", lesson.LessonId, lesson.Title, lesson.CertId);
        return (await GetLessonAsync(lesson.LessonId, cancellationToken))!;
    }

    public async Task<AdminLessonDetailDto> UpdateLessonAsync(int lessonId, AdminLessonUpsertDto request, CancellationToken cancellationToken = default)
    {
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.LessonId == lessonId, cancellationToken);
        if (lesson is null)
        {
            throw new AdminLessonException(ApiError.Codes.LessonNotFound, $"Lesson {lessonId} was not found.", 404);
        }

        if (lesson.CertId != request.CertId)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Certificate cannot be changed for an existing lesson.", 400);
        }

        await ValidateUpsertAsync(request, cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        lesson.SkillId = request.SkillId;
        lesson.CategoryId = request.CategoryId;
        lesson.Title = request.Title.Trim();
        lesson.Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim();
        lesson.EstimatedMinutes = request.EstimatedMinutes;
        lesson.DisplayOrder = request.DisplayOrder;

        // Reset LastViewedCardId to avoid FK violations when removing existing cards
        await db.LearnerLessonProgresses
            .Where(p => p.LessonId == lessonId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastViewedCardId, (int?)null), cancellationToken);

        var existingCards = await db.LessonCards.Where(c => c.LessonId == lessonId).ToListAsync(cancellationToken);
        db.LessonCards.RemoveRange(existingCards);
        await db.SaveChangesAsync(cancellationToken);

        for (int i = 0; i < request.Cards.Count; i++)
        {
            var cardDto = request.Cards[i];
            db.LessonCards.Add(BuildLessonCard(
                lesson.LessonId,
                lesson.CertId,
                cardDto.CardType,
                cardDto.Title,
                cardDto.ContentMarkdown,
                cardDto.QuestionPrompt,
                cardDto.OptionA,
                cardDto.OptionB,
                cardDto.OptionC,
                cardDto.OptionD,
                cardDto.CorrectOption,
                cardDto.Explanation,
                cardDto.Rationale,
                i));
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        log?.LogInformation("Updated lesson {LessonId} '{Title}'", lesson.LessonId, lesson.Title);
        return (await GetLessonAsync(lesson.LessonId, cancellationToken))!;
    }

    public async Task DeleteLessonAsync(int lessonId, CancellationToken cancellationToken = default)
    {
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.LessonId == lessonId, cancellationToken);
        if (lesson is null)
        {
            throw new AdminLessonException(ApiError.Codes.LessonNotFound, $"Lesson {lessonId} was not found.", 404);
        }

        var isReferencedInPath = await db.PathModuleItems.AnyAsync(p => p.RefLessonId == lessonId, cancellationToken);
        if (isReferencedInPath)
        {
            throw new AdminLessonException(
                ApiError.Codes.Validation,
                "Cannot delete lesson because it is referenced by an active learning path item. Remove it from the path template first.",
                400);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var progresses = await db.LearnerLessonProgresses.Where(p => p.LessonId == lessonId).ToListAsync(cancellationToken);
        db.LearnerLessonProgresses.RemoveRange(progresses);

        var cards = await db.LessonCards.Where(c => c.LessonId == lessonId).ToListAsync(cancellationToken);
        db.LessonCards.RemoveRange(cards);

        db.Lessons.Remove(lesson);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        log?.LogInformation("Deleted lesson {LessonId}", lessonId);
    }

    private async Task ValidateUpsertAsync(AdminLessonUpsertDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Title is required.", 400);
        }

        if (request.Title.Length > 160)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Title cannot exceed 160 characters.", 400);
        }

        if (request.EstimatedMinutes < 1)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Estimated minutes must be at least 1.", 400);
        }

        if (!await db.Certificates.AnyAsync(c => c.CertId == request.CertId, cancellationToken))
        {
            throw new AdminLessonException(ApiError.Codes.CertificateUnavailable, $"Certificate {request.CertId} not found.", 404);
        }

        if (!await db.Skills.AnyAsync(s => s.SkillId == request.SkillId && s.CertId == request.CertId, cancellationToken))
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Skill does not belong to the selected certificate.", 400);
        }

        if (request.CategoryId.HasValue &&
            !await db.QuestionCategories.AnyAsync(c => c.CategoryId == request.CategoryId.Value && c.CertId == request.CertId, cancellationToken))
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Category does not belong to the selected certificate.", 400);
        }

        if (request.Cards is null || request.Cards.Count == 0)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "At least one card is required.", 400);
        }

        for (int i = 0; i < request.Cards.Count; i++)
        {
            var card = request.Cards[i];
            var cardNum = i + 1;

            if (string.IsNullOrWhiteSpace(card.CardType) || !AllowedCardTypes.Contains(card.CardType.Trim()))
            {
                throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: CardType must be 'rule', 'pitfall', 'example', or 'micro_check'.", 400);
            }

            if (string.IsNullOrWhiteSpace(card.Title))
            {
                throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: Title is required.", 400);
            }

            if (card.Title.Length > 160)
            {
                throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: Title cannot exceed 160 characters.", 400);
            }

            if (string.IsNullOrWhiteSpace(card.ContentMarkdown))
            {
                throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: ContentMarkdown is required.", 400);
            }

            if (string.Equals(card.CardType.Trim(), "micro_check", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(card.QuestionPrompt))
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: QuestionPrompt is required for micro_check.", 400);
                }

                if (string.IsNullOrWhiteSpace(card.OptionA) || card.OptionA.Length > 250)
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: OptionA is required and cannot exceed 250 characters.", 400);
                }

                if (string.IsNullOrWhiteSpace(card.OptionB) || card.OptionB.Length > 250)
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: OptionB is required and cannot exceed 250 characters.", 400);
                }

                if (string.IsNullOrWhiteSpace(card.OptionC) || card.OptionC.Length > 250)
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: OptionC is required and cannot exceed 250 characters.", 400);
                }

                if (string.IsNullOrWhiteSpace(card.OptionD) || card.OptionD.Length > 250)
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: OptionD is required and cannot exceed 250 characters.", 400);
                }

                var correct = card.CorrectOption?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(correct) || correct is not ("A" or "B" or "C" or "D"))
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: CorrectOption must be one of 'A', 'B', 'C', or 'D'.", 400);
                }

                if (string.IsNullOrWhiteSpace(card.Explanation))
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: Explanation is required for micro_check.", 400);
                }
            }
        }
    }

    private static LessonCard BuildLessonCard(
        int lessonId,
        int certId,
        string cardType,
        string title,
        string contentMarkdown,
        string? questionPrompt,
        string? optionA,
        string? optionB,
        string? optionC,
        string? optionD,
        string? correctOption,
        string? explanation,
        string? rationale,
        int displayOrder) =>
        new()
        {
            LessonId = lessonId,
            CertId = certId,
            CardType = cardType.Trim().ToLowerInvariant(),
            Title = title.Trim(),
            ContentMarkdown = contentMarkdown.Trim(),
            QuestionPrompt = string.IsNullOrWhiteSpace(questionPrompt) ? null : questionPrompt.Trim(),
            OptionA = string.IsNullOrWhiteSpace(optionA) ? null : optionA.Trim(),
            OptionB = string.IsNullOrWhiteSpace(optionB) ? null : optionB.Trim(),
            OptionC = string.IsNullOrWhiteSpace(optionC) ? null : optionC.Trim(),
            OptionD = string.IsNullOrWhiteSpace(optionD) ? null : optionD.Trim(),
            CorrectOption = string.IsNullOrWhiteSpace(correctOption) ? null : correctOption.Trim().ToUpperInvariant(),
            Explanation = string.IsNullOrWhiteSpace(explanation) ? null : explanation.Trim(),
            Rationale = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim(),
            DisplayOrder = displayOrder,
        };
}
