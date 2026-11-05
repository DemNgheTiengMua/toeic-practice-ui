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
    private sealed record OneQuestionPlacement(int ExamId, int QuestionId, int CorrectOptionId);

    /// <param name="questionType">`mcq` for a synchronously scorable question, `free_response` for one
    /// that goes to pending AI.</param>
    private async Task<OneQuestionPlacement> SeedOneQuestionPlacementAsync(
        int certId, string certCode, string questionType)
    {
        await database.SqlAsync($"""
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = {certId});
            DECLARE @sec INT = (SELECT TOP 1 SectionId FROM Sections WHERE CertId = {certId} AND SkillId = @skill);
            -- #171/D10: a skill with no scale refuses at scoring time, so the fixture must configure one
            UPDATE Skills SET MinScaledScore = 100, MaxScaledScore = 500, ScoreStep = 50 WHERE SkillId = @skill;
            -- Bands LOW/HIGH already exist from SeedCertWithNamedSkillsAsync; adding another range
            -- would overlap and trg_ScoreBands_NoOverlap would refuse the fixture, not the thing under test.
            INSERT INTO ScoreScales (CertId, SkillId, RawScore, ScaledScore)
              VALUES ({certId}, @skill, 0, 100), ({certId}, @skill, 1, 300);
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
              VALUES ({certId}, N'Placement {certCode}', 'published', 30, 'placement', 1);
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
              VALUES (@exam, {certId}, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
              VALUES (@grp, @exam, @sec, @skill, N'Pick one.', 2, '{questionType}', 1);
            DECLARE @q INT = SCOPE_IDENTITY();
            -- FK_Options_QuestionType: only an mcq question may carry answer options
            IF '{questionType}' = 'mcq'
              INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect)
                VALUES (@q, 'mcq', 'A', N'Right', 1), (@q, 'mcq', 'B', N'Wrong', 0);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var examId = await db.Exams.AsNoTracking()
            .Where(e => e.CertId == certId && e.Purpose == "placement" && e.Name == $"Placement {certCode}")
            .Select(e => e.ExamId).SingleAsync();
        var questionId = await db.Questions.AsNoTracking()
            .Where(q => q.ExamId == examId).Select(q => q.QuestionId).SingleAsync();
        // a free-response question carries no options, so the correct one is absent by design
        var correctOptionId = await db.QuestionOptions.AsNoTracking()
            .Where(o => o.QuestionId == questionId && o.IsCorrect == true)
            .Select(o => (int?)o.OptionId).FirstOrDefaultAsync() ?? 0;

        return new OneQuestionPlacement(examId, questionId, correctOptionId);
    }
}
