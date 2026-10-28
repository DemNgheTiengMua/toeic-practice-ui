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

    public async Task<LessonManifestDto> ExportLessonAsync(int lessonId, CancellationToken cancellationToken = default)
    {
        var lesson = await db.Lessons.AsNoTracking()
            .Include(l => l.Skill)
            .Include(l => l.Category)
            .Include(l => l.LessonCards)
            .FirstOrDefaultAsync(l => l.LessonId == lessonId, cancellationToken);

        if (lesson is null)
        {
            throw new AdminLessonException(ApiError.Codes.LessonNotFound, $"Lesson {lessonId} was not found.", 404);
        }

        var cards = lesson.LessonCards
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.CardId)
            .Select(c => new LessonManifestCardDto(
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
                c.Rationale))
            .ToList();

        var item = new LessonManifestItemDto(
            lesson.Title,
            lesson.Skill.Code,
            lesson.Category?.Code,
            lesson.Summary,
            lesson.EstimatedMinutes,
            lesson.DisplayOrder,
            cards);

        return new LessonManifestDto(new[] { item });
    }

    public LessonManifestDto GetTemplate()
    {
        return new LessonManifestDto(new[]
        {
            new LessonManifestItemDto(
                "Present Simple vs Present Continuous",
                "reading",
                "GRAMMAR",
                "Master when to use present simple for habits and present continuous for ongoing actions.",
                5,
                0,
                new List<LessonManifestCardDto>
                {
                    new(
                        "rule",
                        "The Core Principle",
                        "### Present Simple vs Present Continuous\n\nUse **Present Simple** for habits, routines, and permanent states.\n\nUse **Present Continuous** for actions happening right now or temporary situations."
                    ),
                    new(
                        "pitfall",
                        "Common Traps: Stative Verbs",
                        "### Beware of Stative Verbs!\n\nVerbs of perception, emotion, and possession (e.g., *know*, *like*, *belong*, *understand*) are rarely used in continuous tenses.\n\n- ❌ *I am knowing the answer.*\n- ✔️ *I know the answer.*"
                    ),
                    new(
                        "example",
                        "Real Exam Context",
                        "### Workplace Scenario\n\n*\"The accounting department **audits** (Present Simple) our branch annually, but they **are auditing** (Present Continuous) this week due to regulatory changes.\"*"
                    ),
                    new(
                        "micro_check",
                        "Quick Check",
                        "Select the correct verb form for the sentence.",
                        QuestionPrompt: "Please quiet down; Ms. Vance _______ a confidential conference call with overseas investors.",
                        OptionA: "conducts",
                        OptionB: "is conducting",
                        OptionC: "conducted",
                        OptionD: "has conducted",
                        CorrectOption: "B",
                        Explanation: "The prompt 'Please quiet down' indicates an action taking place at the present moment, requiring the present continuous tense ('is conducting').",
                        Rationale: "Option A is simple present (habitual, not in-progress). Options C and D are past and perfect tenses, which do not fit the urgent immediate context."
                    )
                }
            )
        });
    }
}
