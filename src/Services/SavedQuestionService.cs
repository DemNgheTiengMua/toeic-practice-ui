using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class SavedQuestionException(string code, string message) : Exception(message)
{
    public const string QuestionNotFound = "question_not_found";
    public const string SavedQuestionNotFound = "saved_question_not_found";

    public string Code { get; } = code;
}

public interface ISavedQuestionService
{
    Task<SavedQuestionsView> GetSavedQuestionsAsync(int userId, SavedQuestionFilterInput filter, CancellationToken cancellationToken = default);

    Task<SavedQuestionItem> GetSavedQuestionAsync(int userId, int questionId, CancellationToken cancellationToken = default);

    Task<SaveQuestionResult> SaveQuestionAsync(int userId, int questionId, string source = "manual", CancellationToken cancellationToken = default);

    Task<SaveQuestionResult> UnsaveQuestionAsync(int userId, int questionId, CancellationToken cancellationToken = default);

    Task<bool> IsQuestionSavedAsync(int userId, int questionId, CancellationToken cancellationToken = default);
}

public sealed class SavedQuestionService(
    CompanionDbContext db,
    ILearnerService learners) : ISavedQuestionService
{
    private static readonly System.Linq.Expressions.Expression<Func<SavedQuestion, SavedQuestionItem>> QuestionItemProjection = sq => new SavedQuestionItem(
        sq.QuestionId,
        sq.Source,
        sq.SavedAt,
        sq.Question.SectionId,
        sq.Question.Section.Code,
        sq.Question.Section.Name,
        sq.Question.CategoryId,
        sq.Question.Category != null ? sq.Question.Category.Name : null,
        sq.Question.Stem,
        sq.Question.Group.Passage,
        sq.Question.Group.ImagePath,
        sq.Question.Group.AudioPath,
        sq.Question.Hint,
        sq.Question.Explanation,
        sq.Question.Group.Transcript,
        sq.Question.QuestionOptionQuestionNavigations
            .OrderBy(o => o.Label)
            .Select(o => new SavedQuestionOption(o.OptionId, o.Label, o.Text, o.IsCorrect))
            .ToList());

    public async Task<SavedQuestionsView> GetSavedQuestionsAsync(int userId, SavedQuestionFilterInput filter, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .Select(c => new CertificateOption(c.Code, c.Name))
            .SingleAsync(cancellationToken);

        // Strict isolation: only this user's items for this certificate
        var baseQuery = db.SavedQuestions
            .AsNoTracking()
            .Where(sq => sq.UserId == userId && sq.Question.Section.CertId == activeCertId);

        var totalSaved = await baseQuery.CountAsync(cancellationToken);
        var manualSaved = await baseQuery.CountAsync(sq => sq.Source == "manual", cancellationToken);
        var incorrectSaved = await baseQuery.CountAsync(sq => sq.Source == "incorrect", cancellationToken);

        var sectionData = await baseQuery
            .Select(sq => new
            {
                sq.Question.SectionId,
                sq.Question.Section.Code,
                sq.Question.Section.Name,
                sq.Question.Section.DisplayOrder
            })
            .ToListAsync(cancellationToken);

        var sectionFilters = sectionData
            .GroupBy(s => new { s.SectionId, s.Code, s.Name, s.DisplayOrder })
            .OrderBy(g => g.Key.DisplayOrder)
            .ThenBy(g => g.Key.Code)
            .Select(g => new SavedQuestionSectionFilter(g.Key.SectionId, g.Key.Code, g.Key.Name, g.Count()))
            .ToList();

        var categoryData = await baseQuery
            .Where(sq => sq.Question.CategoryId != null)
            .Select(sq => new { CategoryId = sq.Question.CategoryId!.Value, Name = sq.Question.Category!.Name })
            .ToListAsync(cancellationToken);

        var categoryFilters = categoryData
            .GroupBy(c => new { c.CategoryId, c.Name })
            .OrderBy(g => g.Key.Name)
            .Select(g => new SavedQuestionCategoryFilter(g.Key.CategoryId, g.Key.Name, g.Count()))
            .ToList();

        var query = baseQuery;
        if (!string.IsNullOrWhiteSpace(filter.Source) && (filter.Source == "manual" || filter.Source == "incorrect"))
        {
            query = query.Where(sq => sq.Source == filter.Source);
        }

        if (filter.SectionId.HasValue)
        {
            query = query.Where(sq => sq.Question.SectionId == filter.SectionId.Value);
        }

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(sq => sq.Question.CategoryId == filter.CategoryId.Value);
        }

        var questions = await query
            .OrderByDescending(sq => sq.SavedAt)
            .Select(QuestionItemProjection)
            .ToListAsync(cancellationToken);

        return new SavedQuestionsView(
            cert,
            totalSaved,
            manualSaved,
            incorrectSaved,
            questions,
            sectionFilters,
            categoryFilters);
    }

    public async Task<SavedQuestionItem> GetSavedQuestionAsync(int userId, int questionId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        return await db.SavedQuestions
            .AsNoTracking()
            .Where(sq => sq.UserId == userId && sq.QuestionId == questionId && sq.Question.Section.CertId == activeCertId)
            .Select(QuestionItemProjection)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SavedQuestionException(SavedQuestionException.SavedQuestionNotFound, $"Question {questionId} is not in your saved bank.");
    }

    public async Task<SaveQuestionResult> SaveQuestionAsync(int userId, int questionId, string source = "manual", CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var questionExists = await db.Questions
            .AnyAsync(q => q.QuestionId == questionId && q.Section.CertId == activeCertId, cancellationToken);
        if (!questionExists)
        {
            throw new SavedQuestionException(SavedQuestionException.QuestionNotFound, $"Question {questionId} does not exist.");
        }

        var normalizedSource = source == "incorrect" ? "incorrect" : "manual";
        var existing = await db.SavedQuestions
            .FirstOrDefaultAsync(sq => sq.UserId == userId && sq.QuestionId == questionId, cancellationToken);

        if (existing != null)
        {
            // Idempotent: If user manually saves an automatically saved question, upgrade source to manual
            if (normalizedSource == "manual" && existing.Source == "incorrect")
            {
                existing.Source = "manual";
                await db.SaveChangesAsync(cancellationToken);
            }

            return new SaveQuestionResult(true, existing.Source, existing.SavedAt);
        }

        var saved = new SavedQuestion
        {
            UserId = userId,
            QuestionId = questionId,
            Source = normalizedSource,
            SavedAt = DateTimeOffset.UtcNow
        };

        db.SavedQuestions.Add(saved);
        await db.SaveChangesAsync(cancellationToken);

        return new SaveQuestionResult(true, saved.Source, saved.SavedAt);
    }

    public async Task<SaveQuestionResult> UnsaveQuestionAsync(int userId, int questionId, CancellationToken cancellationToken = default)
    {
        var existing = await db.SavedQuestions
            .FirstOrDefaultAsync(sq => sq.UserId == userId && sq.QuestionId == questionId, cancellationToken);

        if (existing == null)
        {
            // Idempotent: Unsaving an unsaved question succeeds with IsSaved = false
            return new SaveQuestionResult(false, null, null);
        }

        db.SavedQuestions.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);

        return new SaveQuestionResult(false, null, null);
    }

    public async Task<bool> IsQuestionSavedAsync(int userId, int questionId, CancellationToken cancellationToken = default)
    {
        return await db.SavedQuestions
            .AnyAsync(sq => sq.UserId == userId && sq.QuestionId == questionId, cancellationToken);
    }
}
