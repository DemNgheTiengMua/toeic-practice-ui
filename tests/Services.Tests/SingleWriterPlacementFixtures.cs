using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>
/// Fixtures for the #191 single-writer tests: a placement with exactly one question, so a submission
/// can be driven end to end without a multi-section blueprint.
/// </summary>
public sealed partial class LearnerServiceTests
{
    /// <param name="questionId">The <c>ExamItemId</c> - on a pool paper a position IS the question, and
    /// the engine addresses an answer by it.</param>
    private sealed record OneQuestionPlacement(int ExamId, int QuestionId, int CorrectOptionId);

    /// <param name="questionType">`mcq` for a synchronously scorable question, `free_response` for one
    /// that goes to pending AI.</param>
    private async Task<OneQuestionPlacement> SeedOneQuestionPlacementAsync(
        int certId, string certCode, string questionType)
    {
        await database.SqlAsync($"""
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = {certId});
            -- #171/D10: a skill with no scale refuses at scoring time, so the fixture must configure one
            UPDATE Skills SET MinScaledScore = 100, MaxScaledScore = 500, ScoreStep = 50 WHERE SkillId = @skill;
            -- Bands LOW/HIGH already exist from SeedCertWithNamedSkillsAsync; adding another range
            -- would overlap and trg_ScoreBands_NoOverlap would refuse the fixture, not the thing under test.
            INSERT INTO ScoreScales (CertId, SkillId, RawScore, ScaledScore)
              VALUES ({certId}, @skill, 0, 100), ({certId}, @skill, 1, 300);
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
              VALUES ({certId}, N'Placement {certCode}', 'published', 30, 'placement', 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var exam = await db.Exams.AsNoTracking()
            .Where(e => e.CertId == certId && e.Purpose == "placement" && e.Name == $"Placement {certCode}")
            .SingleAsync();

        // #272: the position is a pool binding, authored the way IAdminItemService would author it.
        // The section names the modality, so the item's type and render format come out of the pool's
        // own rules rather than being restated here.
        var section = await db.Sections.AsNoTracking()
            .Where(s => s.CertId == certId).OrderBy(s => s.DisplayOrder).FirstAsync();
        var position = questionType == "mcq"
            ? await PoolFixture.AddMcqAsync(
                db, exam.ExamId, certId, section.SectionId, section.Modality, "Pick one.", displayOrder: 1)
            : await PoolFixture.AddProductiveAsync(
                db, exam.ExamId, certId, section.SectionId, section.Modality, "Pick one.", displayOrder: 1);

        // a free-response position carries no options, so the correct one is absent by design
        var correctOptionId = questionType == "mcq"
            ? await PoolFixture.OptionIdAsync(db, exam.ExamId, position.ExamItemId, "A")
            : 0;

        return new OneQuestionPlacement(exam.ExamId, position.ExamItemId, correctOptionId);
    }
}
