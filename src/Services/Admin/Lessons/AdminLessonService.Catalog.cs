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

    public async Task<IReadOnlyList<AdminLessonSummaryDto>> GetLessonsAsync(int? certId = null, int? skillId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Lessons.AsNoTracking().AsQueryable();

        if (certId.HasValue)
        {
            query = query.Where(l => l.CertId == certId.Value);
        }

        if (skillId.HasValue)
        {
            query = query.Where(l => l.SkillId == skillId.Value);
        }

        return await query
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.LessonId)
            .Select(l => new AdminLessonSummaryDto(
                l.LessonId,
                l.CertId,
                l.Cert.Code,
                l.SkillId,
                l.Skill.Code,
                l.Skill.Name,
                l.CategoryId,
                l.Category != null ? l.Category.Code : null,
                l.Category != null ? l.Category.Name : null,
                l.Title,
                l.Summary,
                l.EstimatedMinutes,
                l.DisplayOrder,
                l.LessonCards.Count,
                l.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminLessonDetailDto?> GetLessonAsync(int lessonId, CancellationToken cancellationToken = default)
    {
        var lesson = await db.Lessons.AsNoTracking()
            .Include(l => l.Cert)
            .Include(l => l.Skill)
            .Include(l => l.Category)
            .Include(l => l.LessonCards)
            .FirstOrDefaultAsync(l => l.LessonId == lessonId, cancellationToken);

        if (lesson is null)
        {
            return null;
        }

        var cards = lesson.LessonCards
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.CardId)
            .Select(c => new LessonCardDto(
                c.CardId,
                c.CardType,
                c.Title,
                c.ContentMarkdown,
                c.QuestionPrompt,
                c.OptionA,
                c.OptionB,
                c.OptionC,
                c.OptionD,
                c.CorrectOption,
                c.Explanation,
                c.DisplayOrder,
                c.Rationale))
            .ToList();

        return new AdminLessonDetailDto(
            lesson.LessonId,
            lesson.CertId,
            lesson.Cert.Code,
            lesson.SkillId,
            lesson.Skill.Code,
            lesson.CategoryId,
            lesson.Category?.Code,
            lesson.Title,
            lesson.Summary,
            lesson.EstimatedMinutes,
            lesson.DisplayOrder,
            lesson.CreatedAt,
            cards);
    }

    public async Task<AdminLessonFormOptionsDto> GetFormOptionsAsync(int? certId = null, CancellationToken cancellationToken = default)
    {
        var certs = await db.Certificates.AsNoTracking()
            .OrderBy(c => c.CertId)
            .Select(c => new AdminCertificateOptionDto(c.CertId, c.Code, c.Name))
            .ToListAsync(cancellationToken);

        var skillsQuery = db.Skills.AsNoTracking();
        if (certId.HasValue)
        {
            skillsQuery = skillsQuery.Where(s => s.CertId == certId.Value);
        }
        var skills = await skillsQuery
            .OrderBy(s => s.CertId)
            .ThenBy(s => s.DisplayOrder)
            .Select(s => new AdminLessonSkillOptionDto(s.SkillId, s.CertId, s.Code, s.Name, s.Modality))
            .ToListAsync(cancellationToken);

        var catQuery = db.QuestionCategories.AsNoTracking();
        if (certId.HasValue)
        {
            catQuery = catQuery.Where(c => c.CertId == certId.Value);
        }
        var categories = await catQuery
            .OrderBy(c => c.CertId)
            .ThenBy(c => c.Code)
            .Select(c => new AdminLessonCategoryOptionDto(c.CategoryId, c.CertId, c.Code, c.Name))
            .ToListAsync(cancellationToken);

        return new AdminLessonFormOptionsDto(certs, skills, categories);
    }
}
